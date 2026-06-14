using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Abstract base class for interactive containers that players fill and then call HQ to empty
/// (e.g. PostBox for MutantBits, DumpsterInteractable for trash bags).
///
/// Fill tracking, HQ call flow, and collector payment are handled here.
/// Subclasses provide deposit logic, interact text, and any specialised visual feedback.
///
/// Setup requirements:
///   - NetworkObject on the same GameObject (inherited from Interactable → NetworkBehaviour).
///   - HighlightEffect (required by Interactable).
///   - Collider on the Interactable layer.
///   - HQPickupDispatcher present somewhere in the scene.
/// </summary>
public abstract class CollectableContainer : Interactable
{
    [Header("Container Settings")]
    [Tooltip("Maximum number of items this container accepts before calling HQ.")]
    [SerializeField] protected int _capacity = 5;

    [Tooltip("Coupons awarded to all players when HQ collects a full container.")]
    [SerializeField] private int _couponRewardPerCollection = 20;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _fillCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isAwaitingPickup = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Properties ──────────────────────────────────────────────────────────

    /// <summary>Current number of items deposited in this container.</summary>
    public int FillCount => _fillCount.Value;

    /// <summary>Maximum items this container accepts.</summary>
    public int Capacity => _capacity;

    /// <summary>True when the container has reached capacity.</summary>
    public bool IsFull => _fillCount.Value >= _capacity;

    /// <summary>True after the player has called HQ and a collector is en route.</summary>
    public bool IsAwaitingPickup => _isAwaitingPickup.Value;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = GetDefaultInteractText();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _fillCount.OnValueChanged        += OnFillCountChanged;
        _isAwaitingPickup.OnValueChanged += OnPickupStateChanged;
        RefreshInteractText();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _fillCount.OnValueChanged        -= OnFillCountChanged;
        _isAwaitingPickup.OnValueChanged -= OnPickupStateChanged;
    }

    // ── Interact (no held item) ───────────────────────────────────────────────

    /// <summary>
    /// When the player left-clicks without a held item and the container is full,
    /// calls HQ for pickup.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsFull && !IsAwaitingPickup)
            CallHQForPickupServerRpc();
    }

    // ── Deposit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Increments the fill counter by one. Guards against overflow and mid-pickup deposits.
    /// Call this directly from server-side code (e.g. from within a [ServerRpc] body).
    /// </summary>
    protected void PerformDeposit()
    {
        if (!IsServer) return;
        if (IsFull || IsAwaitingPickup) return;

        _fillCount.Value = Mathf.Min(_fillCount.Value + 1, _capacity);
    }

    /// <summary>
    /// Client-to-server deposit route. Calls PerformDeposit() on the server.
    /// Use when the deposit originates on a non-server client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DepositServerRpc()
    {
        PerformDeposit();
    }

    // ── HQ call ──────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void CallHQForPickupServerRpc()
    {
        if (!IsFull || IsAwaitingPickup) return;

        _isAwaitingPickup.Value = true;

        if (HQPickupDispatcher.Instance != null)
            HQPickupDispatcher.Instance.DispatchCollector(this);
        else
            Debug.LogWarning($"[CollectableContainer] HQPickupDispatcher.Instance is null — cannot dispatch collector.", this);
    }

    // ── Collector callback ────────────────────────────────────────────────────

    /// <summary>
    /// Called by CollectorNPC when it reaches this container and has finished collecting.
    /// Resets fill state and awards the coupon reward. SERVER ONLY.
    /// </summary>
    public void OnCollectorArrived()
    {
        if (!IsServer) return;

        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.AddMoney(_couponRewardPerCollection);

        _fillCount.Value        = 0;
        _isAwaitingPickup.Value = false;

        Debug.Log($"[CollectableContainer] '{name}' collected. {_couponRewardPerCollection} coupons awarded.");
    }

    // ── Text helpers ──────────────────────────────────────────────────────────

    /// <summary>Interact text shown when the container has space and is not awaiting pickup.</summary>
    protected abstract string GetDefaultInteractText();

    /// <summary>Interact text shown when the container is full and the player can call HQ.</summary>
    protected abstract string GetFullInteractText();

    /// <summary>Refreshes interactText based on current fill and pickup state.</summary>
    protected void RefreshInteractText()
    {
        if (IsAwaitingPickup)
            interactText = "Pickup Requested...";
        else if (IsFull)
            interactText = GetFullInteractText();
        else
            interactText = GetDefaultInteractText();
    }

    // ── NetworkVariable callbacks ─────────────────────────────────────────────

    protected virtual void OnFillCountChanged(int previous, int current)
    {
        RefreshInteractText();
    }

    protected virtual void OnPickupStateChanged(bool previous, bool current)
    {
        RefreshInteractText();
    }
}
