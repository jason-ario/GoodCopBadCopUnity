using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fonts tab: font role renaming. A role name is referenced by string from each language's role fonts and from
// LocalizedText.fontRole overrides in scenes/prefabs, so a rename rewrites all of them.
public partial class LocalizationWindow
{
    private const string RoleRenameControl = "LocFontRoleRename";
    private static readonly Regex RoleNameRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _\-]*$");

    private int _renamingRole = -1;
    private string _renameRoleText = "";
    private bool _focusRoleRename;

    private void HandleRolePillClick(int index, Rect pillRect)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || !pillRect.Contains(e.mousePosition)) return;

        if (e.button == 0 && e.clickCount == 2)
        {
            BeginRoleRename(index);
            e.Use();
        }
        else if (e.button == 1)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => BeginRoleRename(index));
            menu.ShowAsContext();
            e.Use();
        }
    }

    private void BeginRoleRename(int index)
    {
        _renamingRole = index;
        _renameRoleText = _settings.fontRoles[index].name;
        _focusRoleRename = true;
        Repaint();
    }

    private void CancelRoleRename()
    {
        _renamingRole = -1;
        GUI.FocusControl(null);
        Repaint();
    }

    private void DrawRoleRenameField(int index)
    {
        // Handle keys before the text field so it can't swallow Enter/Escape.
        var e = Event.current;
        bool focused = GUI.GetNameOfFocusedControl() == RoleRenameControl;
        bool submit = false, cancel = false;
        if (focused && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { submit = true; e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { cancel = true; e.Use(); }
        }

        string error = ValidateRoleName(_renameRoleText, index);

        GUI.SetNextControlName(RoleRenameControl);
        _renameRoleText = EditorGUILayout.TextField(new GUIContent("", error ?? "Enter to rename, Esc to cancel"), _renameRoleText, GUILayout.Width(120));
        if (_focusRoleRename && e.type == EventType.Repaint)
        {
            EditorGUI.FocusTextInControl(RoleRenameControl);
            _focusRoleRename = false;
        }

        GUI.enabled = error == null;
        if (GUILayout.Button(new GUIContent("\u2713", error ?? "Rename"), EditorStyles.miniButton, GUILayout.Width(20))) submit = true;
        GUI.enabled = true;
        if (GUILayout.Button(new GUIContent("\u2715", "Cancel"), EditorStyles.miniButton, GUILayout.Width(20))) cancel = true;

        if (cancel) { CancelRoleRename(); return; }
        if (submit && error == null)
        {
            string newName = _renameRoleText.Trim();
            CancelRoleRename();
            // Defer: the rename rewrites assets and may show a dialog, which must not run mid-layout.
            EditorApplication.delayCall += () => RenameFontRole(index, newName);
        }
    }

    private string ValidateRoleName(string name, int ignoreIndex)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "Name is empty.";
        if (!RoleNameRx.IsMatch(name)) return "Use letters, digits, spaces, '-' or '_'.";
        var roles = _settings.fontRoles;
        for (int i = 0; i < roles.Count; i++)
            if (i != ignoreIndex && roles[i] != null && roles[i].name == name) return "A role named '" + name + "' already exists.";
        return null;
    }

    private void RenameFontRole(int index, string newName)
    {
        if (_settings == null || index < 0 || index >= _settings.fontRoles.Count || _settings.fontRoles[index] == null) return;
        var role = _settings.fontRoles[index];
        string oldName = role.name;
        if (oldName == newName) return;
        if (ValidateRoleName(newName, index) != null) return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _status = "Exit Play Mode to rename font roles.";
            Repaint();
            return;
        }

        string subtitleNote = oldName == "Dialogue"
            ? "\n\nNote: subtitles fall back to a role named 'Dialogue' when their font has no role. After this rename that fallback " +
              "no longer applies (subtitles still follow the role their font is assigned to)."
            : "";
        if (!EditorUtility.DisplayDialog("Rename font role",
                "Rename font role '" + oldName + "' to '" + newName + "'?\n\n" +
                "This updates every language's font for the role and every LocalizedText Font Role override. " +
                "Prefab and unloaded scene files are rewritten on disk (not undoable); open scenes are edited in memory and must be saved." +
                subtitleNote,
                "Rename", "Cancel"))
            return;

        Undo.RecordObject(_settings, "Rename font role");
        role.name = newName;
        foreach (var l in _settings.languages)
        {
            if (l == null || l.roleFonts == null) continue;
            foreach (var rf in l.roleFonts) if (rf != null && rf.role == oldName) rf.role = newName;
        }
        if (_previewRole == oldName) _previewRole = newName;
        EditorUtility.SetDirty(_settings);
        AssetDatabase.SaveAssetIfDirty(_settings);

        int files = RewriteRoleOverridesOnDisk(oldName, newName, out int fileEdits);
        int live = RenameRoleInOpenScenes(oldName, newName);

        _status = "Renamed font role '" + oldName + "' to '" + newName + "': " + fileEdits + " override(s) in " + files + " file(s)" +
                  (live > 0 ? ", " + live + " label(s) in open scenes (save the scene)." : ".");
        Repaint();
    }

    /// <summary>Rewrites fontRole values (and prefab-instance overrides of it) in prefab and unloaded scene YAML.</summary>
    private static int RewriteRoleOverridesOnDisk(string oldName, string newName, out int edits)
    {
        edits = 0;
        var loaded = new HashSet<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!string.IsNullOrEmpty(s.path)) loaded.Add(s.path);
        }

        string o = Regex.Escape(oldName);
        string value = "(?:" + o + "|'" + o + "'|\"" + o + "\")(?=[ \\t]*\\r?$)";
        var direct = new Regex(@"(?m)^([ \t]*fontRole: )" + value);
        var modification = new Regex(@"(?m)(^[ \t]*propertyPath: fontRole[ \t]*\r?\n[ \t]*value: )" + value);
        string replacement = "${1}" + newName;

        var paths = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p => !loaded.Contains(p)).ToList();
        var changed = new List<string>();
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (i % 50 == 0) EditorUtility.DisplayProgressBar("Renaming font role", paths[i], (float)i / paths.Count);
                string abs = LocalizationPaths.Abs(paths[i]);
                string text;
                try { text = File.ReadAllText(abs); } catch { continue; }
                if (text.IndexOf("fontRole", System.StringComparison.Ordinal) < 0) continue;

                int count = direct.Matches(text).Count + modification.Matches(text).Count;
                if (count == 0) continue;
                text = direct.Replace(text, replacement);
                text = modification.Replace(text, replacement);
                File.WriteAllText(abs, text);
                changed.Add(paths[i]);
                edits += count;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (changed.Count > 0)
        {
            AssetDatabase.StartAssetEditing();
            try { foreach (var p in changed) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
            finally { AssetDatabase.StopAssetEditing(); }
        }
        return changed.Count;
    }

    /// <summary>Renames fontRole on LocalizedText objects in open scenes and prefab stages (undoable).</summary>
    private static int RenameRoleInOpenScenes(string oldName, string newName)
    {
        int n = 0;
        foreach (var lt in Resources.FindObjectsOfTypeAll<LocalizedText>())
        {
            if (lt == null || lt.fontRole != oldName || EditorUtility.IsPersistent(lt)) continue;
            var scene = lt.gameObject.scene;
            if (!scene.IsValid()) continue;
            Undo.RecordObject(lt, "Rename font role");
            lt.fontRole = newName;
            PrefabUtility.RecordPrefabInstancePropertyModifications(lt);
            if (!EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.MarkSceneDirty(scene);
            n++;
        }
        return n;
    }
}
