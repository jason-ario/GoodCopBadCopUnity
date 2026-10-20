using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager that spawns a fresh networked <see cref="Newspaper"/> at
/// <see cref="_spawnPoint"/> once per day: when the postman drops it in the mailbox
/// (<see cref="DeliverTodaysNewspaperServer"/>, default) or, with <see cref="_deliveredByPostman"/>
/// off, every time <see cref="ShiftManager.OnDayStart"/> fires.
/// Any newspaper instance spawned on a previous day is despawned first, so at most one
/// daily newspaper exists in the scene at a time.
///
/// Prefab setup:
///   - This GameObject requires a NetworkObject component.
///   - <see cref="_newspaperPrefab"/> must have a NetworkObject and be registered in the
///     NetworkManager prefab list.
///   - Assign the world spawn location to <see cref="_spawnPoint"/> (e.g. "Newspaper Pickup Pos").
/// </summary>
public class DailyNewspaperSpawnManager : NetworkBehaviour
{
    [Header("Newspaper")]
    [Tooltip("Networked newspaper prefab to spawn at the start of each day.")]
    [SerializeField] private Newspaper _newspaperPrefab;

    [Tooltip("World location where the daily newspaper is spawned.")]
    [SerializeField] private Transform _spawnPoint;

    [Tooltip("Mailbox the paper is spawned in. While the paper waits, the mailbox is the target " +
             "(highlighted, \"Take newspaper\") instead of the paper. Auto-resolved to the scene's " +
             "PostBox if left empty.")]
    [SerializeField] private PostBox _mailbox;

    [Header("Tutorial Arrow")]
    [Tooltip("World-space tutorial arrow that hovers over the PostBox/Mailbox while today's " +
             "newspaper is waiting to be picked up. Shown right after spawn, hidden as soon as " +
             "a player picks the newspaper up. Assign the scene's 'Tutorial Arrow - Mailbox'.")]
    [SerializeField] private Transform _pickupTutorialArrow;

    [Header("Delivery")]
    [Tooltip("When true, OnDayStart only clears the previous day's newspaper. Today's paper is " +
             "spawned when the postman drops it in the mailbox (PostmanVisitController calls " +
             "DeliverTodaysNewspaperServer). Turn off to spawn it at day start as before.")]
    [SerializeField] private bool _deliveredByPostman = true;

    [Tooltip("Last day the postman delivers (he turns into a mutant on this day). On later days " +
             "the paper spawns at day start again. 0 = he always delivers.")]
    [SerializeField, Min(0)] private int _lastPostmanDeliveryDay = 4;

    private bool PostmanDeliversToday =>
        _deliveredByPostman &&
        (_lastPostmanDeliveryDay <= 0 || ShiftManager.Instance == null ||
         ShiftManager.Instance.CurrentDay <= _lastPostmanDeliveryDay);

    // The newspaper spawned for the current day, if any, so it can be despawned next day.
    private NetworkObject _activeNewspaper;

    // The Newspaper component on the active newspaper, so its pickup event can be unsubscribed.
    private Newspaper _activeNewspaperComponent;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    // Tracks whether OnDayStart has been subscribed, so OnNetworkDespawn only unsubscribes
    // once the retry coroutine has actually attached the handler.
    private bool _subscribedToDayStart;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[DailyNewspaperSpawnManager] OnNetworkSpawn. IsServer={IsServer}, IsHost={IsHost}, IsClient={IsClient}.", this);

        if (!IsServer)
            return;

        StartCoroutine(SubscribeToDayStartWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && _subscribedToDayStart && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;

        _subscribedToDayStart = false;

        UnsubscribeFromActiveNewspaper();
    }

    /// <summary>
    /// Waits for <see cref="ShiftManager.Instance"/> to become available before subscribing.
    /// Scene-placed NetworkObjects spawn in a non-deterministic order, so ShiftManager's
    /// singleton may not be assigned yet at the exact moment this object's OnNetworkSpawn runs.
    /// A one-shot null check here would silently and permanently miss the subscription —
    /// this retries every frame until the instance exists.
    /// </summary>
    private IEnumerator SubscribeToDayStartWhenReady()
    {
        yield return new WaitUntil(() => ShiftManager.Instance != null);

        ShiftManager.Instance.OnDayStart += OnDayStart;
        _subscribedToDayStart = true;

        Debug.Log("[DailyNewspaperSpawnManager] Subscribed to ShiftManager.OnDayStart.", this);
    }

