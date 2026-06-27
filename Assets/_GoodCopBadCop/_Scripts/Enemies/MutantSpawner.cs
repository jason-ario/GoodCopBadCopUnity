using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative spawner that periodically creates bursts of MutantEnemy instances
/// at random positions within a configurable box volume in the woods.
/// The box is centred on this GameObject's position.
/// Spawn intervals are randomised between <see cref="spawnIntervalMin"/> and <see cref="spawnIntervalMax"/>.
/// Each interval triggers a burst: a rapid sequence of spawns with a short delay between each one.
/// </summary>
public class MutantSpawner : NetworkBehaviour
{
    // ── Configuration ──────────────────────────────────────────────────────────

    [Header("Enemy Setup")]
    [Tooltip("Networked prefabs to choose from at random. Each must contain a MutantEnemy component and be registered in NetworkManager's prefab list.")]
    [SerializeField] private GameObject[] mutantPrefabs;

    [Header("Aggro Target")]
    [Tooltip("Optional target (e.g. the booth) that aggroed mutants will head toward on spawn. Each mutant's aggroChance (from its MutantEnemyData) determines whether it actually aggros.")]
    [SerializeField] private Transform aggroTarget;

    [Header("Spawn Area")]
    [Tooltip("Half-extents of the axis-aligned box (in local space) within which enemies can spawn. The box is centred on this GameObject's position.")]
    [SerializeField] private Vector3 spawnBoxHalfExtents = new Vector3(20f, 0f, 20f);

    [Header("Timing")]
    [Tooltip("Seconds before the first burst after the game starts.")]
    [SerializeField] private float initialDelay = 10f;

    [Tooltip("Minimum seconds between consecutive bursts.")]
    [SerializeField] private float spawnIntervalMin = 30f;

    [Tooltip("Maximum seconds between consecutive bursts.")]
    [SerializeField] private float spawnIntervalMax = 60f;

    [Header("Burst")]
    [Tooltip("Minimum number of enemies spawned per burst.")]
    [SerializeField] private int burstCountMin = 2;

    [Tooltip("Maximum number of enemies spawned per burst.")]
    [SerializeField] private int burstCountMax = 5;

    [Tooltip("Seconds between each individual spawn within a burst.")]
    [SerializeField] private float burstSpawnDelay = 0.5f;

    [Header("Cap")]
    [Tooltip("Maximum number of active enemies this spawner will maintain. Individual burst spawns are skipped once at or above this cap.")]
    [SerializeField] private int maxActiveEnemies = 10;

    [Header("Activation")]
    [Tooltip("The first campaign day on which this spawner becomes active.")]
    [SerializeField] private int firstActiveDay = 2;

    [Header("Day Scaling")]
    [Tooltip("When enabled, spawn parameters scale up with the current campaign day, starting sparse and growing toward the values above.")]
    [SerializeField] private bool scaledByDay = false;

    [Tooltip("Campaign day at which the spawner reaches its full (peak) intensity. Days beyond this are clamped to full intensity.")]
    [SerializeField] private int peakScalingDay = 7;

    [Tooltip("Intensity curve: X is normalised day progress (0 = firstActiveDay, 1 = peakScalingDay), Y is intensity (0 = sparse baseline, 1 = full values above). Defaults to ease-in-out.")]
    [SerializeField] private AnimationCurve dayIntensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Minimum seconds between bursts at sparse intensity (first active day).")]
    [SerializeField] private float sparseIntervalMin = 120f;

    [Tooltip("Maximum seconds between bursts at sparse intensity (first active day).")]
    [SerializeField] private float sparseIntervalMax = 240f;

    [Tooltip("Minimum enemies per burst at sparse intensity.")]
    [SerializeField] private int sparseBurstCountMin = 1;

    [Tooltip("Maximum enemies per burst at sparse intensity.")]
    [SerializeField] private int sparseBurstCountMax = 1;

    [Tooltip("Maximum active enemy cap at sparse intensity.")]
    [SerializeField] private int sparseMaxActiveEnemies = 1;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _activeEnemies = new List<NetworkObject>();
    private bool _isRunning;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        if (mutantPrefabs == null || mutantPrefabs.Length == 0)
        {
            Debug.LogError("[MutantSpawner] mutantPrefabs is empty. Spawner will not run.", this);
            return;
        }

