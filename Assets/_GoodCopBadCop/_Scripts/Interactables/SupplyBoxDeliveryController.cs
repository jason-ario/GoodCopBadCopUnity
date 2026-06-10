using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the supply box delivery sequence.
///
/// Place this on its own GameObject with a <see cref="NetworkObject"/> component, following
/// the same pattern as <see cref="DailyPickupSpawnManager"/>. Each delivery day it:
///   1. Despawns any box and items left over from the previous day.
///   2. Spawns a fresh <see cref="SupplyBox"/> instance at <see cref="_spawnPoint"/>.
///   3. Spawns the day's configured item prefabs as NetworkObject children of the box contents.
///   4. Fires <see cref="TriggerDeliveryDoorClientRpc"/> so the door opens on all clients.
///   5. DOTween-moves the box through <see cref="_waypoints"/> (A → B → C).
///   6. Unlocks the box and its contents for normal pickup once the sequence ends.
/// </summary>
public class SupplyBoxDeliveryController : NetworkBehaviour
{
    [Header("Prefabs")]
    [Tooltip("Supply Box prefab to spawn each delivery day. Must have NetworkObject and SupplyBox components, and be registered in the NetworkManager prefab list.")]
    [SerializeField] private GameObject _supplyBoxPrefab;

    [Header("Scene References")]
    [Tooltip("Animator on the Delivery Door child of the Door prefab.")]
    [SerializeField] private Animator _deliveryDoorAnimator;

    [Tooltip("Transform where the supply box first appears, outside the delivery door.")]
    [SerializeField] private Transform _spawnPoint;

    [Header("Waypoints (A → B → C)")]
    [Tooltip("The box moves through these transforms in order after the door opens.")]
    [SerializeField] private Transform[] _waypoints;

    [Header("Audio")]
    [Tooltip("Sound played at the spawn point when the delivery sequence begins.")]
    [SerializeField] private AudioClip _deliverySound;

    [Header("Timing")]
    [Tooltip("Seconds to wait after the day officially starts before beginning the delivery sequence.")]
    [SerializeField] private float _startDelay = 3f;

    [Tooltip("Seconds to wait after triggering openDoor before the box starts moving.")]
    [SerializeField] private float _doorOpenDelay = 1.5f;

    [Tooltip("Seconds to travel between each consecutive waypoint.")]
    [SerializeField] private float _segmentDuration = 2f;

    [Tooltip("Easing applied to each movement segment.")]
    [SerializeField] private Ease _moveEase = Ease.InOutSine;

    // ── Private State ─────────────────────────────────────────────────────────

    private static readonly int OpenDoorTrigger = Animator.StringToHash("openDoor");

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
            Debug.Log($"[SupplyBoxDeliveryController] Subscribed to ShiftManager.OnDayStart on server. Animator assigned: {_deliveryDoorAnimator != null}", this);
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
        StartCoroutine(StartDeliverySequenceWithDelay());
    }

    private IEnumerator StartDeliverySequenceWithDelay()
    {
        Debug.Log($"[SupplyBoxDeliveryController] Day started. Waiting {_startDelay}s before delivery...", this);
        
        if (_startDelay > 0)
            yield return new WaitForSeconds(_startDelay);

        Debug.Log($"[SupplyBoxDeliveryController] Beginning delivery sequence. ActiveDay: {(CampaignManager.Instance?.ActiveDay != null ? CampaignManager.Instance.ActiveDay.DayNumber.ToString() : "null")}", this);
        
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

        // Play spatialized delivery sound using SFXController
        if (_deliverySound != null && SFXController.Instance != null)
        {
            Vector3 soundPos = _spawnPoint != null ? _spawnPoint.position : transform.position;
            SFXController.Instance.PlayAtPosition(_deliverySound, soundPos);
        }

        SpawnSupplyBox();
        SpawnItems(day.SupplyBoxItemPrefabs);
        StartCoroutine(RunDeliverySequence());
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnSupplyBox()
    {
        if (_supplyBoxPrefab == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] _supplyBoxPrefab is not assigned.", this);
            return;
        }

        if (_spawnPoint == null)
        {
            Debug.LogError("[SupplyBoxDeliveryController] _spawnPoint is not assigned.", this);
            return;
        }

        GameObject instance = Instantiate(_supplyBoxPrefab, _spawnPoint.position, _spawnPoint.rotation);
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

    // ── Delivery Sequence ─────────────────────────────────────────────────────

    private IEnumerator RunDeliverySequence()
    {
        TriggerDeliveryDoorClientRpc();

        yield return new WaitForSeconds(_doorOpenDelay);

        if (_waypoints != null)
        {
            foreach (Transform waypoint in _waypoints)
            {
                if (waypoint == null || _activeBox == null) continue;

                bool moveDone = false;
                bool rotateDone = false;

                _activeBox.transform
                    .DOMove(waypoint.position, _segmentDuration)
                    .SetEase(_moveEase)
                    .OnComplete(() => moveDone = true);

                _activeBox.transform
                    .DORotateQuaternion(waypoint.rotation, _segmentDuration)
                    .SetEase(_moveEase)
                    .OnComplete(() => rotateDone = true);

                yield return new WaitUntil(() => moveDone && rotateDone);
            }
        }

        if (_activeBox == null) yield break;

        _activeBox.SetCanPickUpNetworked(true);
        _activeBox.UnlockInteractableNetworked();
        _activeBox.FinalizeDeliveryClientRpc();

        foreach (NetworkObject item in _spawnedItems)
        {
            if (item != null && item.TryGetComponent(out PickableObject pickable))
                pickable.UnlockInteractableNetworked();
        }

        Debug.Log("[SupplyBoxDeliveryController] Delivery sequence complete.", this);
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────────

    /// <summary>Fires the "openDoor" trigger on the delivery door animator on every client.</summary>
    [ClientRpc]
    private void TriggerDeliveryDoorClientRpc()
    {
        if (_deliveryDoorAnimator != null)
        {
            if (!_deliveryDoorAnimator.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[SupplyBoxDeliveryController] TriggerDeliveryDoorClientRpc: Animator GameObject is inactive!", this);
                _deliveryDoorAnimator.gameObject.SetActive(true);
            }

            if (!_deliveryDoorAnimator.enabled)
            {
                Debug.LogWarning("[SupplyBoxDeliveryController] TriggerDeliveryDoorClientRpc: Animator component is disabled!", this);
                _deliveryDoorAnimator.enabled = true;
            }

            // Ensure the base layer weight is 1
            if (_deliveryDoorAnimator.layerCount > 0)
                _deliveryDoorAnimator.SetLayerWeight(0, 1f);

            _deliveryDoorAnimator.SetTrigger(OpenDoorTrigger);
            Debug.Log($"[SupplyBoxDeliveryController] Triggered 'openDoor' on {_deliveryDoorAnimator.name}. Layer 0 weight: {_deliveryDoorAnimator.GetLayerWeight(0)}", this);
        }
        else
        {
            Debug.LogWarning("[SupplyBoxDeliveryController] TriggerDeliveryDoorClientRpc: _deliveryDoorAnimator is null on this client!", this);
        }
    }
}
