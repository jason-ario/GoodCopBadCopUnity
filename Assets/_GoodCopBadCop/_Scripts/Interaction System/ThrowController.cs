using UnityEngine;

/// <summary>
/// Handles player-initiated throwing of held pickable objects.
/// Hold F to charge a throw; release F to launch. A ballistic arc LineRenderer
/// previews the trajectory during charge. The actual throw is executed as a
/// server-authoritative physics event via <see cref="PickableObject.ThrowServerRpc"/>.
///
/// Attach to the same GameObject as <see cref="PlayerPickupController"/>.
/// Wire up a <see cref="LineRenderer"/> in the Inspector for the arc preview.
/// </summary>
[RequireComponent(typeof(PlayerPickupController))]
public class ThrowController : MonoBehaviour
{
    [Header("Throw Settings")]
    [Tooltip("Minimum launch speed (m/s) when charge is at zero.")]
    [SerializeField] private float minThrowForce = 6f;

    [Tooltip("Maximum launch speed (m/s) at full charge.")]
    [SerializeField] private float maxThrowForce = 22f;

    [Tooltip("Time in seconds to reach full charge.")]
    [SerializeField] private float maxChargeTime = 1.5f;

    [Header("Arc Preview")]
    [Tooltip("LineRenderer used to display the throw trajectory arc. Optional.")]
    [SerializeField] private LineRenderer throwArcLine;

    [Tooltip("Number of points on the arc preview.")]
    [SerializeField] private int arcSegments = 30;

    [Tooltip("Time step between arc sample points (seconds). Smaller = smoother but shorter arc.")]
    [SerializeField] private float arcTimeStep = 0.05f;

    private float _chargeTime;
    private bool _isCharging;
    private Camera _cam;
    private PlayerPickupController _pickupController;

    /// <summary>True while the player is holding F to charge a throw.</summary>
    public bool IsCharging => _isCharging;

    /// <summary>0–1 ratio of how far the charge has progressed toward full.</summary>
    public float ChargeRatio => maxChargeTime > 0f ? _chargeTime / maxChargeTime : 0f;

    private void Awake()
    {
        _pickupController = GetComponent<PlayerPickupController>();
        _cam = GetComponentInChildren<Camera>();
    }

    /// <summary>
    /// Begins accumulating throw charge. No-op when no item is held.
    /// Called by <see cref="PlayerInteractionController"/> on F key down.
    /// </summary>
    public void StartCharge()
    {
        if (!_pickupController.IsHoldingObject) return;
        _chargeTime = 0f;
        _isCharging = true;
        if (throwArcLine != null) throwArcLine.gameObject.SetActive(true);
    }

    /// <summary>
    /// Advances charge time and refreshes the arc preview.
    /// Call every frame while F is held.
    /// </summary>
    public void UpdateCharge(float deltaTime)
    {
        if (!_isCharging) return;
        _chargeTime = Mathf.Min(_chargeTime + deltaTime, maxChargeTime);
        UpdateArcPreview();
    }

    /// <summary>
    /// Releases the held item as a throw with the force accumulated so far.
    /// Detaches the item from the player's hand and sends a server RPC to apply
    /// physics velocity and re-enable <c>NetworkTransform</c> on all clients.
    /// </summary>
    public void ReleaseThrow()
    {
        if (!_isCharging) return;

        float savedCharge = _chargeTime;
        CancelCharge();

        PickableObject released = _pickupController.ReleaseHeldObjectForThrow();
        if (released == null) return;

        float force = Mathf.Lerp(minThrowForce, maxThrowForce, savedCharge / maxChargeTime);
        Vector3 velocity = _cam.transform.forward * force;

        released.ThrowServerRpc(released.transform.position, velocity);
    }

    /// <summary>
    /// Cancels an in-progress charge without throwing (e.g. item dropped while charging).
    /// </summary>
    public void CancelCharge()
    {
        _isCharging = false;
        _chargeTime = 0f;
        if (throwArcLine != null) throwArcLine.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void UpdateArcPreview()
    {
        if (throwArcLine == null || _pickupController.HeldObject == null) return;

        Vector3 startPos = _pickupController.HeldObject.transform.position;
        float force = Mathf.Lerp(minThrowForce, maxThrowForce, _chargeTime / maxChargeTime);
        Vector3 initialVelocity = _cam.transform.forward * force;

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
