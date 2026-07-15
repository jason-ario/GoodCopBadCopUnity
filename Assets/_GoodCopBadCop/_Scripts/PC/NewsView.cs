using TMPro;
using UnityEngine;

public sealed class NewsView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI header;
    [SerializeField] private TextMeshProUGUI subheader;
    [SerializeField] private TextMeshProUGUI description;
    [SerializeField] private TextMeshProUGUI footer;

    public void Show(TerminalNewsEntry newsEntry)
    {
        NewspaperContentScriptable content = newsEntry?.Content;
        SetText(header, content != null ? content.headerText : "NEWS ENTRY UNAVAILABLE");
        SetText(subheader, content != null ? content.subheaderText : string.Empty);
        SetText(description, content != null ? content.descriptionText : string.Empty);
        SetText(footer, content != null ? content.footerText : string.Empty);
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
            label.text = value;
    }
}