        CampaignManager.OnDayChanged += OnDayChanged;

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart      += OnShiftStarted;
            ShiftManager.Instance.OnNightPhaseBegin += OnNightPhaseBegun;
        }
        else
        {
            Debug.LogWarning("[MutantSpawner] ShiftManager.Instance not found — day/night gating unavailable.", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        CampaignManager.OnDayChanged -= OnDayChanged;
        _isRunning = false;

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart      -= OnShiftStarted;
            ShiftManager.Instance.OnNightPhaseBegin -= OnNightPhaseBegun;
        }
    }

    /// <summary>
    /// Stops spawning if the day drops below <see cref="firstActiveDay"/>.
    /// Does not start spawning on day advance — that is handled by <see cref="OnNightPhaseBegun"/>.
    /// SERVER ONLY (subscribed only on the server in <see cref="OnNetworkSpawn"/>).
    /// </summary>
    private void OnDayChanged(int newDay)
    {
        if (newDay < firstActiveDay && _isRunning)
            StopSpawning();
    }

    /// <summary>
    /// Stops the spawner when the shift starts so mutants do not spawn during the day phase.
    /// </summary>
    private void OnShiftStarted()
    {
        if (_isRunning)
        {
            StopSpawning();
            Debug.Log("[MutantSpawner] Spawning paused — shift started (day phase).");
        }
    }

    /// <summary>
    /// Starts the spawner when the night phase begins, provided the current day meets
    /// the <see cref="firstActiveDay"/> threshold.
    /// </summary>
    private void OnNightPhaseBegun()
    {
        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
        if (currentDay >= firstActiveDay)
        {
            BeginSpawning();
            Debug.Log($"[MutantSpawner] Spawning started — night phase begun (Day {currentDay}).");
        }
    }

    private void BeginSpawning()
    {
        _isRunning = true;
        StartCoroutine(SpawnLoop());
        Debug.Log($"[MutantSpawner] Spawner activated on Day {CampaignManager.Instance?.CurrentDay}.");
    }

    // ── Day Scaling ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a 0..1 intensity value based on the current campaign day.
    /// Returns 1 when <see cref="scaledByDay"/> is disabled.
    /// </summary>
    private float GetDayIntensity()
    {
        if (!scaledByDay || CampaignManager.Instance == null)
            return 1f;

        int day = CampaignManager.Instance.CurrentDay;
        int range = peakScalingDay - firstActiveDay;

        if (range <= 0)
            return 1f;

        float t = Mathf.Clamp01((float)(day - firstActiveDay) / range);
        return dayIntensityCurve.Evaluate(t);
    }

    // ── Spawn Loop ─────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(initialDelay);

        while (_isRunning)
        {
            yield return StartCoroutine(SpawnBurst());

            float intensity = GetDayIntensity();
            float effectiveMin = Mathf.Lerp(sparseIntervalMin, spawnIntervalMin, intensity);
            float effectiveMax = Mathf.Lerp(sparseIntervalMax, spawnIntervalMax, intensity);
            float interval = Random.Range(effectiveMin, effectiveMax);
            yield return new WaitForSeconds(interval);
        }
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a rapid sequence of enemies, one at a time, with <see cref="burstSpawnDelay"/> between each.
    /// The burst count is randomised between the effective min and max values (scaled by day when enabled).
    /// Individual spawns are skipped (but the delay still elapses) when the effective active cap is reached.
    /// </summary>
    private IEnumerator SpawnBurst()
    {
        float intensity = GetDayIntensity();
        int effectiveBurstMin = Mathf.RoundToInt(Mathf.Lerp(sparseBurstCountMin, burstCountMin, intensity));
        int effectiveBurstMax = Mathf.RoundToInt(Mathf.Lerp(sparseBurstCountMax, burstCountMax, intensity));
        int effectiveCap = Mathf.RoundToInt(Mathf.Lerp(sparseMaxActiveEnemies, maxActiveEnemies, intensity));
        int count = Random.Range(effectiveBurstMin, effectiveBurstMax + 1);

        for (int i = 0; i < count; i++)
        {
            if (!_isRunning)
                yield break;

            PruneDeadEnemies();

            if (_activeEnemies.Count < effectiveCap)
                SpawnSingleEnemy();

            if (i < count - 1)
                yield return new WaitForSeconds(burstSpawnDelay);
        }
    }

    private void SpawnSingleEnemy(bool forceAggro = false)
    {
        Vector3 localOffset = new Vector3(
            Random.Range(-spawnBoxHalfExtents.x, spawnBoxHalfExtents.x),
            Random.Range(-spawnBoxHalfExtents.y, spawnBoxHalfExtents.y),
            Random.Range(-spawnBoxHalfExtents.z, spawnBoxHalfExtents.z)
        );

        Vector3 spawnPosition = transform.TransformPoint(localOffset);
        Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject prefab = mutantPrefabs[Random.Range(0, mutantPrefabs.Length)];
        GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[MutantSpawner] A prefab in mutantPrefabs is missing a NetworkObject component.", this);
            Destroy(instance);
            return;
        }

        // Assign the aggro target before Spawn() so InitialiseServer can read it.
        MutantEnemy enemy = instance.GetComponent<MutantEnemy>();
        if (aggroTarget != null)
            enemy?.SetAggroTarget(aggroTarget);
        if (forceAggro)
            enemy?.SetForceAggro(true);

        netObj.Spawn(true);
        _activeEnemies.Add(netObj);
    }

    /// <summary>
    /// Removes entries from the active list that have already been despawned or have died
    /// (death animation may still be playing before despawn occurs).
    /// </summary>
    private void PruneDeadEnemies()
    {
        _activeEnemies.RemoveAll(netObj =>
        {
            if (netObj == null || !netObj.IsSpawned)
                return true;

            MutantEnemy enemy = netObj.GetComponent<MutantEnemy>();
            return enemy != null && enemy.IsDead;
        });
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually triggers an immediate burst. SERVER ONLY.
    /// </summary>
    public void ForceSpawn()
    {
        if (!IsServer)
            return;

        StartCoroutine(SpawnBurst());
    }

    /// <summary>
    /// Spawns a single enemy that is guaranteed to be in aggro mode, heading straight
    /// for the assigned <see cref="aggroTarget"/> regardless of <see cref="MutantEnemyData.aggroChance"/>.
    /// Respects the active-enemy cap (day-scaled when enabled). SERVER ONLY.
    /// </summary>
    public void ForceSpawnAggroed()
    {
        if (!IsServer)
            return;

        PruneDeadEnemies();

        int effectiveCap = Mathf.RoundToInt(Mathf.Lerp(sparseMaxActiveEnemies, maxActiveEnemies, GetDayIntensity()));
        if (_activeEnemies.Count >= effectiveCap)
        {
            Debug.LogWarning("[MutantSpawner] ForceSpawnAggroed skipped — active enemy cap reached.", this);
            return;
        }

        SpawnSingleEnemy(forceAggro: true);
        Debug.Log("[MutantSpawner] Forced aggroed mutant spawn.");
    }

    /// <summary>
    /// Stops the spawner loop. Existing enemies remain active. SERVER ONLY.
    /// </summary>
    public void StopSpawning()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Current tracked active enemy count. Prunes dead entries before returning. SERVER ONLY.
    /// </summary>
    public int ActiveEnemyCount
    {
        get
        {
            PruneDeadEnemies();
            return _activeEnemies.Count;
        }
    }

    /// <summary>
    /// Restarts the spawner loop after it has been stopped. SERVER ONLY.
    /// </summary>
    public void ResumeSpawning()
    {
        if (!IsServer || _isRunning)
            return;

        _isRunning = true;
        StartCoroutine(SpawnLoop());
    }

    /// <summary>
    /// Despawns all currently tracked active enemies. SERVER ONLY.
    /// </summary>
    public void DespawnAllEnemies()
    {
        if (!IsServer)
            return;

        foreach (NetworkObject netObj in _activeEnemies)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
        }

        _activeEnemies.Clear();
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, spawnBoxHalfExtents * 2f);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, spawnBoxHalfExtents * 2f);
    }
}
