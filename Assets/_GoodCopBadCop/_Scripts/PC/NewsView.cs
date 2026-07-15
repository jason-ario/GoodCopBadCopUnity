using TMPro;
using UnityEngine;

public sealed class NewsView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI header;
    [SerializeField] private TextMeshProUGUI date;
    [SerializeField] private TextMeshProUGUI subheader;
    [SerializeField] private TextMeshProUGUI description;
    [SerializeField] private TextMeshProUGUI footer;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Show(TerminalNewsEntry newsEntry)
    {
        ResolveReferences();

        NewspaperContentScriptable content = newsEntry?.Content;
        SetText(header, content != null ? content.headerText : "NEWS ENTRY UNAVAILABLE");
        SetText(date, newsEntry != null ? "Date: " + newsEntry.Date : "Date: unknown");
        SetText(subheader, content != null ? content.subheaderText : string.Empty);
        SetText(description, content != null ? content.descriptionText : string.Empty);
        SetText(footer, content != null ? content.footerText : string.Empty);
    }

    private void ResolveReferences()
    {
        if (header == null)
            header = FindDescendantByName(transform, "Header")?.GetComponent<TextMeshProUGUI>();

        if (date == null)
            date = FindDescendantByName(transform, "Date")?.GetComponent<TextMeshProUGUI>();

        if (subheader == null)
            subheader = FindDescendantByName(transform, "Subheader")?.GetComponent<TextMeshProUGUI>();

        if (description == null)
            description = FindDescendantByName(transform, "Description")?.GetComponent<TextMeshProUGUI>();

        if (footer == null)
            footer = FindDescendantByName(transform, "Footer")?.GetComponent<TextMeshProUGUI>();
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
            label.text = value;
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform result = FindDescendantByName(child, targetName);
            if (result != null)
                return result;
        }

        return null;
    }
}