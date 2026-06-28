using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect produces disembodied or sourceless voices.
/// Activating the anomaly starts a periodic whisper sequence on the scene-level
/// <see cref="DisembodiedVoicesController"/>; deactivating it stops playback.
/// </summary>
public class DisembodiedVoicesAnomaly : SupernaturalAnomaly
{
    [Tooltip("Scene-level controller that owns the audio source and drives the whisper interval loop.")]
    [SerializeField] private DisembodiedVoicesController voicesController;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (voicesController == null)
        {
            Debug.LogWarning($"[DisembodiedVoicesAnomaly] voicesController is not assigned on '{gameObject.name}'.", this);
            return;
        }

        voicesController.StartWhispering();
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        voicesController?.StopWhispering();
    }

    [ContextMenu("Activate Anomaly")]
    private void ActivateAnomalyDebug() => ActivateAnomaly();
}
