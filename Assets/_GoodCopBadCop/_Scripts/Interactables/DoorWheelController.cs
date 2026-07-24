using UnityEngine;

/// <summary>
/// Interactable entry point for the whole bunker door (frame, door leaf, and wheel knob
/// all route here via <see cref="InteractableCollider"/>).
/// When the door is closed, opens the <see cref="DoorWheelDiegeticController"/> view so the
/// player can spin the wheel. When the door is already open, interacting slams it shut.
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

    [Tooltip("Tracks whether another player is currently using this door. Prevents two players from opening the wheel view at once.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    // ─── Interactable override ────────────────────────────────────────────────

    /// <summary>
    /// If the door is open, slams it shut. Otherwise opens the wheel diegetic view,
    /// provided no other player is currently occupying it.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_bunkerDoor != null && _bunkerDoor.IsOpen)
        {
            _bunkerDoor.Close();
            return;
        }

        if (DiegeticViewController.IsAnyViewActive)
            return;

        if (_occupancy != null && !_occupancy.TryClaim(player))
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
