using TMPro;
using UnityEngine;

/// <summary>
/// Represents a single threat row in the guidebook task list canvas.
/// Call Bind() to populate the row from an ISystemicThreat.
/// The row self-manages its high-threat indicator by subscribing to TaskRegistry.OnTaskStateChanged.
/// </summary>
public class GuidebookTaskRow : MonoBehaviour
{
    [Tooltip("Child GameObject shown when threat level is at or above 50%, hidden otherwise.")]
    [SerializeField] private GameObject _checkmark;

    [Tooltip("Displays the threat name in uppercase bold.")]
    [SerializeField] private TextMeshProUGUI _nameLabel;

    [Tooltip("Displays the short threat description (e.g. 'Active mutants: 3/10').")]
    [SerializeField] private TextMeshProUGUI _descriptionLabel;

    [Tooltip("Displays the current threat level as a percentage.")]
    [SerializeField] private TextMeshProUGUI _rewardLabel;

    private ISystemicThreat _threat;

    private void OnEnable()
    {
        TaskRegistry.OnTaskStateChanged += OnTaskStateChanged;
        Debug.Log($"[GuidebookTaskRow] Subscribed to OnTaskStateChanged. Threat: {_threat?.ThreatName ?? "none"}", this);

        // Re-sync in case the threat level changed while the panel was closed.
        if (_threat != null)
            SetHighThreat(_threat.ThreatLevel >= 0.5f);
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskStateChanged -= OnTaskStateChanged;
    }

    /// <summary>Populates all row fields from the given threat and syncs the high-threat indicator.</summary>
    public void Bind(ISystemicThreat threat)
    {
        if (threat == null) return;

        _threat = threat;

        if (_nameLabel != null)
            _nameLabel.text = threat.ThreatName.ToUpper();

        if (_descriptionLabel != null)
            _descriptionLabel.text = threat.ThreatDescription;

        if (_rewardLabel != null)
            _rewardLabel.text = $"{threat.ThreatLevel:P0}";

        Debug.Log($"[GuidebookTaskRow] Bind called. Threat: '{threat.ThreatName}', Level: {threat.ThreatLevel:P0}", this);
        SetHighThreat(threat.ThreatLevel >= 0.5f);
    }

    private void OnTaskStateChanged()
    {
        if (_threat != null)
            Bind(_threat);
    }

    /// <summary>Shows or hides the high-threat warning indicator.</summary>
    private void SetHighThreat(bool high)
    {
        Debug.Log($"[GuidebookTaskRow] SetHighThreat({high}). Indicator: {(_checkmark != null ? _checkmark.name : "NULL")}", this);
        if (_checkmark != null)
            _checkmark.SetActive(high);
    }
}
