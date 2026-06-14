using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Between-shift task: pick up all trash bags and deposit them in the dumpster.
///
/// This class is obsolete. Use TrashThreat instead.
/// </summary>
[System.Obsolete("Use TrashThreat instead. The between-shift task system has been replaced by the systemic threat model.")]
[RequireComponent(typeof(NetworkObject))]
public class TakeOutTrashTask : NetworkBehaviour, IBetweenShiftTask
{
    public static TakeOutTrashTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName        = "Take Out the Trash";
    [SerializeField] private int    _couponReward     = 10;
    [SerializeField] private int    _totalBags        = 5;

    [Header("Spawning")]
    [Tooltip("Must be registered as a Network Prefab in the NetworkManager.")]
    [SerializeField] private GameObject _trashBagPrefab;
    [Tooltip("One or more zones in which bags are randomly placed. A zone is picked at random for each bag.")]
    [SerializeField] private SpawnZone[] _spawnZones;
    [Tooltip("Layer(s) the downward raycast hits to land bags on the ground.")]
    [SerializeField] private LayerMask  _groundLayer;

    [Header("Dumpsters")]
    [Tooltip("All DumpsterInteractables in the scene. Their deposit counters are reset alongside the task.")]
    [SerializeField] private DumpsterInteractable[] _dumpsters;

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    public string TaskName    => _taskName;
    public int    CouponReward => _couponReward;
    public bool   IsComplete   => _isComplete;

    /// <summary>
    /// Dynamic description reflects current progress.
    /// Example: "Deposit trash bags: 2/5"
    /// </summary>
    public string TaskDescription =>
        _isComplete
            ? $"All {_totalBags} bags deposited!"
            : $"Deposit trash bags: {_bagsDeposited.Value}/{_totalBags}";

    // ── Networked state ──────────────────────────────────────────────────────

    private NetworkVariable<int> _bagsDeposited = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local flag — set on all clients via MarkCompleteClientRpc.
    private bool _isComplete;

    // Server-side: tracks spawned bags so they can be cleaned up on reset.
    private readonly List<NetworkObject> _spawnedBags = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TakeOutTrashTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _bagsDeposited.OnValueChanged += OnBagsDepositedChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _bagsDeposited.OnValueChanged -= OnBagsDepositedChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    /// <summary>
    /// Resets task state at the start of each night phase.
    /// Called on every client by BetweenShiftTaskManager.BeginNightPhase().
    /// Bag spawning is server-only so only one authoritative set of bags is created.
    /// </summary>
    public void ResetTask()
    {
        _isComplete = false;

        if (!IsServer) return;

        _bagsDeposited.Value = 0;
        DespawnExistingBags();
        SpawnTrashBags();
        ResetDumpsters();
    }

    // ── Deposit flow ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by DumpsterInteractable on the local client after a bag is accepted.
    /// Routes deposit acknowledgement to the server, which is the single authority
    /// for the deposit counter.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DepositBagServerRpc()
    {
        if (_isComplete) return;

        _bagsDeposited.Value = Mathf.Clamp(_bagsDeposited.Value + 1, 0, _totalBags);

        if (_bagsDeposited.Value >= _totalBags)
        {
            // Mark complete immediately on the server to guard against duplicate calls
            // arriving before MarkCompleteClientRpc propagates.
            _isComplete = true;

            // Notify ShiftManager from the server — one authoritative call.
            if (BetweenShiftTaskManager.Instance != null)
                BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);

            // Propagate completion state and UI refresh to all clients.
            MarkCompleteClientRpc();
        }
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── Bag spawning (server only) ────────────────────────────────────────────

    private void SpawnTrashBags()
    {
        if (_trashBagPrefab == null)
        {
            Debug.LogError("[TakeOutTrashTask] _trashBagPrefab is not assigned.");
            return;
        }

        for (int i = 0; i < _totalBags; i++)
        {
            Vector3 spawnPos = GetRandomSpawnPosition();
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject bagGo = Instantiate(_trashBagPrefab, spawnPos, spawnRot);
            NetworkObject netObj = bagGo.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError("[TakeOutTrashTask] Trash bag prefab has no NetworkObject component.");
                Destroy(bagGo);
                continue;
            }

            netObj.Spawn(destroyWithScene: true);
            _spawnedBags.Add(netObj);
        }
    }

    private void ResetDumpsters()
    {
        // ResetServerRpc has been removed from DumpsterInteractable.
        // Dumpster resets are now handled automatically by HQPickupDispatcher.
        Debug.LogWarning("[TakeOutTrashTask] ResetDumpsters is obsolete — dumpsters no longer support ResetServerRpc.");
    }

    private void DespawnExistingBags()
    {
        foreach (NetworkObject netObj in _spawnedBags)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedBags.Clear();
    }

    /// <summary>
    /// Returns a world position within a randomly chosen spawn zone, snapped to the
    /// ground via raycast. Falls back to the zone's centre height when no ground is found.
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
    {
        if (_spawnZones == null || _spawnZones.Length == 0)
        {
            Debug.LogWarning("[TakeOutTrashTask] No spawn zones assigned; spawning at origin.");
            return Vector3.zero;
        }

        // Pick a random zone, then a random point inside it.
        SpawnZone zone = _spawnZones[Random.Range(0, _spawnZones.Length)];

        if (zone.Center == null)
        {
            Debug.LogWarning("[TakeOutTrashTask] A spawn zone has no Centre Transform assigned; spawning at origin.");
            return Vector3.zero;
        }

        Vector3 center  = zone.Center.position;
        float   randomX = Random.Range(-zone.HalfExtents.x, zone.HalfExtents.x);
        float   randomZ = Random.Range(-zone.HalfExtents.z, zone.HalfExtents.z);

        // Cast from above the zone downward to land on the ground.
        Vector3 castOrigin = new Vector3(center.x + randomX, center.y + 5f, center.z + randomZ);

        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f, _groundLayer))
            return hit.point;

        // Fallback: use the zone centre Y if no ground surface was hit.
        return new Vector3(castOrigin.x, center.y, castOrigin.z);
    }

    // ── Progress sync ─────────────────────────────────────────────────────────

    private void OnBagsDepositedChanged(int previous, int current)
    {
        // Refresh the guidebook task row description on all clients whenever progress changes.
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_spawnZones == null) return;

        for (int i = 0; i < _spawnZones.Length; i++)
        {
            SpawnZone zone = _spawnZones[i];
            if (zone.Center == null) continue;

            // Cycle hue across zones so they're visually distinct.
            float hue = (float)i / Mathf.Max(_spawnZones.Length, 1);
            Color fill = Color.HSVToRGB(hue, 0.7f, 1f);
            fill.a = 0.25f;

            Vector3 size = new Vector3(zone.HalfExtents.x * 2f, 0.1f, zone.HalfExtents.z * 2f);

            Gizmos.color = fill;
            Gizmos.DrawCube(zone.Center.position, size);

            fill.a = 1f;
            Gizmos.color = fill;
            Gizmos.DrawWireCube(zone.Center.position, size);

            // Label each zone in the Scene view.
            UnityEditor.Handles.Label(
                zone.Center.position + Vector3.up * 0.2f,
                $"Zone {i}");
        }
    }
#endif
}