    // ── Day Start ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="ShiftManager.OnDayStart"/> on the server.
    /// Despawns the previous day's newspaper (if still present), then spawns a fresh one.
    /// </summary>
    private void OnDayStart()
    {
        DespawnPreviousNewspaper();

        if (PostmanDeliversToday)
        {
            Debug.Log("[DailyNewspaperSpawnManager] OnDayStart received — previous newspaper cleared; today's arrives with the postman.", this);
            return;
        }

        Debug.Log("[DailyNewspaperSpawnManager] OnDayStart received — despawning previous newspaper and spawning today's.", this);
        SpawnDailyNewspaper();
    }

    /// <summary>
    /// Server-only. Spawns today's newspaper in the mailbox right now (despawning any previous
    /// one first). Called by <c>GoodCopBadCop.Postman.PostmanVisitController</c> at the moment
    /// the postman drops the paper in, when <see cref="_deliveredByPostman"/> is enabled.
    /// </summary>
    public void DeliverTodaysNewspaperServer()
    {
        if (!IsServer) return;

        DespawnPreviousNewspaper();
        SpawnDailyNewspaper(playMailSound: true);
    }

    private PostBox Mailbox
    {
        get
        {
            if (_mailbox == null)
                _mailbox = FindFirstObjectByType<PostBox>();
            return _mailbox;
        }
    }

    // ── Despawn ──────────────────────────────────────────────────────────────

    private void DespawnPreviousNewspaper()
    {
        // The previous day's newspaper may still be sitting unpicked — clear its arrow and
        // unsubscribe before the instance is despawned.
        UnsubscribeFromActiveNewspaper();
        HidePickupArrow();

        if (Mailbox != null)
            Mailbox.ClearNewspaperServer();

        if (_activeNewspaper != null && _activeNewspaper.IsSpawned)
            _activeNewspaper.Despawn();

        _activeNewspaper = null;
    }

    // ── Spawn ────────────────────────────────────────────────────────────────

    private void SpawnDailyNewspaper(bool playMailSound = false)
    {
        if (_newspaperPrefab == null)
        {
            Debug.LogWarning("[DailyNewspaperSpawnManager] No newspaper prefab assigned.", this);
            return;
        }

        if (_spawnPoint == null)
        {
            Debug.LogWarning("[DailyNewspaperSpawnManager] No spawn point assigned.", this);
            return;
        }

        GameObject instance = Instantiate(_newspaperPrefab.gameObject, _spawnPoint.position, _spawnPoint.rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[DailyNewspaperSpawnManager] Prefab '{_newspaperPrefab.name}' is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        netObj.Spawn(true);

        // Netcode's NetworkRigidbody does not auto-manage kinematic state for this prefab
        // (AutoUpdateKinematicState is off), so the freshly spawned instance can end up
        // non-kinematic and fall through the floor under gravity. Force it kinematic here,
        // matching the defensive re-assertion PickableObject does at every other spawn/place
        // site (e.g. SetSocketFollow, ApplySaveData).
        Rigidbody rb = instance.GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = true;

        _activeNewspaper = netObj;

        // Point players at the PostBox with a hovering arrow until this newspaper is picked up.
        _activeNewspaperComponent = instance.GetComponent<Newspaper>();
        if (_activeNewspaperComponent != null)
            _activeNewspaperComponent.OnPickedUpNetworked += OnActiveNewspaperPickedUp;

        // The paper waits in the mailbox: the mailbox is the target until someone takes it out.
        if (Mailbox != null && _activeNewspaperComponent != null)
            Mailbox.PlaceNewspaperServer(_activeNewspaperComponent, playMailSound);
        else if (Mailbox == null)
            Debug.LogWarning("[DailyNewspaperSpawnManager] No PostBox found — newspaper is a direct pickup.", this);

        ShowPickupArrow();

        Debug.Log("[DailyNewspaperSpawnManager] Spawned today's newspaper.");
    }

    // ── Tutorial Arrow ───────────────────────────────────────────────────────

    /// <summary>
    /// Fired on every instance (server and clients) the moment the tracked newspaper is picked
    /// up for the first time. Hides the arrow for all clients and stops tracking the pickup.
    /// </summary>
    private void OnActiveNewspaperPickedUp()
    {
        UnsubscribeFromActiveNewspaper();
        HidePickupArrow();
    }

    private void UnsubscribeFromActiveNewspaper()
    {
        if (_activeNewspaperComponent != null)
            _activeNewspaperComponent.OnPickedUpNetworked -= OnActiveNewspaperPickedUp;

        _activeNewspaperComponent = null;
    }

    private void ShowPickupArrow()
    {
        if (_pickupTutorialArrow == null) return;

        // Day 1 has its own scripted introduction to the newspaper (see the intro timeline /
        // Day_01 flow), so the generic mailbox tutorial arrow would be redundant there.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;

        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_pickupTutorialArrow, true);
    }

    private void HidePickupArrow()
    {
        if (_pickupTutorialArrow == null) return;
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_pickupTutorialArrow, false);
    }
}
