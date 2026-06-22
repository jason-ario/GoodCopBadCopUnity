using UnityEngine;

/// <summary>
/// Marks a suspect as having an elevated body temperature anomaly.
/// The Thermometer reads this component to decide which temperature to display —
/// active anomaly suspects show ElevatedTemperature, all other suspects show normal
/// human body temperature. No shader changes required; this is purely data-driven.
/// </summary>
public class HighTemperatureAnomaly : VitalsAnomaly
{
    [Tooltip("Temperature displayed on the thermometer when this anomaly is active.")]
    [SerializeField] private float elevatedTemperature = 45.5f;

    [Tooltip("Random jitter range (±) applied to each thermometer reading while active.")]
    [SerializeField] private float jitterRange = 1.5f;

    /// <summary>Whether the anomaly is currently active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Target temperature the thermometer ramps toward for this suspect.</summary>
    public float ElevatedTemperature => elevatedTemperature;

    /// <summary>Per-reading jitter range (±) applied on top of ElevatedTemperature.</summary>
    public float JitterRange => jitterRange;

    /// <summary>Activates the temperature anomaly — subsequent thermometer scans will show an elevated reading.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        IsActive = true;
    }

    /// <summary>Deactivates the anomaly — thermometer will return to showing normal temperature for this suspect.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        IsActive = false;
    }
}
