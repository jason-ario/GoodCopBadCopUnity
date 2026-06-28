using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class HierarchyAutoSections
{
    private static readonly Color RowColor = new Color(0.16f, 0.16f, 0.16f, 1f);
    private static readonly Color BorderColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    private static readonly Color SelectedRowColor = new Color(0.24f, 0.30f, 0.38f, 1f);
    private static readonly Color InactiveRowColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);
    static HierarchyAutoSections()
    {
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyGUI;
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void OnHierarchyGUI(EntityId entityId, Rect selectionRect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        if (EditorUtility.EntityIdToObject(entityId) is not GameObject go)
            return;

        if (!go.name.Contains("---"))
            return;

        bool isActive = go.activeInHierarchy;

    // If inactive → use the old SelectedRowColor look
        Color bg = isActive ? RowColor : SelectedRowColor;
        
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
        Color textColor = isActive 
            ? TextColor 
            : new Color(0.8f, 0.85f, 0.9f, 1f); // slightly softer bluish tone

        style.normal.textColor = textColor;
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