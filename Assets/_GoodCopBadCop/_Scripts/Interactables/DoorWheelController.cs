using UnityEngine;

/// <summary>
/// Interactable entry point for the bunker door wheel knob.
/// When clicked (while the door is closed), opens the <see cref="DoorWheelDiegeticController"/>
/// view so the player can spin the wheel.
/// Implements <see cref="IHeldItemPassthrough"/> so the interaction triggers even if the
/// player is holding an item.
/// </summary>
[RequireComponent(typeof(DoorWheelDiegeticController))]
public class DoorWheelController : Interactable, IHeldItemPassthrough
{
    [Header("References")]
    [Tooltip("The diegetic view controller that manages the wheel-spin interaction.")]
    [SerializeField] private DoorWheelDiegeticController _diegeticController;

    [Tooltip("The bunker door controller used to check whether the door is already open.")]
    [SerializeField] private BunkerDoorController _bunkerDoor;

    // ─── Interactable override ────────────────────────────────────────────────

    /// <summary>
    /// Opens the wheel diegetic view if the door is not already open and no other
    /// diegetic view is currently active.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_bunkerDoor != null && _bunkerDoor.IsOpen)
            return;

        if (DiegeticViewController.IsAnyViewActive)
            return;

        _diegeticController.Open(player);
    }

    // ─── MonoBehaviour ────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_diegeticController == null)
            _diegeticController = GetComponent<DoorWheelDiegeticController>();
    }
}
