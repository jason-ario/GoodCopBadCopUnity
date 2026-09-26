using UnityEngine;

/// <summary>
/// Handles player-initiated throwing of held pickable objects.
/// Hold the throw input to aim; a fixed-strength ballistic arc LineRenderer
/// previews the trajectory and rotates with the camera as the player looks
/// around. Releasing the input launches the item at that fixed speed along
/// the current look direction — there is no charge-up, so the throw is
/// always the same strength and can be redirected freely before release.
/// The actual throw is executed as a server-authoritative physics event via
/// <see cref="PickableObject.ThrowServerRpc"/>.
///
/// Attach to the same GameObject as <see cref="PlayerPickupController"/>.
/// Wire up a <see cref="LineRenderer"/> in the Inspector for the arc preview.
/// </summary>
[RequireComponent(typeof(PlayerPickupController))]
public class ThrowController : MonoBehaviour
{
    [Header("Throw Settings")]
    [Tooltip("Fixed forward launch speed (m/s) applied to every throw. Aim by looking around while holding the throw input; there is no charge-up.")]
    [SerializeField] private float forwardThrowSpeed = 14f;

    [Tooltip("Additional upward launch speed (m/s), independent of camera pitch. Applied equally to the preview and actual throw.")]
    [SerializeField] private float upwardThrowBoost = 2f;

    [Header("Arc Preview")]
    [Tooltip("LineRenderer used to display the throw trajectory arc. Optional.")]
    [SerializeField] private LineRenderer throwArcLine;

    [Tooltip("Number of points on the arc preview.")]
    [SerializeField] private int arcSegments = 30;

    [Tooltip("Time step between arc sample points (seconds). Smaller = smoother but shorter arc.")]
    [SerializeField] private float arcTimeStep = 0.05f;

    private bool _isAiming;
    private Camera _cam;
    private PlayerPickupController _pickupController;
    private PlayerInventory _inventory;

    /// <summary>True while the player is holding the throw input to aim.</summary>
    public bool IsCharging => _isAiming;

    private void Awake()
    {
        _pickupController = GetComponent<PlayerPickupController>();
        _inventory = GetComponent<PlayerInventory>();
        _cam = GetComponentInChildren<Camera>();
    }

    private void OnEnable()
    {
        if (_inventory != null) _inventory.OnActiveSlotChanged += HandleActiveSlotChanged;
    }

    private void OnDisable()
    {
        if (_inventory != null) _inventory.OnActiveSlotChanged -= HandleActiveSlotChanged;
    }

    /// <summary>
    /// Cancels an in-progress aim the moment the player swaps their equipped hotbar slot
    /// (hotkey 1/2, scroll cycling, or a full-to-full swap) — never leave the aim active
    /// while the held item itself is changing out from under it.
    /// </summary>
    private void HandleActiveSlotChanged(int newSlot)
    {
        if (_isAiming) CancelCharge();
    }

    /// <summary>
    /// Begins aiming a throw. No-op when no item is held, or when the
    /// held item's <see cref="PickableItemData.canBeThrown"/> flag is false (e.g. stamps).
    /// Called by <see cref="PlayerInteractionController"/> on throw-input down.
    /// </summary>
    public void StartCharge()
    {
        if (!_pickupController.IsHoldingObject) return;

        PickableItemData heldItemData = _pickupController.HeldObject.ItemData;
        if (heldItemData != null && !heldItemData.canBeThrown) return;

        _isAiming = true;
        if (throwArcLine != null) throwArcLine.gameObject.SetActive(true);
    }

    /// <summary>
    /// Refreshes the arc preview to follow the current look direction.
    /// Call every frame while the throw input is held.
    /// </summary>
    public void UpdateCharge(float deltaTime)
    {
        if (!_isAiming) return;
        UpdateArcPreview();
    }

    /// <summary>
    /// Releases the held item as a throw at the fixed <see cref="forwardThrowSpeed"/> speed,
    /// launched along the current camera look direction. Detaches the item from the
    /// player's hand and sends a server RPC to apply physics velocity and re-enable
    /// <c>NetworkTransform</c> on all clients.
    /// </summary>
    public void ReleaseThrow()
    {
        if (!_isAiming) return;

        CancelCharge();

        PickableObject released = _pickupController.ReleaseHeldObjectForThrow();
        if (released == null) return;

        Vector3 velocity = GetInitialThrowVelocity();
        released.ThrowServerRpc(released.transform.position, velocity);
    }

    /// <summary>
    /// Cancels an in-progress aim without throwing (e.g. item dropped while aiming).
    /// </summary>
    public void CancelCharge()
    {
        _isAiming = false;
        if (throwArcLine != null) throwArcLine.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private Vector3 GetInitialThrowVelocity()
    {
        return _cam.transform.forward * forwardThrowSpeed + Vector3.up * upwardThrowBoost;
    }

    private void UpdateArcPreview()
    {
        if (throwArcLine == null || _pickupController.HeldObject == null) return;

        Vector3 startPos = _pickupController.HeldObject.transform.position;
        Vector3 initialVelocity = GetInitialThrowVelocity();

        throwArcLine.positionCount = arcSegments;
        for (int i = 0; i < arcSegments; i++)
        {
            float t = i * arcTimeStep;
            // Ballistic projectile equation: p(t) = p0 + v0*t + ½g*t²
            Vector3 point = startPos + initialVelocity * t + 0.5f * Physics.gravity * t * t;
            throwArcLine.SetPosition(i, point);
        }
    }
}
