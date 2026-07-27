using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the supply box delivery.
///
/// When the day starts it:
///   1. Despawns any box and items left over from the previous day.
///   2. Spawns a fresh <see cref="SupplyBox"/> instance at the active day's
///      <see cref="DayBase.GetSupplyBoxSpawnPointOverride"/> (falling back to
///      <see cref="_spawnPoint"/> when the day doesn't provide one).
///   3. Spawns the day's configured item prefabs as NetworkObject children of the box contents.
///   4. Immediately unlocks the box and its contents for pickup.
/// </summary>
public class SupplyBoxDeliveryController : NetworkBehaviour
{
    [Header("Prefabs")]
    [Tooltip("Supply Box prefab to spawn each delivery day. Must have NetworkObject and SupplyBox components, and be registered in the NetworkManager prefab list.")]
    [SerializeField] private GameObject _supplyBoxPrefab;

    [Header("Scene References")]
    [Tooltip("Transform where the supply box spawns at the start of the day.")]
    [SerializeField] private Transform _spawnPoint;

    [Header("Timing")]
    [Tooltip("Seconds to wait after the day officially starts before spawning the supply box.")]
    [SerializeField] private float _startDelay = 3f;

    // ── Private State ─────────────────────────────────────────────────────────

    private NetworkObject _activeBoxNetObj;
    private SupplyBox _activeBox;
    private readonly List<NetworkObject> _spawnedItems = new List<NetworkObject>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnDayStart += OnDayStart;
            Debug.Log("[SupplyBoxDeliveryController] Subscribed to ShiftManager.OnDayStart on server.", this);
        }
        else
        {
            Debug.LogError("[SupplyBoxDeliveryController] ShiftManager.Instance is null — delivery will not trigger.", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    // ── Day Start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        StartCoroutine(SpawnOnDayStart());
    }

    private IEnumerator SpawnOnDayStart()
    {
        if (_startDelay > 0)
            yield return new WaitForSeconds(_startDelay);

        DespawnPreviousDelivery();

        DayBase day = CampaignManager.Instance?.ActiveDay;
        if (day == null)
        {
            Debug.LogWarning("[SupplyBoxDeliveryController] OnDayStart: ActiveDay is null.", this);
            yield break;
        }

        if (!day.HasSupplyBoxDelivery)
        {
            Debug.Log($"[SupplyBoxDeliveryController] Day {day.DayNumber} does not have a supply box delivery configured.", this);
            yield break;
        }

        SpawnSupplyBox();
        SpawnItems(day.SupplyBoxItemPrefabs);
        FinalizeDelivery();
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnSupplyBox()
    {
        if (_supplyBoxPrefab == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] _supplyBoxPrefab is not assigned.", this);
            return;
        }

        // Ask the currently active day for a delivery position override — resolved fresh every
        // time so it can never be silently missed regardless of event-firing order, unlike a
        // one-shot mutable property that has to be set ahead of time and gets consumed once.
        Transform overrideTransform = CampaignManager.Instance?.ActiveDay?.GetSupplyBoxSpawnPointOverride();
        Transform spawnTransform = overrideTransform != null ? overrideTransform : _spawnPoint;

        if (spawnTransform == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] No spawn point available — _spawnPoint is not assigned and no override was set.", this);
            return;
        }

        GameObject instance = Instantiate(_supplyBoxPrefab, spawnTransform.position, spawnTransform.rotation);
        _activeBoxNetObj = instance.GetComponent<NetworkObject>();

        if (_activeBoxNetObj == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] Supply box prefab is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        _activeBoxNetObj.Spawn(destroyWithScene: true);
        _activeBox = instance.GetComponent<SupplyBox>();

        if (_activeBox == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] Supply box prefab is missing a SupplyBox component.", this);
            return;
        }

        _activeBox.LockInteractableNetworked();
        _activeBox.SetCanPickUpNetworked(false);
        _activeBox.ResetForDeliveryClientRpc();
    }

    private void SpawnItems(List<GameObject> prefabs)
    {
        if (_activeBox == null || prefabs == null || prefabs.Count == 0) return;

        Transform contentsParent = _activeBox.ContentsParent;

        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null) continue;

            GameObject instance = Instantiate(prefab, contentsParent.position, contentsParent.rotation);
            NetworkObject netObj = instance.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[SupplyBoxDeliveryController] Item prefab '{prefab.name}' is missing a NetworkObject — skipping.", this);
                Destroy(instance);
                continue;
            }

            netObj.Spawn(destroyWithScene: true);

            // Dynamically spawned ExamNotebooks require explicit page spawning — OnNetworkSpawn
            // only auto-spawns pages for scene objects. This mirrors the shop's purchase flow in
            // PlayerPickupController.PurchaseAndPickUpServerRpc.
            if (instance.TryGetComponent(out ExamNotebook notebook))
            {
                var spawnedPages = notebook.SpawnAndWirePages();
                if (spawnedPages.Count > 0)
                {
                    var pageRefs = new NetworkObjectReference[spawnedPages.Count];
                    for (int i = 0; i < spawnedPages.Count; i++)
                        pageRefs[i] = new NetworkObjectReference(spawnedPages[i]);
                    notebook.SetPageReferencesClientRpc(pageRefs);
                }
            }

            if (instance.TryGetComponent(out PickableObject pickable))
            {
                pickable.SetParent(contentsParent);
                pickable.LockInteractableNetworked();
                _activeBox.RegisterItem(pickable);
            }

            _spawnedItems.Add(netObj);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void DespawnPreviousDelivery()
    {
        foreach (NetworkObject item in _spawnedItems)
        {
            if (item != null && item.IsSpawned)
                item.Despawn();
        }
        _spawnedItems.Clear();

        if (_activeBox != null)
            _activeBox.ClearRegisteredItems();

        if (_activeBoxNetObj != null && _activeBoxNetObj.IsSpawned)
            _activeBoxNetObj.Despawn();

        _activeBoxNetObj = null;
        _activeBox = null;
    }

    // ── Finalize ──────────────────────────────────────────────────────────────

    private void FinalizeDelivery()
    {
        if (_activeBox == null) return;

        _activeBox.SetCanPickUpNetworked(true);
        _activeBox.UnlockInteractableNetworked();
        _activeBox.FinalizeDeliveryClientRpc();

        foreach (NetworkObject item in _spawnedItems)
        {
            if (item != null && item.TryGetComponent(out PickableObject pickable))
                pickable.UnlockInteractableNetworked();
        }

        Debug.Log("[SupplyBoxDeliveryController] Supply box spawned and ready for pickup.", this);
    }
}
