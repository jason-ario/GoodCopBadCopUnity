using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row inside the consolidated HUD sidebar's "MAINTENANCE" section
/// (e.g. "Throw away trash   3 / 7"). Shows an icon square whose fill amount
/// and color track completion progress (empty/red when just started, full/green
/// when done), plus a label and a "current / total" counter.
///
/// Purely a display widget — <see cref="CheckpointMaintenanceHUD"/> owns the row
/// instances and feeds them progress via <see cref="SetProgress"/>.
/// </summary>
public class MaintenanceTaskRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Image iconFill;
    [SerializeField] private Color incompleteColor = new Color(0.75f, 0.15f, 0.12f);
    [SerializeField] private Color completeColor = new Color(0.15f, 0.7f, 0.25f);

    /// <summary>Sets the static row label (e.g. "Throw away trash"). Safe to call once at setup.</summary>
    public void SetLabel(string text)
    {
        if (label != null)
            label.text = text;
    }

    /// <summary>Updates the "current / total" counter and the icon's fill amount/color.</summary>
    public void SetProgress(int current, int total)
    {
        int safeTotal = Mathf.Max(total, 0);
        int safeCurrent = Mathf.Clamp(current, 0, Mathf.Max(safeTotal, current));

        if (countText != null)
            countText.text = $"{safeCurrent} / {safeTotal}";

        if (iconFill == null) return;

        float t = safeTotal <= 0 ? 0f : (float)safeCurrent / safeTotal;
        iconFill.fillAmount = Mathf.Clamp01(t);
        iconFill.color = Color.Lerp(incompleteColor, completeColor, t);
    }
}
