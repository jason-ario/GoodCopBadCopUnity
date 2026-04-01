using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class HierarchyAutoSections
{
    private static readonly Color RowColor = new Color(0.16f, 0.16f, 0.16f, 1f);
    private static readonly Color BorderColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    private static readonly Color SelectedRowColor = new Color(0.24f, 0.30f, 0.38f, 1f);

    static HierarchyAutoSections()
    {
        EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        if (EditorUtility.InstanceIDToObject(instanceID) is not GameObject go)
            return;

        if (!go.name.Contains("---"))
            return;

        bool isSelected = Selection.instanceIDs != null && System.Array.IndexOf(Selection.instanceIDs, instanceID) >= 0;
        Color bg = isSelected ? SelectedRowColor : RowColor;

        string label = CleanName(go.name);
        if (string.IsNullOrWhiteSpace(label))
            label = "SECTION";

        // Keep Unity's foldout arrow / icon area alone.
        // Only replace the text area.
        float textStartX = selectionRect.x + 16f;
        Rect textArea = new Rect(
            textStartX,
            selectionRect.y,
            Mathf.Max(0f, EditorGUIUtility.currentViewWidth - textStartX),
            selectionRect.height
        );

        // Paint only the text zone, not the whole row
        EditorGUI.DrawRect(textArea, bg);

        // Top / bottom borders only across the styled area
        EditorGUI.DrawRect(new Rect(textArea.x, textArea.y, textArea.width, 1f), BorderColor);
        EditorGUI.DrawRect(new Rect(textArea.x, textArea.yMax - 1f, textArea.width, 1f), BorderColor);

        // Draw replacement label
        Rect labelRect = new Rect(
            textArea.x + 6f,
            textArea.y,
            textArea.width - 6f,
            textArea.height
        );

        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            richText = false
        };
        style.normal.textColor = TextColor;

        EditorGUI.LabelField(labelRect, label.ToUpperInvariant(), style);
    }

    private static string CleanName(string rawName)
    {
        string cleaned = rawName.Replace("---", "");
        cleaned = cleaned.Replace("_", " ");
        cleaned = cleaned.Trim();
        return cleaned;
    }
}