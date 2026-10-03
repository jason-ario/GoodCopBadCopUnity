using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One line in the subject detail popup: a stamp-style status word (CAUGHT / MISSED / ...),
/// the anomaly or category name, and a small note (usually the checklist category).
/// </summary>
public class ShiftReportDetailLine : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI noteText;

    public void Set(string status, Color statusColor, string label, string note, Color tint)
    {
        if (statusText != null)
        {
            bool hasStatus = !string.IsNullOrEmpty(status);
            statusText.gameObject.SetActive(hasStatus);
            statusText.text = status ?? string.Empty;
            statusText.color = statusColor;
        }

        if (labelText != null)
            labelText.text = label ?? string.Empty;

        if (noteText != null)
        {
            bool hasNote = !string.IsNullOrEmpty(note);
            noteText.gameObject.SetActive(hasNote);
            noteText.text = note ?? string.Empty;
        }

        if (background != null)
        {
            background.color = tint;
            background.enabled = tint.a > 0.001f;
        }
    }
}
