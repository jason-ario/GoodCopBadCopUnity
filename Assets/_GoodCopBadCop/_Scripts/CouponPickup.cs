using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A world-pickup interactable that awards coupon currency to the shared money pool
/// and despawns itself when interacted with.
/// </summary>
public class CouponPickup : Interactable
{
    private const string DefaultInteractText = "Coupon";

    // ── Static tracking ──────────────────────────────────────────────────────

    /// <summary>
    /// Number of <see cref="CouponPickup"/> NetworkObjects currently live in the scene
    /// across all clients. Incremented on <see cref="OnNetworkSpawn"/>, decremented on
    /// <see cref="OnNetworkDespawn"/>. Use this to detect when all dispensed coupons have
    /// been collected (value reaches zero).
    /// </summary>
    public static int ActiveCount;

    /// <summary>
    /// Fired on ALL clients (via <see cref="ShowPickupPopupClientRpc"/>) whenever any
    /// coupon pickup is collected. Subscribe to drive tutorial task progression.
    /// </summary>
    public static event System.Action OnAnyPickedUp;

    [Header("Coupon Settings")]
    [Tooltip("Amount of coupon currency awarded to the shared pool on pickup.")]
    [SerializeField] private int _couponAmount = 5;

    /// <summary>
    /// The currency value of this coupon. Used by the ATM to determine how many
    /// physical coupons to spawn for a given monetary amount.
    /// </summary>
    public int CouponValue => _couponAmount;

    [Header("Audio")]
    [Tooltip("Sound played for all clients on pickup.")]
    [SerializeField] private AudioClip _pickupSound;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = DefaultInteractText;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ActiveCount++;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        ActiveCount--;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Triggered by the local player via E or left-click. Disables the component
    /// immediately to prevent double-firing during the server round-trip, then
    /// routes the award, popup, and despawn to the server.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        // Disable locally so the highlight and interact prompt disappear immediately.
        enabled = false;

        PickupServerRpc();
    }

    // ── Server ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Awards coupons to the shared pool, broadcasts the cash popup to all clients,
    /// then despawns this pickup. Server-authoritative.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void PickupServerRpc()
    {
        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.AddMoney(_couponAmount);

        ShowPickupPopupClientRpc(_couponAmount);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ── Clients ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the pickup sound on every client and fires <see cref="OnAnyPickedUp"/>
    /// so tutorial systems can track collection progress.
    /// </summary>
    [ClientRpc]
    private void ShowPickupPopupClientRpc(int amount)
    {
        OnAnyPickedUp?.Invoke();

        if (_pickupSound != null && SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(_pickupSound, transform.position);
    }
}
