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
    [Tooltip("Child GameObject with the sign mesh and its interaction collider. Hidden and " +
             "non-interactable while locked, alongside the post and soldier.")]
    [SerializeField] private GameObject _guardSign;
    [Tooltip("Placeholder world-space TextMeshPro shown on the post once a guard has been " +
             "purchased but hasn't arrived yet (scheduled to spawn at the next day start). " +
             "Simple text stand-in for a proper 'purchased, arriving tomorrow' visual.")]
    [SerializeField] private TMPro.TextMeshPro _pendingArrivalText;

    [Header("Persistence")]
    [Tooltip("Stable, unique ID for this purchase point (e.g. 'GuardPost_Checkpoint'). Used to " +
             "persist purchase/arrival state to the save file so a guard bought right before " +
             "quitting still arrives on the next day after reloading. Leave empty to disable " +
             "persistence for this point (not recommended).")]
    [SerializeField] private string _guardPointId;

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

        // Restore purchase/arrival state saved before the last quit, before anyone reads the
        // NetworkVariables below — so a guard purchased but not yet arrived resumes correctly
        // (still scheduled to arrive at the next OnDayStart) instead of the purchase being lost.
        if (IsServer)
            ApplySavedPurchaseState();

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
        if (_guardPurchased.Value) return;

        _guardPurchased.Value = true;
        PersistState();
    }

    private void OnGuardPurchasedChanged(bool previousValue, bool newValue)
    {
        RefreshVisualState();

        if (!newValue) return;
        UIController.Instance.ShowShopNotification("Guard hired! Will arrive tomorrow.");
    }

    /// <summary>Called on all clients at the start of each day. Sets arrival state on the server.</summary>
    private void OnDayStart()
    {
        if (!IsServer) return;
        if (!_guardPurchased.Value || _guardArrived.Value) return;

        _guardArrived.Value = true;
        PersistState();
    }

    private void OnGuardArrivedChanged(bool previousValue, bool newValue) => RefreshVisualState();

    private void OnUnlockedChanged(bool previousValue, bool newValue) => RefreshVisualState();

    /// <summary>
    /// Called by the guard's own combat script (<c>SoldierMutantResponder</c>) once its corpse
    /// has actually been picked up and thrown away as trash (a <see cref="JunkItem"/> collected
    /// into a <see cref="TrashBag"/>) — not merely on death. Until this fires, the dead guard's
    /// body keeps occupying the soldier slot (still "arrived") so it stays visible and
    /// collectible; only once it's cleared away does the purchase post reappear and a new guard
    /// become buyable. Forces <see cref="_unlocked"/> to true so the post reappears even if this
    /// point was never explicitly unlocked by a day script (e.g. a default guard placed directly
    /// under a GuardPurchasePoint from Day 1) — a dead guard's slot must always become
    /// re-purchasable, regardless of day-gating history. Server-only.
    /// </summary>
    public void NotifyGuardCorpseCollected()
    {
        if (!IsServer) return;

        _guardArrived.Value = false;
        _guardPurchased.Value = false;
        _unlocked.Value = true;
        PersistState();
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. Restores purchase/arrival state from the save file for <see cref="_guardPointId"/>,
    /// so a guard purchased just before quitting is not silently lost on reload. A saved
    /// "purchased but not yet arrived" state is restored exactly as-is — the guard remains
    /// scheduled to arrive at the next <see cref="ShiftManager.OnDayStart"/> rather than spawning
    /// immediately or reverting to unpurchased.
    /// </summary>
    private void ApplySavedPurchaseState()
    {
        if (string.IsNullOrEmpty(_guardPointId) || SaveDataManager.Instance == null) return;

        GuardPurchasePointSaveEntry saved = SaveDataManager.Instance.GetGuardPurchasePointState(_guardPointId);
        if (saved == null) return;

        _guardPurchased.Value = saved.Purchased;
        _guardArrived.Value = saved.Arrived;
    }

    /// <summary>
    /// Server-only. Persists the current purchased/arrived state for <see cref="_guardPointId"/>
    /// to the save file, if a persistent ID is configured. No-op otherwise.
    /// </summary>
    private void PersistState()
    {
        if (string.IsNullOrEmpty(_guardPointId) || SaveDataManager.Instance == null) return;
        SaveDataManager.Instance.SaveGuardPurchasePointState(_guardPointId, _guardPurchased.Value, _guardArrived.Value);
    }

    /// <summary>
    /// Applies the correct visibility for the sign/post/soldier/pending-text children based on the
    /// current locked/purchased/arrived state:
    ///   - Locked: sign, post, soldier, and pending text all hidden — nothing is visible or interactable.
    ///   - Unlocked, not purchased, not arrived: sign and post visible (buyable), soldier and pending text hidden.
    ///   - Unlocked, purchased, not yet arrived: sign and post visible, plus the pending-arrival
    ///     placeholder text ("purchased, arriving tomorrow"); soldier still hidden.
    ///   - Unlocked, guard arrived: sign and soldier visible, post and pending text hidden.
    /// </summary>
    private void RefreshVisualState()
    {
        bool showSoldier = _unlocked.Value && _guardArrived.Value;
        bool showPost = _unlocked.Value && !_guardArrived.Value;
        bool showPendingArrival = showPost && _guardPurchased.Value;

        if (_suspectSoldier != null)
            _suspectSoldier.SetActive(showSoldier);

        if (_guardPurchasePost != null)
            _guardPurchasePost.SetActive(showPost);

        if (_guardSign != null)
            _guardSign.SetActive(_unlocked.Value);

        if (_pendingArrivalText != null)
            _pendingArrivalText.gameObject.SetActive(showPendingArrival);
    }
}
