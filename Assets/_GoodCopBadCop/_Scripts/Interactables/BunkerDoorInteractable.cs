using UnityEngine;

/// <summary>
/// Interactable entry point for the whole bunker door (frame, door leaf, and both wheel
/// knobs all route here via <see cref="InteractableCollider"/>).
/// When the door is closed, opens whichever wheel's diegetic view sits on the interacting
/// player's side of the door (the nearest wheel to the player wins). When the door is
/// already open, interacting slams it shut.
/// Implements <see cref="IHeldItemPassthrough"/> so the interaction triggers even if the
/// player is holding an item.
/// </summary>
[RequireComponent(typeof(BunkerDoorController))]
public class BunkerDoorInteractable : Interactable, IHeldItemPassthrough
{
    [Header("References")]
    [Tooltip("The bunker door controller used to check whether the door is already open, and to open/close it.")]
    [SerializeField] private BunkerDoorController _bunkerDoor;

    [Tooltip("Tracks whether another player is currently using this door. Prevents two players from opening a wheel view at once.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    [Header("Wheel Views")]
    [Tooltip("The diegetic wheel views available on this door, one per side. Whichever is nearest to the interacting player is opened.")]
    [SerializeField] private DoorWheelDiegeticController[] _wheelViews;

    // ─── Interactable override ────────────────────────────────────────────────

    /// <summary>
    /// If the door is open, slams it shut. Otherwise opens the wheel diegetic view on the
    /// player's side of the door, provided no other player is currently occupying it.
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

        DoorWheelDiegeticController nearestView = GetNearestWheelView(player.transform.position);
        if (nearestView == null)
        {
            _occupancy?.Release();
            return;
        }

        nearestView.Open(player);
    }

    // ─── MonoBehaviour ────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_bunkerDoor == null)
            _bunkerDoor = GetComponent<BunkerDoorController>();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>Returns whichever configured wheel view's Transform is closest to <paramref name="fromPosition"/>.</summary>
    private DoorWheelDiegeticController GetNearestWheelView(Vector3 fromPosition)
    {
        DoorWheelDiegeticController nearest = null;
        float nearestSqrDist = float.MaxValue;

        if (_wheelViews == null) return null;

        foreach (DoorWheelDiegeticController view in _wheelViews)
        {
            if (view == null) continue;

            float sqrDist = (view.transform.position - fromPosition).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = view;
            }
        }

        return nearest;
    }
}
