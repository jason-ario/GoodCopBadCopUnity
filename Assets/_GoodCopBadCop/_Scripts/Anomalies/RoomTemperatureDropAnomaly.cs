using GoodCopBadCop.RoomSystem;
using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect causes a sudden drop in room temperature.
/// </summary>
public class RoomTemperatureDropAnomaly : SupernaturalAnomaly
{
    [SerializeField] private float temperatureOffset = -15f;

    [VContainer.Inject] private IRoomService roomService;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (roomService == null)
        {
            Debug.LogWarning($"[RoomTemperatureDropAnomaly] {nameof(IRoomService)} was not injected on '{gameObject.name}'.", this);
            return;
        }

        roomService.SetTemperatureOffset(this, temperatureOffset);
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        roomService?.ResetTemperatureOffset(this);
    }

    public override void InitializeDisabled()
    {
        roomService?.ResetTemperatureOffset(this);
    }
}
