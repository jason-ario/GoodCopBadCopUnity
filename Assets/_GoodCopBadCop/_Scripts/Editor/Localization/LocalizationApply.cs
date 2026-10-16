using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Attaches LocalizedText, binds keys to origin fields and writes edits back to the origin asset.</summary>
public static class LocalizationApply
{
    // ---- Hierarchy paths: "Root/Child/Child#1" where #n disambiguates same-named siblings ----

    public static string ObjectPathOf(Transform t)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null; cur = cur.parent)
        {
            string name = cur.name;
            if (cur.parent != null)
            {
                int idx = 0;
                for (int i = 0; i < cur.GetSiblingIndex(); i++)
                    if (cur.parent.GetChild(i).name == cur.name) idx++;
                if (idx > 0) name += "#" + idx;
            }
            else
            {
                // scene roots: disambiguate in the same way
                var scene = cur.gameObject.scene;
                if (scene.IsValid())
                {
                    int idx = 0;
                    foreach (var r in scene.GetRootGameObjects())
                    {
                        if (r.transform == cur) break;
                        if (r.name == cur.name) idx++;
                    }
                    if (idx > 0) name += "#" + idx;
                }
            }
            parts.Add(name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static Transform FindChild(Transform parent, string segment)
    {
        string name = segment;
        int want = 0;
        int hash = segment.LastIndexOf('#');
        if (hash > 0 && int.TryParse(segment.Substring(hash + 1), out int n)) { name = segment.Substring(0, hash); want = n; }
        int seen = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name != name) continue;
            if (seen == want) return c;
            seen++;
        }
        return null;
    }

    public static Transform FindByPath(IEnumerable<GameObject> roots, string path)
    {
        var segs = path.Split('/');
        if (segs.Length == 0) return null;

        string rootName = segs[0];
        int want = 0;
        int hash = rootName.LastIndexOf('#');
        if (hash > 0 && int.TryParse(rootName.Substring(hash + 1), out int n)) { want = n; rootName = rootName.Substring(0, hash); }

        Transform cur = null;
        int seen = 0;
        foreach (var r in roots)
        {
            if (r.name != rootName) continue;
            if (seen++ == want) { cur = r.transform; break; }
        }
        for (int i = 1; cur != null && i < segs.Length; i++) cur = FindChild(cur, segs[i]);
        return cur;
    }

    /// <summary>Finds the GameObject a binding points at. For prefabs the contents are loaded and must be unloaded by the caller.</summary>
    public static GameObject ResolveObject(LocBinding b, out GameObject prefabContentsRoot, out Scene scene)
    {
        prefabContentsRoot = null;
        scene = default;
        if (string.IsNullOrEmpty(b.assetPath) || string.IsNullOrEmpty(b.objectPath)) return null;

        if (b.assetPath.EndsWith(".unity"))
        {
            scene = SceneManager.GetSceneByPath(b.assetPath);
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var t = FindByPath(scene.GetRootGameObjects(), b.objectPath);
            return t != null ? t.gameObject : null;
        }
        if (b.assetPath.EndsWith(".prefab"))
        {
            prefabContentsRoot = PrefabUtility.LoadPrefabContents(b.assetPath);
            var t = FindByPath(new[] { prefabContentsRoot }, b.objectPath);
            if (t == null) { PrefabUtility.UnloadPrefabContents(prefabContentsRoot); prefabContentsRoot = null; return null; }
            return t.gameObject;
        }
        return null;
    }

    public static TMP_Text ResolveTmp(LocBinding b, out GameObject prefabContentsRoot, out Scene scene)
    {
        var go = ResolveObject(b, out prefabContentsRoot, out scene);
        var tmp = go != null ? go.GetComponent<TMP_Text>() : null;
        if (tmp == null && prefabContentsRoot != null) { PrefabUtility.UnloadPrefabContents(prefabContentsRoot); prefabContentsRoot = null; }
        return tmp;
    }

    public static Component FindComponent(GameObject go, string typeName, int index)
    {
        int seen = 0;
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null || c.GetType().FullName != typeName) continue;
            if (seen++ == index) return c;
        }
        return null;
    }

    // ---- Attaching LocalizedText ----

    /// <summary>Adds / updates LocalizedText for all given bindings (TMP kind only). Returns the number applied.</summary>
    public static int AttachLocalizedText(IEnumerable<LocBinding> bindings)
    {
        int applied = 0;
        foreach (var group in bindings.Where(b => b.kind == "TMP").GroupBy(b => b.assetPath))
        {
            if (group.Key.EndsWith(".unity"))
            {
                var scene = SceneManager.GetSceneByPath(group.Key);
                if (!scene.IsValid() || !scene.isLoaded) { Debug.LogWarning("[Localization] Scene not loaded, skipped: " + group.Key); continue; }
                foreach (var b in group)
                {
                    var t = FindByPath(scene.GetRootGameObjects(), b.objectPath);
                    var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
                    if (tmp == null) continue;
                    var lt = tmp.GetComponent<LocalizedText>();
                    if (lt == null) lt = Undo.AddComponent<LocalizedText>(tmp.gameObject);
                    else Undo.RecordObject(lt, "Set localization key");
                    lt.key = b.key;
                    EditorUtility.SetDirty(lt);
                    applied++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
            }
            else if (group.Key.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(group.Key);
                try
                {
                    bool changed = false;
                    foreach (var b in group)
                    {
                        var t = FindByPath(new[] { root }, b.objectPath);
                        var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
                        if (tmp == null) continue;
                        var lt = tmp.GetComponent<LocalizedText>();
                        if (lt == null) lt = tmp.gameObject.AddComponent<LocalizedText>();
                        lt.key = b.key;
                        changed = true;
                        applied++;
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, group.Key);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        return applied;
    }

    // ---- Write-back of source edits ----

    /// <summary>Writes new source text to every binding of the key. Returns the number of origins updated.</summary>
    public static int WriteSource(string key, string newText)
    {
        int updated = 0;
        foreach (var b in LocBindings.For(key))
        {
            if (b.kind == "SO")
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(b.assetPath);
                if (obj == null) continue;
                var so = new SerializedObject(obj);
                var prop = so.FindProperty(b.propertyPath);
                if (prop == null || prop.propertyType != SerializedPropertyType.String) continue;
                Undo.RecordObject(obj, "Edit localized source");
                prop.stringValue = newText;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(obj);
                updated++;
            }
            else if (b.kind == "MB")
            {
                var go = ResolveObject(b, out var prefabRoot, out var scene);
                if (go == null) continue;
                var comp = FindComponent(go, b.componentType, b.componentIndex);
                var so = comp != null ? new SerializedObject(comp) : null;
                var prop = so != null ? so.FindProperty(b.propertyPath) : null;
                if (prop != null && prop.propertyType == SerializedPropertyType.String)
                {
                    if (prefabRoot == null) Undo.RecordObject(comp, "Edit localized source");
                    prop.stringValue = newText;
                    so.ApplyModifiedProperties();
                    if (prefabRoot != null) PrefabUtility.SaveAsPrefabAsset(prefabRoot, b.assetPath);
                    else { EditorUtility.SetDirty(comp); EditorSceneManager.MarkSceneDirty(scene); }
                    updated++;
                }
                if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
            else if (b.kind == "TMP")
            {
                var tmp = ResolveTmp(b, out var prefabRoot, out var scene);
                if (tmp == null) continue;
                if (prefabRoot != null)
                {
                    tmp.text = newText;
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, b.assetPath);
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
                else
                {
                    Undo.RecordObject(tmp, "Edit localized source");
                    tmp.text = newText;
                    EditorUtility.SetDirty(tmp);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                updated++;
            }
        }
        if (updated > 0) AssetDatabase.SaveAssets();
        return updated;
    }

    public static void Ping(string key)
    {
        var b = LocBindings.For(key).FirstOrDefault();
        if (b == null) { Debug.Log("[Localization] No origin recorded for '" + key + "'."); return; }

        if ((b.kind == "TMP" || b.kind == "MB") && b.assetPath.EndsWith(".unity"))
        {
            var scene = SceneManager.GetSceneByPath(b.assetPath);
            if (scene.IsValid() && scene.isLoaded)
            {
                var t = FindByPath(scene.GetRootGameObjects(), b.objectPath);
                if (t != null) { Selection.activeGameObject = t.gameObject; EditorGUIUtility.PingObject(t.gameObject); return; }
            }
        }
        var asset = AssetDatabase.LoadAssetAtPath<Object>(b.assetPath);
        if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
    }
}
