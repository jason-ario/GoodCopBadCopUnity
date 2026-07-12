using UnityEngine;

/// <summary>
/// Elevates a suspect's heartRateBpm while active. Future heart-rate tools should
/// read the value directly from SuspectCharacter, the same way RadiationScanner
/// reads radiationAmount.
/// </summary>
public class HeartRateAnomaly : VitalsAnomaly
{
    [Tooltip("The heart rate displayed by heart-rate tools while the anomaly is active.")]
    [SerializeField] private int elevatedHeartRateBpm = 140;

    private SuspectCharacter _suspect;
    private int _originalHeartRateBpm;

    private void Awake()
    {
        _suspect = GetComponentInParent<SuspectCharacter>();

        if (_suspect == null)
            Debug.LogWarning($"[HeartRateAnomaly] No SuspectCharacter found in parent hierarchy of '{gameObject.name}'. Anomaly will not function.", this);
    }

    /// <summary>Stores the suspect's current heart rate and replaces it with the elevated value.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_suspect == null) return;

        _originalHeartRateBpm = _suspect.heartRateBpm;
        _suspect.heartRateBpm = elevatedHeartRateBpm;
    }

    /// <summary>Restores the suspect's heart rate to what it was before activation.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (_suspect == null) return;

        _suspect.heartRateBpm = _originalHeartRateBpm;
    }
}
