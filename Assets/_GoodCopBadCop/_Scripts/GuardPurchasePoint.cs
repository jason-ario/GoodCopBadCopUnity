using Unity.Netcode;
using UnityEngine;

/// <summary>
/// World interactable that opens the Guard Purchase Screen when the player interacts with it.
/// Uses NetworkVariables to synchronise purchase and arrival state across all clients.
/// The guard arrives at the start of the next in-game day after the purchase.
///
/// Locked by default (see <see cref="_unlocked"/>) so it stays hidden and non-interactable
/// until a day script (e.g. Day_03) calls <see cref="SetUnlocked"/>. This GameObject must
/// remain active in the scene at all times so Netcode spawns it as an in-scene placed
/// NetworkObject at scene load — the locked state is purely a visual/interaction gate,
/// not a GameObject activation toggle.
/// </summary>
public class GuardPurchasePoint : Interactable
{
    [Header("Purchase Settings")]
    [Tooltip("Cost in coupons to purchase a guard.")]
    [SerializeField] private int _guardPrice = 50;

    [Header("Scene References")]
    [Tooltip("Child GameObject to activate when the guard arrives the next day.")]
    [SerializeField] private GameObject _suspectSoldier;
    [Tooltip("Child GameObject representing the purchase post — deactivated when the guard arrives.")]
    [SerializeField] private GameObject _guardPurchasePost;

    private readonly NetworkVariable<bool> _guardPurchased = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _guardArrived = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this purchase point is unlocked and available to the players. Locked (false)
    /// by default so the point stays hidden and non-interactable until a day script (e.g.
    /// Day_03) calls <see cref="SetUnlocked"/>.
    /// </summary>
    private readonly NetworkVariable<bool> _unlocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _guardPurchased.OnValueChanged += OnGuardPurchasedChanged;
        _guardArrived.OnValueChanged += OnGuardArrivedChanged;
        _unlocked.OnValueChanged += OnUnlockedChanged;

        // Apply state immediately for late-joining clients.
        RefreshVisualState();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[GuardPurchasePoint] ShiftManager.Instance is null on network spawn.", this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _guardPurchased.OnValueChanged -= OnGuardPurchasedChanged;
        _guardArrived.OnValueChanged -= OnGuardArrivedChanged;
        _unlocked.OnValueChanged -= OnUnlockedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (!_unlocked.Value) return;

        base.Interact(player);

        if (_guardPurchased.Value)
            UIController.Instance.OpenGuardPurchaseScreenHired();
        else
            UIController.Instance.OpenGuardPurchaseScreen(_guardPrice, OnPurchaseConfirmed);
    }

    /// <summary>
    /// Unlocks (or re-locks) this purchase point, making it visible and interactable. Called
    /// by day scripts (e.g. Day_03) at day start. Server-only; safe to call from all clients
    /// since day activation logic runs on every client.
    /// </summary>
    public void SetUnlocked(bool unlocked)
    {
        if (!IsServer) return;
        _unlocked.Value = unlocked;
    }

    private void OnPurchaseConfirmed()
    {
        GlobalHostVariables.Instance.SubtractMoneyFromClient(_guardPrice);
        SetGuardPurchasedServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetGuardPurchasedServerRpc()
    {
        if (!_guardPurchased.Value)
            _guardPurchased.Value = true;
    }

    private void OnGuardPurchasedChanged(bool previousValue, bool newValue)
    {
        if (!newValue) return;
        UIController.Instance.ShowShopNotification("Guard hired! Will arrive tomorrow.");
    }

    /// <summary>Called on all clients at the start of each day. Sets arrival state on the server.</summary>
    private void OnDayStart()
    {
        if (!IsServer) return;
        if (_guardPurchased.Value && !_guardArrived.Value)
            _guardArrived.Value = true;
    }

    private void OnGuardArrivedChanged(bool previousValue, bool newValue) => RefreshVisualState();

    private void OnUnlockedChanged(bool previousValue, bool newValue) => RefreshVisualState();

    /// <summary>
    /// Applies the correct visibility for the post/soldier children based on the current
    /// locked/purchased/arrived state:
    ///   - Locked: both post and soldier hidden.
    ///   - Unlocked, not yet arrived: post visible, soldier hidden.
    ///   - Unlocked, guard arrived: soldier visible, post hidden.
    /// </summary>
    private void RefreshVisualState()
    {
        bool showSoldier = _unlocked.Value && _guardArrived.Value;
        bool showPost = _unlocked.Value && !_guardArrived.Value;

        if (_suspectSoldier != null)
            _suspectSoldier.SetActive(showSoldier);

        if (_guardPurchasePost != null)
            _guardPurchasePost.SetActive(showPost);
    }
}
