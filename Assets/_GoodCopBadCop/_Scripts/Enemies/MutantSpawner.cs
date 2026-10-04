using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative spawner that periodically creates bursts of MutantEnemy instances
/// at random positions within a configurable box volume in the woods.
/// The box is centred on this GameObject's position.
/// Spawn intervals are randomised between <see cref="spawnIntervalMin"/> and <see cref="spawnIntervalMax"/>.
/// Each interval triggers a burst: a rapid sequence of spawns with a short delay between each one.
///
/// Ambient mutants roam at any time of day — spawning is gated only by <see cref="firstActiveDay"/>
/// (and, when <see cref="requiresZoneActivation"/> is set, by zone entry) and otherwise runs
/// continuously regardless of shift/day-night state, until <see cref="MaxAmbientSpawnsPerDay"/> ambient
/// spawns have occurred that day across ALL spawners combined (a shared, map-wide budget) — then
/// every spawner pauses until the campaign day changes. Spawners with <see cref="exemptFromDailyCap"/>
/// set (e.g. the power plant, needed for the Day 3 power outage) ignore that budget and don't consume it.
///
/// Ambient mutants spawned this way (including legacy-mutant reintroductions) never start
/// aggroed — they always spawn with no aggro target, ignoring <see cref="MutantEnemyData.aggroChance"/>
/// entirely. Aggro is reserved for <see cref="MutantBreachManager"/> breach mutants (when
/// <see cref="MutantBreachData.forceAggro"/> is set), or via the debug-only
/// <see cref="ForceSpawnAggroed"/> cheat.
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

    [Tooltip("Random spawn points are projected down onto the ground from this height above the spawner, " +
             "so hills and dips inside the spawn box are handled. Must clear the tallest hill in the box.")]
    [SerializeField] private float groundProbeHeight = 60f;

    [Tooltip("Random spawn points to try before giving up on a single spawn. Each attempt must land on the " +
             "NavMesh and have a complete path back to the spawner's area (no disconnected hill islands).")]
    [SerializeField, Min(1)] private int spawnPlacementAttempts = 12;

    [Tooltip("Optional point on the main walkable NavMesh (e.g. a clearing or road in the woods). Spawn points " +
             "must have a complete NavMesh path to it, which rejects small disconnected patches on steep hills. " +
             "Leave empty to use the NavMesh point nearest the spawn centre. Avoid points inside the fenced " +
             "compound: carved fences make it unreachable outside breaches.")]
    [SerializeField] private Transform navReachabilityAnchor;

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

    [Tooltip("Maximum number of ambient enemies this spawner may spawn per campaign day. Once reached, " +
             "ambient spawning stops until the next day. Scripted packs (SpawnPackAt) and the debug " +
             "aggro cheat are not counted. 0 = unlimited.")]
    [SerializeField, Min(0)] private int maxSpawnsPerDay = 5;

    [Tooltip("When enabled, this spawner ignores the shared map-wide daily cap (MaxAmbientSpawnsPerDay) " +
             "and its spawns do not consume that budget. Use for scripted/location spawners that must " +
             "always populate their area, e.g. the power plant during the Day 3 power outage.")]
    [SerializeField] private bool exemptFromDailyCap = false;

    [Header("Activation")]
    [Tooltip("The first campaign day on which this spawner becomes active.")]
    [SerializeField] private int firstActiveDay = 2;

    [Tooltip("When enabled, this spawner will NOT start automatically on the night phase. " +
             "Instead it only begins once a player enters its associated zone trigger " +
             "(wired via a MutantSpawnerZoneTrigger component). " +
             "Useful for location-specific spawners such as the power plant.")]
    [SerializeField] private bool requiresZoneActivation = false;

    [Tooltip("When enabled together with Requires Zone Activation, entering the zone fires a single burst " +
             "instead of starting the continuous spawn loop. After the burst completes, the spawner enters " +
             "a cooldown period before zone entry can trigger another burst.")]
    [SerializeField] private bool burstOnlyMode = false;

    [Tooltip("Seconds after a burst-only burst finishes before the zone trigger re-arms. Only used when Burst Only Mode is enabled.")]
    [SerializeField] private float burstCooldownDuration = 180f;

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

    [Header("Legacy Mutants")]
    [Tooltip("When enabled, this spawner is eligible to spawn previously-escaped residents — " +
             "suspects who were beaten as full mutants and fled rather than died (tracked via " +
             "SuspectRunRecords.isLegacyMutant) — in their full-mutant SuspectCharacter form instead " +
             "of a random entry from mutantPrefabs. Requires a SuspectRunRecords instance in the scene.")]
    [SerializeField] private bool spawnLegacyMutants = false;

    [Tooltip("Probability (0-1) that an individual burst spawn rolls for a legacy mutant instead of " +
             "a random mutantPrefabs entry. Ignored — falls back to mutantPrefabs — when no legacy " +
             "mutant is currently eligible.")]
    [Range(0f, 1f)]
    [SerializeField] private float legacyMutantChance = 0.3f;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _activeEnemies = new List<NetworkObject>();
    private bool _isRunning;
    // Burst-only mode cooldown state (server-only).
    private bool _isOnBurstCooldown;
    private Coroutine _burstCooldownCoroutine;
    private bool _registeredInBudget;

    // ── Shared Daily Budget ────────────────────────────────────────────────────

    /// <summary>
    /// Maximum ambient enemies spawned per campaign day across every MutantSpawner on the map.
    /// Scripted packs (<see cref="SpawnPackAt"/>), breaches, and the debug aggro cheat are not counted.
    /// </summary>
    public const int MaxAmbientSpawnsPerDay = 5;

    // Map-wide ambient spawn budget (server-only), shared by all spawners and reset when the day changes.
    private static int s_spawnsToday;
    private static int s_spawnBudgetDay = int.MinValue;
    private static int s_activeSpawnerCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_spawnsToday = 0;
        s_spawnBudgetDay = int.MinValue;
        s_activeSpawnerCount = 0;
    }

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

        // First spawner of a new session resets the shared map-wide budget.
        if (s_activeSpawnerCount == 0)
        {
            s_spawnsToday = 0;
            s_spawnBudgetDay = int.MinValue;
        }
        s_activeSpawnerCount++;
        _registeredInBudget = true;

        // Ambient spawning runs any time of day — start immediately (subject to the day
        // threshold and zone-activation gating) rather than waiting for a night phase.
        int startingDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
        if (startingDay >= firstActiveDay && !requiresZoneActivation)
        {
            BeginSpawning();
            Debug.Log($"[MutantSpawner] Spawning started on network spawn (Day {startingDay}).");
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        CampaignManager.OnDayChanged -= OnDayChanged;
        _isRunning = false;
        CancelBurstCooldown();

        if (_registeredInBudget)
        {
            _registeredInBudget = false;
            s_activeSpawnerCount = Mathf.Max(0, s_activeSpawnerCount - 1);
        }
    }

    /// <summary>
    /// Starts or stops spawning based on whether the new day meets <see cref="firstActiveDay"/>.
    /// Ambient spawning is otherwise continuous — it is not gated by shift/day-night state.
    /// SERVER ONLY (subscribed only on the server in <see cref="OnNetworkSpawn"/>).
    /// </summary>
    private void OnDayChanged(int newDay)
    {
        RefreshDailySpawnBudget(newDay);

        if (newDay < firstActiveDay)
        {
            if (_isRunning)
                StopSpawning();
            return;
        }

        if (!_isRunning && !requiresZoneActivation)
        {
            BeginSpawning();
            Debug.Log($"[MutantSpawner] Spawning started — day threshold reached (Day {newDay}).");
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

    // ── Daily Spawn Budget ─────────────────────────────────────────────────────

    private int CurrentCampaignDay => CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;

    /// <summary>
    /// Resets the shared map-wide ambient spawn counter when <paramref name="day"/> differs from
    /// the day the counter was last tracked for.
    /// </summary>
    private static void RefreshDailySpawnBudget(int day)
    {
        if (day == s_spawnBudgetDay)
            return;

        s_spawnBudgetDay = day;
        s_spawnsToday = 0;
    }

    /// <summary>
    /// True while the map-wide budget still allows ambient spawns today.
    /// </summary>
    private bool HasDailySpawnBudget()
    {
        if (exemptFromDailyCap)
            return true;

        RefreshDailySpawnBudget(CurrentCampaignDay);
        return s_spawnsToday < MaxAmbientSpawnsPerDay;
    }

    // ── Spawn Loop ─────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(initialDelay);

        while (_isRunning)
        {
            if (!HasDailySpawnBudget())
            {
                Debug.Log($"[MutantSpawner] {name}: map-wide daily cap of {MaxAmbientSpawnsPerDay} reached on Day {s_spawnBudgetDay} — pausing until next day.", this);
                yield return new WaitUntil(() => !_isRunning || HasDailySpawnBudget());
                if (!_isRunning)
                    yield break;
                yield return new WaitForSeconds(initialDelay);
                continue;
            }

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
            if (!_isRunning || !HasDailySpawnBudget())
                yield break;

            PruneDeadEnemies();

            if (_activeEnemies.Count < effectiveCap && SpawnSingleEnemy() && !exemptFromDailyCap)
                s_spawnsToday++;

            if (i < count - 1)
                yield return new WaitForSeconds(burstSpawnDelay);
        }
    }

    /// <summary>
    /// Spawns one mutant. Ambient spawns (the normal burst loop) never start aggroed —
    /// only <see cref="MutantBreachManager"/>-spawned mutants, or an explicit
    /// <paramref name="forceAggro"/> override (used solely by the debug console's
    /// "Aggroed Mutant" cheat via <see cref="ForceSpawnAggroed"/>), can start hostile.
    /// </summary>
    /// <returns>True when an enemy was actually spawned.</returns>
    private bool SpawnSingleEnemy(bool forceAggro = false)
    {
        Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Roll for a legacy mutant — a resident who previously escaped a full-mutant encounter —
        // before falling back to the normal random mutantPrefabs pool.
        SuspectRecord legacyRecord = null;
        if (spawnLegacyMutants && SuspectRunRecords.Instance != null && Random.value < legacyMutantChance)
            legacyRecord = SuspectRunRecords.Instance.GetRandomLegacyMutantRecord();

        GameObject prefab = legacyRecord != null
            ? legacyRecord.SuspectData.CharacterPrefab.gameObject
            : mutantPrefabs[Random.Range(0, mutantPrefabs.Length)];

        if (!TryFindSpawnPoint(transform.position, prefab, useLocalBox: true, out Vector3 spawnPosition))
        {
            Debug.LogWarning($"[MutantSpawner] {name}: no valid NavMesh spawn point found in " +
                             $"{spawnPlacementAttempts} attempts — skipping this spawn.", this);
            return false;
        }

        GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[MutantSpawner] A prefab in mutantPrefabs is missing a NetworkObject component.", this);
            Destroy(instance);
            return false;
        }

        SuspectCharacter legacyCharacter = legacyRecord != null ? instance.GetComponent<SuspectCharacter>() : null;

        if (legacyCharacter == null)
        {
            // Ambient path — only assign an aggro target (and thus allow InitialiseServer's
            // aggroChance roll / forced aggro to matter) when explicitly forced. Otherwise the
            // mutant spawns with no aggro target at all, guaranteeing it starts non-aggroed.
            MutantEnemy enemy = instance.GetComponent<MutantEnemy>();
            if (forceAggro)
            {
                if (aggroTarget != null)
                    enemy?.SetAggroTarget(aggroTarget);
                enemy?.SetForceAggro(true);
            }
        }

        netObj.Spawn(true);
        _activeEnemies.Add(netObj);

        if (legacyCharacter != null)
        {
            // Legacy path — skip the booth cutscene/window-breach entirely and drop straight
            // into active MutantEnemy behaviour. Only force hostility when explicitly requested;
            // ambient reintroductions of a legacy mutant now start non-aggroed like any other
            // ambient spawn.
            legacyCharacter.ActivateAsLegacyMutant(forceAggro ? aggroTarget : null);
            Debug.Log($"[MutantSpawner] Spawned legacy mutant '{legacyRecord.SuspectData.name}'.", this);
        }

        return true;
    }

    /// <summary>
    /// Finds a spawn position that is really on the NavMesh surface and connected to the main mesh.
    /// Each attempt picks a random XZ point in the spawn box, projects it down onto the ground from
    /// <see cref="groundProbeHeight"/> (spawners sit at y≈0 with a flat box, so without this, mutants
    /// on hilly ground were placed inside the hill or floating above it), snaps it to the NavMesh for
    /// the prefab's agent type, and requires a complete path to the reachability anchor.
    /// </summary>
    /// <param name="center">World-space centre of the spawn box.</param>
    /// <param name="useLocalBox">True: the box follows this spawner's rotation/scale. False: world-axis box around <paramref name="center"/>.</param>
    private bool TryFindSpawnPoint(Vector3 center, GameObject prefab, bool useLocalBox, out Vector3 result)
    {
        var prefabAgent = prefab != null ? prefab.GetComponent<NavMeshAgent>() : null;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent != null ? prefabAgent.agentTypeID : 0,
            areaMask    = prefabAgent != null ? prefabAgent.areaMask : NavMesh.AllAreas
        };

        Vector3 anchorSource = navReachabilityAnchor != null ? navReachabilityAnchor.position : center;
        bool hasAnchor = TrySnapToNavMesh(anchorSource, filter, out Vector3 anchor);
        var path = new NavMeshPath();

        for (int attempt = 0; attempt < spawnPlacementAttempts; attempt++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-spawnBoxHalfExtents.x, spawnBoxHalfExtents.x),
                0f,
                Random.Range(-spawnBoxHalfExtents.z, spawnBoxHalfExtents.z));

            Vector3 candidate = useLocalBox ? transform.TransformPoint(offset) : center + offset;

            if (!TrySnapToNavMesh(candidate, filter, out Vector3 onMesh))
                continue;

            if (hasAnchor &&
                (!NavMesh.CalculatePath(onMesh, anchor, filter, path) || path.status != NavMeshPathStatus.PathComplete))
                continue;

            result = onMesh;
            return true;
        }

        result = center;
        return false;
    }

    /// <summary>
    /// Projects <paramref name="point"/> straight down onto the highest ground below
    /// <see cref="groundProbeHeight"/>, then snaps to the nearest NavMesh point within a short
    /// radius. Falls back to a wider vertical-only NavMesh sample when nothing is hit.
    /// </summary>
    private bool TrySnapToNavMesh(Vector3 point, NavMeshQueryFilter filter, out Vector3 result)
    {
        const float snapRadius = 2f;

        Vector3 probeOrigin = new Vector3(point.x, transform.position.y + groundProbeHeight, point.z);
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit ground, groundProbeHeight * 2f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (NavMesh.SamplePosition(ground.point, out NavMeshHit hit, snapRadius, filter))
            {
                result = hit.position;
                return true;
            }

            // Hit something that isn't walkable (tree canopy, rock, roof) — reject this XZ.
            result = point;
            return false;
        }

        if (NavMesh.SamplePosition(point, out NavMeshHit fallback, snapRadius, filter))
        {
            result = fallback.position;
            return true;
        }

        result = point;
        return false;
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
    /// Called by <see cref="MutantSpawnerZoneTrigger"/> when a player first enters the
    /// linked zone. If the spawner is in zone-activation mode and the day threshold is met,
    /// spawning begins immediately (ambient spawning is not gated by day/night — it runs any
    /// time of day once active).
    /// When <see cref="burstOnlyMode"/> is also enabled, fires a single burst then enters
    /// a <see cref="burstCooldownDuration"/> cooldown before the zone can trigger again.
    /// SERVER ONLY — no-op on clients.
    /// </summary>
    public void ActivateFromZone()
    {
        if (!IsServer) return;

        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
        if (currentDay < firstActiveDay)
        {
            Debug.Log($"[MutantSpawner] Zone entered but day {currentDay} < firstActiveDay {firstActiveDay} — not starting.", this);
            return;
        }

        // ── Burst-only mode ────────────────────────────────────────────────────
        if (burstOnlyMode)
        {
            if (_isOnBurstCooldown)
            {
                Debug.Log("[MutantSpawner] Zone entered but burst cooldown active — skipping.", this);
                return;
            }

            _burstCooldownCoroutine = StartCoroutine(BurstOnceAndCooldown());
            Debug.Log("[MutantSpawner] Zone entered — burst-only mode, firing single burst.", this);
            return;
        }

        // ── Continuous loop mode ───────────────────────────────────────────────
        if (_isRunning) return;

        BeginSpawning();
        Debug.Log("[MutantSpawner] Zone entered — spawning started.", this);
    }

    /// <summary>
    /// Fires a single burst then waits <see cref="burstCooldownDuration"/> seconds before
    /// re-arming the zone trigger. SERVER ONLY (started only from <see cref="ActivateFromZone"/>).
    /// </summary>
    private IEnumerator BurstOnceAndCooldown()
    {
        _isOnBurstCooldown = true;
        _isRunning = true;
        yield return StartCoroutine(SpawnBurst());
        _isRunning = false;

        Debug.Log($"[MutantSpawner] Burst complete — cooldown for {burstCooldownDuration}s.", this);
        yield return new WaitForSeconds(burstCooldownDuration);

        _isOnBurstCooldown = false;
        _burstCooldownCoroutine = null;
        Debug.Log("[MutantSpawner] Burst cooldown complete — zone re-armed.", this);
    }

    /// <summary>
    /// Stops any in-progress burst cooldown and resets related state. SERVER ONLY.
    /// </summary>
    private void CancelBurstCooldown()
    {
        if (_burstCooldownCoroutine != null)
        {
            StopCoroutine(_burstCooldownCoroutine);
            _burstCooldownCoroutine = null;
        }
        _isOnBurstCooldown = false;
    }

    /// <summary>
    /// Called by <see cref="MutantSpawnerZoneTrigger"/> when all players have left the
    /// linked zone (only when <see cref="MutantSpawnerZoneTrigger._deactivateWhenAllPlayersLeave"/>
    /// is enabled). Stops the spawn loop; existing enemies remain active.
    /// SERVER ONLY — no-op on clients.
    /// </summary>
    public void DeactivateFromZone()
    {
        if (!IsServer) return;
        if (!_isRunning) return;

        StopSpawning();
        // Re-arm the zone gate so re-entry starts spawning again.
        requiresZoneActivation = true;
        Debug.Log("[MutantSpawner] All players left zone — spawning paused.", this);
    }

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
    /// Debug-only entry point (used by the F-key cheat console) that spawns a single enemy
    /// guaranteed to be in aggro mode, heading straight for the assigned <see cref="aggroTarget"/>
    /// regardless of <see cref="MutantEnemyData.aggroChance"/>. This is the only way an ambient
    /// <see cref="MutantSpawner"/> mutant can start aggroed — normal bursts (<see cref="SpawnBurst"/>)
    /// never aggro on spawn; only a <see cref="MutantBreachManager"/> breach does that organically.
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
    /// Spawns a pack of <paramref name="count"/> mutants in a burst centred on
    /// <paramref name="center"/> rather than this spawner's own position.
    /// Enemies are scattered using the same <see cref="spawnBoxHalfExtents"/> box and
    /// added to the active-enemy list. Optionally aggros all of them toward
    /// <paramref name="packAggroTarget"/>. Each spawned mutant is held in place (see
    /// <see cref="MutantEnemy.SetHeld"/>) from the moment it exists, so it never patrols/chases
    /// away before the caller decides to release it — pass <paramref name="onSpawned"/> to receive
    /// the full list and release them (e.g. once a player approaches). SERVER ONLY.
    /// </summary>
    public void SpawnPackAt(Vector3 center, int count, Transform packAggroTarget = null,
        System.Action<List<MutantEnemy>> onSpawned = null)
    {
        if (!IsServer) return;
        StartCoroutine(SpawnPackAtCoroutine(center, count, packAggroTarget, onSpawned));
    }

    private IEnumerator SpawnPackAtCoroutine(Vector3 center, int count, Transform packAggroTarget,
        System.Action<List<MutantEnemy>> onSpawned)
    {
        var spawned = new List<MutantEnemy>(count);

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = mutantPrefabs[Random.Range(0, mutantPrefabs.Length)];

            // Packs are placed at a scripted destination (e.g. deep in the woods) rather than this
            // spawner's own box. An agent placed off the NavMesh (or on a disconnected hill island)
            // never links / never finds a path, and just stands frozen.
            if (!TryFindSpawnPoint(center, prefab, useLocalBox: false, out Vector3 spawnPos))
            {
                Debug.LogWarning($"[MutantSpawner] {name}: no valid NavMesh spawn point near pack centre {center} — skipping one pack member.", this);
                continue;
            }
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject instance = Instantiate(prefab, spawnPos, spawnRot);
            NetworkObject netObj = instance.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError("[MutantSpawner] Pack prefab is missing a NetworkObject — skipping.", this);
                Destroy(instance);
                continue;
            }

            MutantEnemy enemy = instance.GetComponent<MutantEnemy>();
            if (packAggroTarget != null)
                enemy?.SetAggroTarget(packAggroTarget);
            enemy?.SetForceAggro(packAggroTarget != null);
            enemy?.SetHeld(true);

            netObj.Spawn(true);
            _activeEnemies.Add(netObj);
            if (enemy != null) spawned.Add(enemy);

            if (i < count - 1)
                yield return new WaitForSeconds(burstSpawnDelay);
        }

        onSpawned?.Invoke(spawned);

        Debug.Log($"[MutantSpawner] Pack of {count} spawned at {center}.", this);
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
