using Unity.Netcode;
using UnityEngine;

/// <summary>
/// World interactable that opens the Guard Purchase Screen when the player interacts with it.
/// Uses NetworkVariables to synchronise purchase and arrival state across all clients.
/// The guard arrives at the start of the next in-game day after the purchase, at which point
/// the server spawns a fresh <see cref="_guardPrefab"/> instance over the network.
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

    [Header("Guard Spawning")]
    [Tooltip("Networked guard prefab spawned by the server when a purchased guard arrives. Must have " +
             "a NetworkObject and SoldierMutantResponder on its root and be registered in the " +
             "NetworkPrefabsList (e.g. Suspect_Soldier_At post Variant).")]
    [SerializeField] private GameObject _guardPrefab;
    [Tooltip("Optional spawn position/facing for the guard. Defaults to this post's own transform " +
             "(only its yaw is used, so the guard always stands upright).")]
    [SerializeField] private Transform _guardSpawnPoint;

    [Header("Scene References")]
    [Tooltip("Child GameObject representing the purchase post — deactivated while a guard is on duty.")]
    [SerializeField] private GameObject _guardPurchasePost;
    [Tooltip("Child GameObject with the sign mesh and its interaction collider. Hidden and " +
             "non-interactable while locked, alongside the post.")]
    [SerializeField] private GameObject _guardSign;
    [Tooltip("Optional story placeholder guard standing at this post before it unlocks (e.g. the " +
             "soldier who is dead by Day 3). Deactivated on every peer, including late joiners, as " +
             "soon as this post is unlocked. Never reactivated.")]
    [SerializeField] private GameObject _placeholderGuard;

    [Header("Pending Arrival Visual")]
    [Tooltip("Renderer of the purchase post sign whose material is swapped while a purchased " +
             "guard is pending arrival. Defaults to the Renderer on the purchase post if left empty.")]
    [SerializeField] private Renderer _postRenderer;
    [Tooltip("Material shown on the purchase post once a guard has been purchased but hasn't " +
             "arrived yet (e.g. a 'GUARD PENDING' sign). The post's original material is restored " +
             "when the slot becomes purchasable again.")]
    [SerializeField] private Material _pendingArrivalMaterial;

    private Material _defaultPostMaterial;

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

    /// <summary>
    /// Server-only. True while this slot's guard is dead but its corpse still occupies the slot
    /// (<see cref="_guardArrived"/> stays true so the body remains visible/collectible). Not
    /// persisted — corpses aren't saved, so a reload simply shows the empty, purchasable post.
    /// </summary>
    private bool _guardDead;

    /// <summary>Server-only. The guard instance currently spawned for this post (alive or corpse).</summary>
    private NetworkObject _spawnedGuard;

    /// <summary>
    /// Unlock request received before this NetworkObject spawned (e.g. a day's DayActivated
    /// running before NGO spawns scene objects). Applied in <see cref="OnNetworkSpawn"/> on the server.
    /// </summary>
    private bool _pendingUnlock;

    // Must override (not hide) Interactable.Awake: the base caches the HighlightEffect used by
    // Highlight(). Hiding it left that null, so hovering the post threw a NullReferenceException
    // inside PlayerInteractionController.HandleReticle, aborting its Update before the Interact
    // key was processed — the hire screen never opened.
    protected override void Awake()
    {
        base.Awake();

        if (_postRenderer == null && _guardPurchasePost != null)
            _postRenderer = _guardPurchasePost.GetComponent<Renderer>();

        if (_postRenderer != null)
            _defaultPostMaterial = _postRenderer.sharedMaterial;
    }

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
        {
            ApplySavedPurchaseState();

            if (_pendingUnlock && !_unlocked.Value)
            {
                _unlocked.Value = true;
                PersistState();
            }

            // A save that already has a guard on duty spawns it straight away.
            SyncSpawnedGuard();
        }
        _pendingUnlock = false;

        // Apply state immediately for late-joining clients.
        RefreshVisualState();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[GuardPurchasePoint] ShiftManager.Instance is null on network spawn.", this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            DespawnGuard();

        base.OnNetworkDespawn();

        _guardPurchased.OnValueChanged -= OnGuardPurchasedChanged;
        _guardArrived.OnValueChanged -= OnGuardArrivedChanged;
        _unlocked.OnValueChanged -= OnUnlockedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    public override string GetInteractVerb(PlayerInteractionController player)
    {
        if (!string.IsNullOrEmpty(interactVerb)) return interactVerb;
        return _guardPurchased.Value ? "Inspect" : "Hire guard";
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
        if (!IsSpawned)
        {
            _pendingUnlock = unlocked;
            return;
        }

        if (!IsServer || _unlocked.Value == unlocked) return;
        _unlocked.Value = unlocked;
        PersistState();
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
        UIController.Instance.ShowPurchaseNotification("Guard hired! Will arrive tomorrow.");
    }

    /// <summary>
    /// Called on all clients at the start of each day. Sets arrival state on the server, which
    /// spawns the guard. A replacement bought while the previous guard's corpse still occupies
    /// the slot (arrived already true) despawns that corpse and spawns a fresh guard instead.
    /// </summary>
    private void OnDayStart()
    {
        if (!IsServer) return;
        if (!_guardPurchased.Value) return;

        if (_guardArrived.Value)
        {
            if (!_guardDead) return;

            _guardDead = false;
            DespawnGuard();
            SyncSpawnedGuard();
            PersistState();
            return;
        }

        _guardArrived.Value = true;
        SyncSpawnedGuard();
        PersistState();
    }

    private void OnGuardArrivedChanged(bool previousValue, bool newValue)
    {
        RefreshVisualState();
        SyncSpawnedGuard();
    }

    private void OnUnlockedChanged(bool previousValue, bool newValue)
    {
        RefreshVisualState();
        SyncSpawnedGuard();
    }

    /// <summary>
    /// Called by <c>SoldierMutantResponder</c> the moment this slot's guard dies. Clears
    /// <see cref="_guardPurchased"/> immediately so interacting with the post opens the hire
    /// screen right away, while <see cref="_guardArrived"/> stays true so the corpse remains
    /// visible/collectible this session. Persists the slot as empty (not purchased, not arrived)
    /// so a reload never restores a living guard — corpses aren't saved. Server-only.
    /// </summary>
    public void NotifyGuardDied()
    {
        if (!IsServer) return;

        _guardDead = true;
        _guardPurchased.Value = false;
        _unlocked.Value = true;
        PersistState();
    }

    /// <summary>
    /// Called by <c>SoldierMutantResponder</c> once this slot's guard corpse has been bagged or
    /// burned. Frees the slot (post reappears) and despawns the corpse if the JunkItem didn't
    /// already. Keeps <see cref="_guardPurchased"/> as-is, so a replacement already bought while
    /// the corpse was lying there still arrives at the next day start. Server-only.
    /// </summary>
    public void NotifyGuardCorpseCollected()
    {
        if (!IsServer) return;

        _guardDead = false;
        _guardArrived.Value = false;
        _unlocked.Value = true;
        SyncSpawnedGuard();
        PersistState();
    }

    // -------------------------------------------------------------------------
    // Guard spawning (server-only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only, idempotent. Ensures a guard instance exists exactly while the post is
    /// unlocked and a guard has arrived (alive or as an uncollected corpse).
    /// </summary>
    private void SyncSpawnedGuard()
    {
        if (!IsServer) return;

        bool wantGuard = _unlocked.Value && _guardArrived.Value;
        bool hasGuard = _spawnedGuard != null && _spawnedGuard.IsSpawned;

        if (wantGuard && !hasGuard)
            SpawnGuard();
        else if (!wantGuard)
            DespawnGuard();
    }

    private void SpawnGuard()
    {
        if (_guardPrefab == null)
        {
            Debug.LogError("[GuardPurchasePoint] No guard prefab assigned — cannot spawn the purchased guard.", this);
            return;
        }

        Transform anchor = _guardSpawnPoint != null ? _guardSpawnPoint : transform;
        Quaternion rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);

        GameObject instance = Instantiate(_guardPrefab, anchor.position, rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[GuardPurchasePoint] Guard prefab '{_guardPrefab.name}' has no NetworkObject on its root.", this);
            Destroy(instance);
            return;
        }

        SoldierMutantResponder responder = instance.GetComponent<SoldierMutantResponder>();
        if (responder != null)
            responder.AssignPurchasePoint(this);
        else
            Debug.LogWarning($"[GuardPurchasePoint] Guard prefab '{_guardPrefab.name}' has no SoldierMutantResponder — " +
                             "its death/corpse won't free this post.", this);

        netObj.Spawn(destroyWithScene: true);
        _spawnedGuard = netObj;
    }

    private void DespawnGuard()
    {
        if (_spawnedGuard != null && _spawnedGuard.IsSpawned &&
            NetworkManager != null && !NetworkManager.ShutdownInProgress)
        {
            _spawnedGuard.Despawn(destroy: true);
        }

        _spawnedGuard = null;
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
        if (saved.Unlocked)
            _unlocked.Value = true;
    }

    /// <summary>
    /// Server-only. Persists the current purchased/arrived/unlocked state for <see cref="_guardPointId"/>
    /// to the save file, if a persistent ID is configured. A dead guard's corpse is saved as
    /// "not arrived" so it never comes back alive on reload. No-op without an ID.
    /// </summary>
    private void PersistState()
    {
        if (string.IsNullOrEmpty(_guardPointId) || SaveDataManager.Instance == null) return;
        SaveDataManager.Instance.SaveGuardPurchasePointState(
            _guardPointId,
            _guardPurchased.Value,
            _guardArrived.Value && !_guardDead,
            _unlocked.Value);
    }

    /// <summary>
    /// Applies the correct visibility for the sign/post children, the post's material and the
    /// placeholder guard, based on the current locked/purchased/arrived state. Runs on every peer.
    ///   - Locked: sign and post hidden — nothing is visible or interactable.
    ///   - Unlocked, not purchased, not arrived: sign and post visible (buyable) with the default
    ///     post material.
    ///   - Unlocked, purchased, not yet arrived: sign and post visible, post swapped to the
    ///     pending-arrival material.
    ///   - Unlocked, guard arrived: sign visible, post hidden (the spawned guard stands there).
    ///   - Any unlocked state: the story placeholder guard is deactivated.
    /// </summary>
    private void RefreshVisualState()
    {
        bool showPost = _unlocked.Value && !_guardArrived.Value;
        bool showPendingArrival = showPost && _guardPurchased.Value;

        if (_guardPurchasePost != null)
            _guardPurchasePost.SetActive(showPost);

        if (_guardSign != null)
            _guardSign.SetActive(_unlocked.Value);

        if (_unlocked.Value && _placeholderGuard != null && _placeholderGuard.activeSelf)
            _placeholderGuard.SetActive(false);

        if (_postRenderer != null && _pendingArrivalMaterial != null)
            _postRenderer.sharedMaterial = showPendingArrival ? _pendingArrivalMaterial : _defaultPostMaterial;
    }
}
