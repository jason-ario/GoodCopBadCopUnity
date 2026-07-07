using GoodCopBadCop.RoomSystem;
using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect causes the booth lights to flicker.
/// Activating the anomaly starts a periodic flickering sequence on the assigned
/// <see cref="BoothFlickeringLightsController"/>; deactivating it stops the sequence
/// and restores the lights.
/// </summary>
public class FlickeringLightsAnomaly : SupernaturalAnomaly
{
    [Tooltip("Scene-level controller that owns the booth lights and drives the flicker sequence.")]
    [SerializeField] private BoothFlickeringLightsController lightsController;

    [VContainer.Inject] private IRoomService roomService;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (roomService != null)
        {
            roomService.StartFlickeringLights(this);
            return;
        }

        if (lightsController == null)
        {
            Debug.LogWarning($"[FlickeringLightsAnomaly] RoomService was not injected and lightsController is not assigned on '{gameObject.name}'.", this);
            return;
        }

        lightsController.StartFlickering();
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        if (roomService != null)
        {
            roomService.StopFlickeringLights(this);
        }
        else
        {
            lightsController?.StopFlickering();
        }
    }

    [ContextMenu("Activate Anomaly")]
    private void ActivateAnomalyDebug() => ActivateAnomaly();
}
