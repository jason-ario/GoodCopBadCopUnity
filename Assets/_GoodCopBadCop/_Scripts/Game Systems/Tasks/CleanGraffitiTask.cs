using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Between-shift task: scrub off all graffiti pieces from the checkpoint walls using a mop.
///
/// On each task cycle the server randomly picks <see cref="_graffitiCount"/> spawn points,
/// then instantiates a random graffiti prefab from the pool at each one. Once every piece
/// has been scrubbed off the task is marked complete and the coupon reward is awarded.
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign <see cref="_graffitiPrefabs"/>: one or more prefabs, each registered as a Network Prefab.
///   - Assign <see cref="_spawnPoints"/>: Transforms placed on the checkpoint walls.
///   - Adjust <see cref="_graffitiCount"/> to control difficulty.
///   - Register this component on <see cref="BetweenShiftTaskManager"/> via the Inspector task list.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanGraffitiTask : NetworkBehaviour, IBetweenShiftTask
{
    public static CleanGraffitiTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName    = "Clean Graffiti";
    [SerializeField] private int    _couponReward = 10;
    [Tooltip("Number of graffiti pieces to spawn per task cycle.")]
    [SerializeField] private int    _graffitiCount = 4;

    [Header("Spawning")]
    [Tooltip("Pool of graffiti prefabs to pick from at random. Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _graffitiPrefabs;
    [Tooltip("Transforms on the checkpoint walls where graffiti can appear. A point is picked at random for each piece.")]
    [SerializeField] private Transform[]  _spawnPoints;

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    public string TaskName    => _taskName;
    public int    CouponReward => _couponReward;
    public bool   IsComplete   => _isComplete;

    /// <summary>Dynamic description reflects current scrub progress.</summary>
    public string TaskDescription =>
        _isComplete
            ? $"All {_graffitiCount} pieces scrubbed!"
            : $"Scrub graffiti: {_scrubbed.Value}/{_graffitiCount}";

    // ── Networked state ──────────────────────────────────────────────────────

    private NetworkVariable<int> _scrubbed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local flag — set on all clients via MarkCompleteClientRpc.
    private bool _isComplete;

    // Server-side: tracks spawned graffiti so they can be cleaned up on reset.
    private readonly List<NetworkObject> _spawnedGraffiti = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CleanGraffitiTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _scrubbed.OnValueChanged += OnScrubbedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubbed.OnValueChanged -= OnScrubbedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    /// <summary>
    /// Resets task state for a new night phase. Server spawns fresh graffiti;
    /// all clients reset the local completion flag.
    /// </summary>
    public void ResetTask()
    {
        _isComplete = false;

        if (!IsServer) return;

        _scrubbed.Value = 0;
        DespawnExistingGraffiti();
        SpawnGraffiti();
    }

    // ── Scrub callback (called by GraffitiInteractable on the server) ─────────

    /// <summary>
    /// Called by <see cref="GraffitiInteractable"/> on the server once a piece has been
    /// fully scrubbed. Increments the progress counter and marks the task complete when
    /// all pieces are done.
    /// </summary>
    public void OnGraffitiScrubbed()
    {
        if (!IsServer || _isComplete) return;

        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _graffitiCount);

        if (_scrubbed.Value >= _graffitiCount)
        {
            // Guard on server before the ClientRpc propagates.
            _isComplete = true;

            if (BetweenShiftTaskManager.Instance != null)
                BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);

            MarkCompleteClientRpc();
        }
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        TaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    /// <summary>Cleans up any remaining graffiti when a new day begins.</summary>
    private void OnDayStart()
    {
        _isComplete = false;

        if (!IsServer) return;

        _scrubbed.Value = 0;
        DespawnExistingGraffiti();
    }

    // ── Spawning (server only) ────────────────────────────────────────────────

    private void SpawnGraffiti()
    {
        if (_graffitiPrefabs == null || _graffitiPrefabs.Length == 0)
        {
            Debug.LogError("[CleanGraffitiTask] _graffitiPrefabs is empty — assign at least one prefab.");
            return;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogError("[CleanGraffitiTask] _spawnPoints is empty — assign at least one spawn point.");
            return;
        }

        for (int i = 0; i < _graffitiCount; i++)
        {
            Transform point  = _spawnPoints[Random.Range(0, _spawnPoints.Length)];
            GameObject prefab = _graffitiPrefabs[Random.Range(0, _graffitiPrefabs.Length)];

            GameObject go = Instantiate(prefab, point.position, point.rotation);
            NetworkObject netObj = go.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[CleanGraffitiTask] Graffiti prefab '{prefab.name}' has no NetworkObject component.");
                Destroy(go);
                continue;
            }

            netObj.Spawn(destroyWithScene: true);
            _spawnedGraffiti.Add(netObj);
        }
    }

    private void DespawnExistingGraffiti()
    {
        foreach (NetworkObject netObj in _spawnedGraffiti)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedGraffiti.Clear();
    }

    // ── Progress sync ──────────────────────────────────────────────────────────

    private void OnScrubbedChanged(int previous, int current)
    {
        TaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_spawnPoints == null) return;

        Gizmos.color = new Color(0.8f, 0.2f, 0.9f, 0.9f);

        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i] == null) continue;

            Vector3 pos = _spawnPoints[i].position;

            Gizmos.DrawWireSphere(pos, 0.15f);

            // Draw a forward arrow so it's clear which way the graffiti faces on the wall.
            Gizmos.DrawLine(pos, pos + _spawnPoints[i].forward * 0.4f);

            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"Graffiti {i}");
        }
    }
#endif
}
