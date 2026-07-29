using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager that spawns a fresh networked <see cref="Newspaper"/> at
/// <see cref="_spawnPoint"/> every time <see cref="ShiftManager.OnDayStart"/> fires.
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

    [Header("Tutorial Arrow")]
    [Tooltip("World-space tutorial arrow that hovers over the PostBox/Mailbox while today's " +
             "newspaper is waiting to be picked up. Shown right after spawn, hidden as soon as " +
             "a player picks the newspaper up. Assign the scene's 'Tutorial Arrow - Mailbox'.")]
    [SerializeField] private Transform _pickupTutorialArrow;

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
        Debug.Log("[DailyNewspaperSpawnManager] OnDayStart received — despawning previous newspaper and spawning today's.", this);
        DespawnPreviousNewspaper();
        SpawnDailyNewspaper();
    }

    // ── Despawn ──────────────────────────────────────────────────────────────

    private void DespawnPreviousNewspaper()
    {
        // The previous day's newspaper may still be sitting unpicked — clear its arrow and
        // unsubscribe before the instance is despawned.
        UnsubscribeFromActiveNewspaper();
        HidePickupArrow();

        if (_activeNewspaper != null && _activeNewspaper.IsSpawned)
            _activeNewspaper.Despawn();

        _activeNewspaper = null;
    }

    // ── Spawn ────────────────────────────────────────────────────────────────

    private void SpawnDailyNewspaper()
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
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_pickupTutorialArrow, true);
    }

    private void HidePickupArrow()
    {
        if (_pickupTutorialArrow == null) return;
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_pickupTutorialArrow, false);
    }
}
