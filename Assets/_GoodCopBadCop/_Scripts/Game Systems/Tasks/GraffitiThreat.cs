using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: graffiti naturally accumulates on checkpoint walls over time.
/// Replaces CleanGraffitiTask — there is no fixed count to scrub; graffiti spawns
/// continuously using the same day-intensity scaling pattern as MutantSpawner.
///
/// Players reduce threat by scrubbing pieces with a Mop (via GraffitiInteractable).
/// Threat level equals active graffiti count divided by <see cref="_maxTrackedGraffiti"/>.
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Assign _graffitiPrefabs (each registered in NetworkManager's prefab list).
///   - Assign _spawnPoints (Transforms on checkpoint walls).
///   - Register this component in BetweenShiftTaskManager._threatBehaviours.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GraffitiThreat : NetworkBehaviour, ISystemicThreat
{
    public static GraffitiThreat Instance { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Graffiti";
    [SerializeField] private float _scoreWeight = 1f;

    [Header("Spawning")]
    [Tooltip("Pool of graffiti prefabs to choose from at random. Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _graffitiPrefabs;

    [Tooltip("Transforms on checkpoint walls where graffiti can appear.")]
    [SerializeField] private Transform[] _spawnPoints;

    [Tooltip("Active graffiti count at which ThreatLevel reaches 1.")]
    [SerializeField] private int _maxTrackedGraffiti = 12;

    [Header("Day Scaling")]
    [Tooltip("Campaign day on which graffiti starts appearing.")]
    [SerializeField] private int _firstActiveDay = 1;

    [Tooltip("Campaign day at which spawn rate reaches its peak values.")]
    [SerializeField] private int _peakScalingDay = 15;

    [Tooltip("Intensity curve: X = normalised day progress (0–1), Y = intensity (0–1).")]
    [SerializeField] private AnimationCurve _dayIntensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Spawn Interval (Peak)")]
    [Tooltip("Minimum seconds between spawns at peak intensity.")]
    [SerializeField] private float _spawnIntervalMin = 20f;

    [Tooltip("Maximum seconds between spawns at peak intensity.")]
    [SerializeField] private float _spawnIntervalMax = 45f;

    [Header("Spawn Interval (Sparse)")]
    [Tooltip("Minimum seconds between spawns at sparse (first-day) intensity.")]
    [SerializeField] private float _sparseIntervalMin = 120f;

    [Tooltip("Maximum seconds between spawns at sparse (first-day) intensity.")]
    [SerializeField] private float _sparseIntervalMax = 240f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedGraffiti = new();

    /// <summary>Maps each currently-spawned graffiti's NetworkObject to the spawn point
    /// index it occupies, so a spot is freed up again once that piece is scrubbed.</summary>
    private readonly Dictionary<NetworkObject, int> _occupiedPointsByGraffiti = new();

    /// <summary>Spawn point indices currently occupied by active graffiti.</summary>
    private readonly HashSet<int> _occupiedPointIndices = new();

    private int _activeGraffitiCount;
    private Coroutine _spawnCoroutine;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription =>
        $"Graffiti coverage: {(_networkThreatLevel.Value * 100f):F0}%";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GraffitiThreat] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>Clears existing graffiti and starts the continuous spawn loop. SERVER ONLY.</summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        DespawnExistingGraffiti();
        _activeGraffitiCount      = 0;
        _networkThreatLevel.Value = 0f;

        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        _spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>Stops the spawn loop. Existing graffiti persists as a day-shift consequence. SERVER ONLY.</summary>
    public void EndNightPhase()
    {
        if (!IsServer) return;

        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
    }

    // ── Scrub callback ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GraffitiInteractable on the server when a piece is fully scrubbed.
    /// Decrements the active count and updates the networked threat level.
    /// Kept for pieces spawned without a tracked point index (should not normally
    /// happen via <see cref="SpawnSingleGraffiti"/>, which always assigns
    /// <see cref="GraffitiInteractable.OnScrubCompleted"/>).
    /// </summary>
    public void OnGraffitiScrubbed()
    {
        if (!IsServer) return;

        _activeGraffitiCount = Mathf.Max(0, _activeGraffitiCount - 1);
        _networkThreatLevel.Value = _maxTrackedGraffiti > 0
            ? (float)_activeGraffitiCount / _maxTrackedGraffiti
            : 0f;
    }

    /// <summary>
    /// Called when a specific tracked graffiti piece is fully scrubbed. Frees its spawn
    /// point so a future spawn can reuse that spot, in addition to the bookkeeping done
    /// by <see cref="OnGraffitiScrubbed"/>.
    /// </summary>
    private void OnGraffitiScrubbedAt(NetworkObject netObj, int pointIndex)
    {
        if (!IsServer) return;

        _occupiedPointIndices.Remove(pointIndex);
        _occupiedPointsByGraffiti.Remove(netObj);
        _spawnedGraffiti.Remove(netObj);

        OnGraffitiScrubbed();
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        if (!IsServer) return;

        DespawnExistingGraffiti();
        _activeGraffitiCount      = 0;
        _networkThreatLevel.Value = 0f;
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            float intensity    = GetDayIntensity();
            float intervalMin  = Mathf.Lerp(_sparseIntervalMin, _spawnIntervalMin, intensity);
            float intervalMax  = Mathf.Lerp(_sparseIntervalMax, _spawnIntervalMax, intensity);
            float interval     = Random.Range(intervalMin, intervalMax);

            yield return new WaitForSeconds(interval);

            if (_activeGraffitiCount < _maxTrackedGraffiti)
                SpawnSingleGraffiti();
        }
    }

    private void SpawnSingleGraffiti()
    {
        if (_graffitiPrefabs == null || _graffitiPrefabs.Length == 0)
        {
            Debug.LogError("[GraffitiThreat] _graffitiPrefabs is empty — assign at least one prefab.");
            return;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogError("[GraffitiThreat] _spawnPoints is empty — assign at least one spawn point.");
            return;
        }

        // Only consider spawn points not already occupied by active graffiti.
        List<int> availableIndices = new(_spawnPoints.Length);
        for (int i = 0; i < _spawnPoints.Length; i++)
            if (!_occupiedPointIndices.Contains(i))
                availableIndices.Add(i);

        if (availableIndices.Count == 0)
        {
            // Every spot is currently covered — nothing to do until one is scrubbed.
            return;
        }

        int pointIndex = availableIndices[Random.Range(0, availableIndices.Count)];
        Transform  point  = _spawnPoints[pointIndex];
        GameObject prefab = _graffitiPrefabs[Random.Range(0, _graffitiPrefabs.Length)];

        GameObject    go     = Instantiate(prefab, point.position, point.rotation);
        NetworkObject netObj = go.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[GraffitiThreat] Graffiti prefab '{prefab.name}' has no NetworkObject component.");
            Destroy(go);
            return;
        }

        GraffitiInteractable interactable = go.GetComponent<GraffitiInteractable>();
        if (interactable != null)
            interactable.OnScrubCompleted = () => OnGraffitiScrubbedAt(netObj, pointIndex);

        netObj.Spawn(destroyWithScene: true);
        _spawnedGraffiti.Add(netObj);
        _occupiedPointIndices.Add(pointIndex);
        _occupiedPointsByGraffiti[netObj] = pointIndex;
        _activeGraffitiCount++;

        _networkThreatLevel.Value = _maxTrackedGraffiti > 0
            ? (float)_activeGraffitiCount / _maxTrackedGraffiti
            : 0f;
    }

    // ── Day intensity ─────────────────────────────────────────────────────────

    private float GetDayIntensity()
    {
        if (CampaignManager.Instance == null) return 1f;

        int day   = CampaignManager.Instance.CurrentDay;
        int range = _peakScalingDay - _firstActiveDay;

        if (range <= 0) return 1f;

        float t = Mathf.Clamp01((float)(day - _firstActiveDay) / range);
        return _dayIntensityCurve.Evaluate(t);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    private void DespawnExistingGraffiti()
    {
        foreach (NetworkObject netObj in _spawnedGraffiti)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedGraffiti.Clear();
        _occupiedPointIndices.Clear();
        _occupiedPointsByGraffiti.Clear();
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
            Gizmos.DrawLine(pos, pos + _spawnPoints[i].forward * 0.4f);
            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"Graffiti {i}");
        }
    }
#endif
}
