using UnityEngine;

/// <summary>
/// Elevates a suspect's radiationAmount when active, causing the RadiationScanner's
/// Geiger needle to spike when pointed at this character. Restores the original value
/// on deactivation. No changes to RadiationScanner are required — it already reads
/// radiationAmount directly from SuspectCharacter.
/// </summary>
public class HighRadiationAnomaly : BiologicalAnomaly
{
    [Tooltip("The radiation level displayed on the scanner while the anomaly is active.")]
    [SerializeField] private int elevatedRadiation = 85;

    private SuspectCharacter _suspect;
    private int _originalRadiation;

    private void Awake()
    {
        _suspect = GetComponentInParent<SuspectCharacter>();

        if (_suspect == null)
            Debug.LogWarning($"[HighRadiationAnomaly] No SuspectCharacter found in parent hierarchy of '{gameObject.name}'. Anomaly will not function.", this);
    }

    /// <summary>Stores the suspect's current radiation level and replaces it with the elevated value.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_suspect == null) return;

        _originalRadiation = _suspect.radiationAmount;
        _suspect.radiationAmount = elevatedRadiation;
    }

    /// <summary>Restores the suspect's radiation level to what it was before activation.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (_suspect == null) return;

        _suspect.radiationAmount = _originalRadiation;
    }
}
