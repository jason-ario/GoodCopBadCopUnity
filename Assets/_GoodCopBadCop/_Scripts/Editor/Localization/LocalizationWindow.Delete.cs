using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private readonly HashSet<string> _selectedForDelete = new HashSet<string>();

    /// <summary>Selection and bulk-delete controls for the Strings tab toolbar.</summary>
    private void DrawDeleteToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(_selectedForDelete.Count + " selected", EditorStyles.boldLabel, GUILayout.Width(80));
            if (GUILayout.Button("Select page", EditorStyles.miniButtonLeft, GUILayout.Width(80)))
                foreach (var e in _filtered.Skip(_page * PageSize).Take(PageSize)) _selectedForDelete.Add(e.key);
            if (GUILayout.Button("Select all filtered (" + _filtered.Count + ")", EditorStyles.miniButtonMid, GUILayout.Width(170)))
                foreach (var e in _filtered) _selectedForDelete.Add(e.key);
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50))) _selectedForDelete.Clear();

            GUILayout.FlexibleSpace();

            var prev = GUI.backgroundColor;
            GUI.enabled = _selectedForDelete.Count > 0;
            GUI.backgroundColor = LocUI.Bad;
            if (GUILayout.Button("Delete selected (" + _selectedForDelete.Count + ")", GUILayout.Width(150), GUILayout.Height(20)))
                ConfirmDelete(_selectedForDelete.ToList());
            GUI.backgroundColor = prev;
            GUI.enabled = true;
        }
    }

    private void ConfirmDelete(List<string> keys)
    {
        if (keys.Count == 0) return;
        string what = keys.Count == 1 ? "string '" + keys[0] + "'" : keys.Count + " strings";
        if (!EditorUtility.DisplayDialog("Delete " + what + "?",
                "This removes " + what + " from the localization sheets, including all translations, and drops their source bindings.\n\n"
                + "The text in your scenes, prefabs and assets is not touched, so a later scan will offer it again unless you clear it there.\n\n"
                + "This cannot be undone from the editor (use version control to restore sheets).",
                "Delete", "Cancel")) return;
        DeleteKeys(keys);
    }

    private void DeleteKeys(List<string> keys)
    {
        var set = new HashSet<string>(keys);
        var sheets = new HashSet<string>();
        foreach (var k in keys)
            if (_table.TryGet(k, out var e)) { sheets.Add(e.sheet); _table.Remove(k); }

        if (_pendingSourceKey != null && set.Contains(_pendingSourceKey)) _pendingSourceKey = null;
        foreach (var k in keys) _selectedForDelete.Remove(k);
        if (_selectedKey != null && set.Contains(_selectedKey)) _selectedKey = null;

        LocBindings.RemoveKeys(set);
        LocBindings.Save();
        LocalizationSheetIO.SaveSheets(_table, sheets);
        foreach (var s in sheets) _dirtySheets.Remove(s);

        _filterDirty = true;
        int maxPage = Mathf.Max(0, Mathf.CeilToInt(_table.entries.Count / (float)PageSize) - 1);
        _page = Mathf.Min(_page, maxPage);
        _status = "Deleted " + keys.Count + " string(s). Run Build runtime table to update the game.";
        GUI.FocusControl(null);
    }
}
