using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: trash bags naturally accumulate in spawn zones over time.
/// Replaces TakeOutTrashTask — there is no fixed bag count to deposit; bags spawn
/// continuously using the same day-intensity scaling pattern as MutantSpawner.
///
/// Players reduce threat by picking up bags and depositing them in a DumpsterInteractable.
/// Bags are pruned automatically when they are despawned (deposited by any player).
/// Threat level equals active bag count divided by <see cref="_maxTrackedBags"/>.
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Assign _trashBagPrefab (registered in NetworkManager's prefab list).
///   - Assign _spawnZones with centre Transforms and half-extents.
///   - Set _groundLayer to match your environment layer.
///   - Register this component in BetweenShiftTaskManager._threatBehaviours.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashThreat : NetworkBehaviour, ISystemicThreat
{
    public static TrashThreat Instance { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Trash Build-up";
    [SerializeField] private float _scoreWeight = 1f;

    [Header("Spawning")]
    [Tooltip("Must be registered as a Network Prefab in the NetworkManager.")]
    [SerializeField] private GameObject _trashBagPrefab;

    [Tooltip("One or more zones in which bags are randomly placed.")]
    [SerializeField] private SpawnZone[] _spawnZones;

    [Tooltip("Layer(s) the downward raycast hits to land bags on the ground.")]
    [SerializeField] private LayerMask _groundLayer;

    [Tooltip("Active bag count at which ThreatLevel reaches 1.")]
    [SerializeField] private int _maxTrackedBags = 15;

    [Header("Day Scaling")]
    [Tooltip("Campaign day on which trash starts accumulating.")]
    [SerializeField] private int _firstActiveDay = 1;

    [Tooltip("Campaign day at which spawn rate reaches its peak values.")]
    [SerializeField] private int _peakScalingDay = 15;

    [Tooltip("Intensity curve: X = normalised day progress (0–1), Y = intensity (0–1).")]
    [SerializeField] private AnimationCurve _dayIntensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Spawn Interval (Peak)")]
    [Tooltip("Minimum seconds between bag spawns at peak intensity.")]
    [SerializeField] private float _spawnIntervalMin = 25f;

    [Tooltip("Maximum seconds between bag spawns at peak intensity.")]
    [SerializeField] private float _spawnIntervalMax = 60f;

    [Header("Spawn Interval (Sparse)")]
    [Tooltip("Minimum seconds between bag spawns at sparse (first-day) intensity.")]
    [SerializeField] private float _sparseIntervalMin = 150f;

    [Tooltip("Maximum seconds between bag spawns at sparse (first-day) intensity.")]
    [SerializeField] private float _sparseIntervalMax = 300f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedBags = new();
    private Coroutine _spawnCoroutine;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription =>
        $"Trash bags: {_spawnedBags.Count}/{_maxTrackedBags}";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TrashThreat] Duplicate instance detected — destroying self.");
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

    /// <summary>Clears existing bags and starts the continuous spawn loop. SERVER ONLY.</summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        DespawnExistingBags();

        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        _spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>Stops the spawn loop. Remaining bags persist as a day-shift consequence. SERVER ONLY.</summary>
    public void EndNightPhase()
    {
        if (!IsServer) return;

        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        if (!IsServer) return;

        DespawnExistingBags();
        _networkThreatLevel.Value = 0f;
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            float intensity   = GetDayIntensity();
            float intervalMin = Mathf.Lerp(_sparseIntervalMin, _spawnIntervalMin, intensity);
            float intervalMax = Mathf.Lerp(_sparseIntervalMax, _spawnIntervalMax, intensity);
            float interval    = Random.Range(intervalMin, intervalMax);

            yield return new WaitForSeconds(interval);

            PruneDespawnedBags();

            if (_spawnedBags.Count < _maxTrackedBags)
                SpawnSingleBag();
        }
    }

    private void SpawnSingleBag()
    {
        if (_trashBagPrefab == null)
        {
            Debug.LogError("[TrashThreat] _trashBagPrefab is not assigned.");
            return;
        }

        Vector3    spawnPos = GetRandomSpawnPosition();
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject    bagGo  = Instantiate(_trashBagPrefab, spawnPos, spawnRot);
        NetworkObject netObj = bagGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[TrashThreat] Trash bag prefab has no NetworkObject component.");
            Destroy(bagGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedBags.Add(netObj);

        UpdateThreatLevel();
    }

    /// <summary>Removes stale references (bags despawned by players) and updates the threat level.</summary>
    private void PruneDespawnedBags()
    {
        _spawnedBags.RemoveAll(n => n == null || !n.IsSpawned);
        UpdateThreatLevel();
    }

    private void UpdateThreatLevel()
    {
        _networkThreatLevel.Value = _maxTrackedBags > 0
            ? (float)_spawnedBags.Count / _maxTrackedBags
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

    // ── Spawn position ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a world position within a randomly chosen spawn zone, snapped to the ground
    /// via raycast. Falls back to the zone centre height when no ground is found.
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
    {
        if (_spawnZones == null || _spawnZones.Length == 0)
        {
            Debug.LogWarning("[TrashThreat] No spawn zones assigned; spawning at origin.");
            return Vector3.zero;
        }

        SpawnZone zone = _spawnZones[Random.Range(0, _spawnZones.Length)];

        if (zone.Center == null)
        {
            Debug.LogWarning("[TrashThreat] A spawn zone has no Centre Transform assigned; spawning at origin.");
            return Vector3.zero;
        }

        Vector3 center  = zone.Center.position;
        float   randomX = Random.Range(-zone.HalfExtents.x, zone.HalfExtents.x);
        float   randomZ = Random.Range(-zone.HalfExtents.z, zone.HalfExtents.z);

        Vector3 castOrigin = new Vector3(center.x + randomX, center.y + 5f, center.z + randomZ);

        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f, _groundLayer))
            return hit.point;

        return new Vector3(castOrigin.x, center.y, castOrigin.z);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    private void DespawnExistingBags()
    {
        foreach (NetworkObject netObj in _spawnedBags)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedBags.Clear();
        UpdateThreatLevel();
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

            float hue  = (float)i / Mathf.Max(_spawnZones.Length, 1);
            Color fill = Color.HSVToRGB(hue, 0.7f, 1f);
            fill.a = 0.25f;

            Vector3 size = new Vector3(zone.HalfExtents.x * 2f, 0.1f, zone.HalfExtents.z * 2f);

            Gizmos.color = fill;
            Gizmos.DrawCube(zone.Center.position, size);

            fill.a = 1f;
            Gizmos.color = fill;
            Gizmos.DrawWireCube(zone.Center.position, size);

            UnityEditor.Handles.Label(zone.Center.position + Vector3.up * 0.2f, $"Zone {i}");
        }
    }
#endif
}
