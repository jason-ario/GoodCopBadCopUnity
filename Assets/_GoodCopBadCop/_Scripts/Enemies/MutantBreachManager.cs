using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Server-authoritative orchestrator for "mutant breach" events — a scripted alarm followed by
/// a fixed-size wave of mutants spawning at fixed breach points, which the players must clear.
///
/// Flow per breach:
///  1. Alarm sound + red alarm lights start, and a PlayerTutorialUI black-bar notification (same style as
///     the "Go to the booth" prompt) is shown on all clients.
///  2. After <see cref="MutantBreachData.alarmLeadTimeSeconds"/>, the breach's fixed mutant count
///     spawns (staggered) at random <see cref="breachPoints"/>.
///  3. The alarm keeps running until every spawned mutant is dead, then stops and lights return to normal.
///
/// Scheduling: once per day, starting Day 2 at the earliest, only on days whose active
/// <see cref="DayBase.HasMutantBreach"/> flag is set and that have at least one entry in
/// <see cref="DayBase.PossibleBreaches"/> — one preset is picked at random. The breach rolls at
/// the end of the day, after every suspect has been processed AND every post-shift task has been
/// completed (<see cref="ShiftManager.OnPostShiftTasksComplete"/>), firing a random
/// [<see cref="minDelayAfterShiftStart"/>, <see cref="maxDelayAfterShiftStart"/>] seconds later.
/// </summary>
public class MutantBreachManager : NetworkBehaviour
{
    public static MutantBreachManager Instance;

    /// <summary>
    /// Fired on ALL clients (including host) the instant a breach's alarm/notification effects
    /// start — before the alarm lead time elapses and mutants spawn. Day scripts (e.g. Day_01's
    /// first-breach tutorial) subscribe to this to layer their own reactions (megaphone barks,
    /// tutorial overlays, unlocking weapons) on top of the generic breach flow without this
    /// manager needing to know about any day-specific tutorial content.
    /// </summary>
    public static event Action OnBreachStartedAllClients;

    /// <summary>
    /// Fired on ALL clients whenever the number of still-active breach mutants changes (including
    /// the initial report right after spawning finishes, and the final 0-remaining report).
    /// A mutant counts as resolved the instant it dies OR begins fleeing — see
    /// <see cref="AllBreachMutantsDefeated"/>/<see cref="MutantEnemy.IsDead"/>.
    /// </summary>
    public static event Action<int, int> OnBreachCountChangedAllClients;

    /// <summary>
    /// Fired on ALL clients once every breach mutant has been resolved and the alarm/music has
    /// been stopped.
    /// </summary>
    public static event Action OnBreachClearedAllClients;

    [Header("Scene References")]
    [Tooltip("Fixed world locations mutants can spawn at for a breach. At least one is required.")]
    [SerializeField] private Transform[] breachPoints;

    [Tooltip("Fallback target breached mutants head toward ONLY when no living, non-cutscened " +
             "player exists to charge (every breach mutant is always in breach charge mode, " +
             "which takes priority — see MutantEnemy.SetBreachChargeMode). Leave null to just " +
             "patrol/idle in that edge case instead.")]
    [SerializeField] private Transform aggroTarget;

    [Tooltip("Controls the red pulsing alarm lights on all clients.")]
    [SerializeField] private AlarmLightController alarmLights;

    [Tooltip("Controls the full-screen red post-processing flash on all clients, synced to alarmLights' pulse.")]
    [SerializeField] private PostProcessAlarmController alertVolume;

    [Tooltip("Looping alarm siren AudioSource. Assign a clip (e.g. Alarm v1) and leave it stopped by default.")]
    [SerializeField] private AudioSource alarmAudioSource;

    [Header("Breach Music")]
    [Tooltip("Looping music track played through MusicManager while a breach is active. Leave null to skip breach music entirely.")]
    [SerializeField] private AudioClip breachMusic;

    [Tooltip("Seconds to fade the breach music in over when the breach starts. Pass -1 to use MusicManager's default.")]
    [SerializeField] private float breachMusicFadeInDuration = -1f;

    [Tooltip("Seconds to fade the breach music out over when the breach ends. Pass -1 to use MusicManager's default.")]
    [SerializeField] private float breachMusicFadeOutDuration = -1f;

    [Header("Scheduling")]
    [Tooltip("Minimum seconds after the day's tasks are all complete before a breach can trigger.")]
    [SerializeField] private float minDelayAfterShiftStart = 10f;

    [Tooltip("Maximum seconds after the day's tasks are all complete before a breach can trigger.")]
    [SerializeField] private float maxDelayAfterShiftStart = 45f;

    [Tooltip("Hard floor — no breach will ever trigger before this campaign day, even if a day's " +
             "HasMutantBreach flag is mistakenly set earlier.")]
    [SerializeField] private int firstActiveDay = 2;

    [Header("Debug")]
    [Tooltip("Breach preset used by DebugForceTriggerBreach (cheat console 'Trigger Mutant Breach' button). " +
             "Not used during normal day-gated scheduling.")]
    [SerializeField] private MutantBreachData debugTestBreachData;

    // ── Server-only state ────────────────────────────────────────────────────

    private bool _hasTriggeredToday;
    private bool _isBreachActive;
    private Coroutine _scheduleCoroutine;
    private Coroutine _breachCoroutine;
    private readonly List<NetworkObject> _activeBreachMutants = new List<NetworkObject>();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        CampaignManager.OnDayChanged += OnDayChanged;
        ShiftManager.OnPostShiftTasksComplete += OnPostShiftTasksComplete;

        if (breachPoints == null || breachPoints.Length == 0)
            Debug.LogWarning("[MutantBreachManager] No breachPoints assigned — breaches will be unable to spawn mutants.", this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        CampaignManager.OnDayChanged -= OnDayChanged;
        ShiftManager.OnPostShiftTasksComplete -= OnPostShiftTasksComplete;

        CancelSchedule();
    }

    /// <summary>
    /// True whenever today's mutant breach (if any) is still scheduled, actively running, or
    /// about to be scheduled for the active day — i.e. whenever it's too soon for the player to
    /// clock out. Checked by <see cref="ShiftManager.TryEnableClockOut"/>: it withholds the
    /// timecard machine's clock-out while this is true, then re-checks once
    /// <see cref="RunBreach"/> finishes via <see cref="ShiftManager.RecheckClockOutGate"/>.
    /// Mirrors the exact same gating conditions as <see cref="TryScheduleBreachForToday"/>, so a
    /// day that will never actually roll a breach (wrong day, flag off, no presets) never blocks
    /// clock-out.
    /// </summary>
    public bool IsBreachPendingOrActiveForToday
    {
        get
        {
            if (_isBreachActive || _scheduleCoroutine != null || _breachCoroutine != null)
                return true;

            // Already resolved for today (or never will be) — nothing left to withhold clock-out for.
            if (_hasTriggeredToday)
                return false;

            int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
            if (currentDay < firstActiveDay)
                return false;

            DayBase activeDay = CampaignManager.Instance != null ? CampaignManager.Instance.ActiveDay : null;
            if (activeDay == null || !activeDay.HasMutantBreach)
                return false;

            return activeDay.PossibleBreaches != null && activeDay.PossibleBreaches.Length > 0;
        }
    }

    // ── Scheduling ─────────────────────────────────────────────────────────────

    private void OnDayChanged(int newDay)
    {
        _hasTriggeredToday = false;

        // Never carry a pending schedule over into the new day, regardless of why it changed.
        CancelSchedule();
    }

    private void OnPostShiftTasksComplete()
    {
        TryScheduleBreachForToday();
    }

    /// <summary>
    /// Rolls whether today's active day should schedule a breach, and if so, starts the
    /// random-delay countdown. Called once the day's full task schedule is complete (see
    /// <see cref="ShiftManager.OnPostShiftTasksComplete"/>). Safe to call multiple times — no-ops
    /// once already scheduled or triggered for the day. SERVER ONLY.
    /// </summary>
    private void TryScheduleBreachForToday()
    {
        if (!IsServer || _hasTriggeredToday || _isBreachActive || _scheduleCoroutine != null)
            return;

        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
        if (currentDay < firstActiveDay)
            return;

        DayBase activeDay = CampaignManager.Instance != null ? CampaignManager.Instance.ActiveDay : null;
        if (activeDay == null || !activeDay.HasMutantBreach)
            return;

        if (activeDay.PossibleBreaches == null || activeDay.PossibleBreaches.Length == 0)
        {
            Debug.LogWarning($"[MutantBreachManager] Day {currentDay} has HasMutantBreach enabled but no PossibleBreaches assigned.", this);
            return;
        }

        _scheduleCoroutine = StartCoroutine(ScheduleBreachCoroutine(activeDay.PossibleBreaches));
    }

    private IEnumerator ScheduleBreachCoroutine(MutantBreachData[] pool)
    {
        float delay = Random.Range(minDelayAfterShiftStart, maxDelayAfterShiftStart);
        yield return new WaitForSeconds(delay);

        _scheduleCoroutine = null;

        MutantBreachData chosen = pool[Random.Range(0, pool.Length)];
        _breachCoroutine = StartCoroutine(RunBreach(chosen));
    }

    private void CancelSchedule()
    {
        if (_scheduleCoroutine != null)
        {
            StopCoroutine(_scheduleCoroutine);
            _scheduleCoroutine = null;
        }
    }

    // ── Manual Trigger ───────────────────────────────────────────────────────

    /// <summary>
    /// Immediately runs a breach using <paramref name="data"/>, bypassing day gating, the
    /// <see cref="DayBase.HasMutantBreach"/> flag, the once-per-day limit, and the random
    /// scheduling delay entirely. Use for deterministic scripted breaches driven directly by a
    /// day script (e.g. Day_01's first-breach tutorial, triggered right after the trash/graffiti
    /// tasks finish) rather than the generic day-gated auto-scheduler.
    /// Server-only; no-op if a breach is already active or <paramref name="data"/> is null.
    /// </summary>
    public void TriggerBreach(MutantBreachData data)
    {
        if (!IsServer || data == null || _isBreachActive)
            return;

        CancelSchedule();
        _breachCoroutine = StartCoroutine(RunBreach(data));
    }

    // ── Debug ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Debug-only entry point for the F12 cheat console's "Trigger Mutant Breach" button.
    /// Immediately runs a breach using <paramref name="overrideData"/> if provided, otherwise
    /// falls back to <see cref="debugTestBreachData"/>. Bypasses day gating, the
    /// <see cref="DayBase.HasMutantBreach"/> flag, the once-per-day limit, and the random
    /// scheduling delay entirely — use this for manual runtime testing.
    /// Server-only; no-op if a breach is already active or no breach data is available.
    /// </summary>
    public void DebugForceTriggerBreach(MutantBreachData overrideData = null)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[MutantBreachManager] DebugForceTriggerBreach must be called on the server.");
            return;
        }

        if (_isBreachActive)
        {
            Debug.LogWarning("[MutantBreachManager] DebugForceTriggerBreach ignored — a breach is already active.");
            return;
        }

        MutantBreachData data = overrideData != null ? overrideData : debugTestBreachData;
        if (data == null)
        {
            Debug.LogWarning("[MutantBreachManager] DebugForceTriggerBreach ignored — no debugTestBreachData assigned in the Inspector and no override provided.");
            return;
        }

        TriggerBreach(data);
        Debug.Log($"[MutantBreachManager] DEBUG — force-triggered breach '{data.breachName}'.");
    }

    // ── Breach Flow ──────────────────────────────────────────────────────────

    private IEnumerator RunBreach(MutantBreachData data)
    {
        List<GameObject> spawnQueue = BuildSpawnQueue(data);
        if (spawnQueue.Count == 0)
        {
            Debug.LogError("[MutantBreachManager] Chosen MutantBreachData has no mutantPrefabs or uniqueMutantPrefabs — aborting breach.", this);
            yield break;
        }

        if (breachPoints == null || breachPoints.Length == 0)
        {
            Debug.LogError("[MutantBreachManager] No breachPoints assigned — aborting breach.", this);
            yield break;
        }

        _isBreachActive = true;
        _hasTriggeredToday = true;

        Debug.Log($"[MutantBreachManager] Breach triggered: '{data.breachName}' — {spawnQueue.Count} mutant(s).");
        TriggerBreachEffectsClientRpc(data.notificationMessage, data.notificationHoldDuration);

        yield return new WaitForSeconds(data.alarmLeadTimeSeconds);

        yield return StartCoroutine(SpawnBreachMutants(data, spawnQueue));

        int total = spawnQueue.Count;
        int lastRemaining = _activeBreachMutants.Count;
        ReportBreachCountClientRpc(lastRemaining, total);

        while (!AllBreachMutantsDefeated())
        {
            int remaining = _activeBreachMutants.Count;
            if (remaining != lastRemaining)
            {
                lastRemaining = remaining;
                ReportBreachCountClientRpc(remaining, total);
            }
            yield return null;
        }

        if (lastRemaining != 0)
            ReportBreachCountClientRpc(0, total);

        Debug.Log("[MutantBreachManager] Breach cleared — ending alarm.");
        EndBreachEffectsClientRpc();

        if (data.showThanksForPlayingOnClear)
        {
            Debug.Log("[MutantBreachManager] Finale breach cleared — ending the demo.");
            CampaignManager.Instance?.ForceCampaignComplete();
            ShowThanksForPlayingScreenClientRpc();
        }

        _isBreachActive = false;
        _breachCoroutine = null;
        _activeBreachMutants.Clear();

        // Let ShiftManager re-evaluate clock-out now that this breach is fully resolved — it may
        // have been withheld solely because this breach was pending/active (see
        // IsBreachPendingOrActiveForToday / ShiftManager.TryEnableClockOut).
        ShiftManager.Instance?.RecheckClockOutGate();
    }

    /// <summary>
    /// Builds the ordered list of prefabs to spawn for a breach: every entry in
    /// <see cref="MutantBreachData.uniqueMutantPrefabs"/> exactly once, then random picks from
    /// <see cref="MutantBreachData.mutantPrefabs"/> filling the remaining slots up to
    /// <see cref="MutantBreachData.mutantCount"/>. The combined list is shuffled so unique
    /// mutants don't always spawn first/in a predictable stagger slot.
    /// </summary>
    private List<GameObject> BuildSpawnQueue(MutantBreachData data)
    {
        var queue = new List<GameObject>();

        if (data.uniqueMutantPrefabs != null)
        {
            foreach (GameObject prefab in data.uniqueMutantPrefabs)
            {
                if (prefab != null)
                    queue.Add(prefab);
            }
        }

        if (queue.Count > data.mutantCount)
        {
            Debug.LogWarning($"[MutantBreachManager] '{data.breachName}' has more uniqueMutantPrefabs ({queue.Count}) than mutantCount ({data.mutantCount}) — spawning all unique mutants; no random picks will be added.", this);
        }

        bool hasRandomPool = data.mutantPrefabs != null && data.mutantPrefabs.Length > 0;
        int remaining = Mathf.Max(0, data.mutantCount - queue.Count);
        for (int i = 0; i < remaining && hasRandomPool; i++)
        {
            queue.Add(data.mutantPrefabs[Random.Range(0, data.mutantPrefabs.Length)]);
        }

        // Fisher-Yates shuffle so unique mutants are mixed randomly among the random picks.
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }

        return queue;
    }

    private IEnumerator SpawnBreachMutants(MutantBreachData data, List<GameObject> spawnQueue)
    {
        for (int i = 0; i < spawnQueue.Count; i++)
        {
            Transform point = breachPoints[Random.Range(0, breachPoints.Length)];
            GameObject prefab = spawnQueue[i];

            GameObject instance = Instantiate(prefab, point.position, point.rotation);
            NetworkObject netObj = instance.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError("[MutantBreachManager] A prefab in MutantBreachData.mutantPrefabs/uniqueMutantPrefabs is missing a NetworkObject component.", this);
                Destroy(instance);
                continue;
            }

            MutantEnemy enemy = instance.GetComponent<MutantEnemy>();

            // Breach mutants relentlessly charge whichever player is currently nearest —
            // ignoring MutantEnemyData.detectionRadius — and smash through any blocking
            // PerimiterFence along the way, rather than heading for a fixed aggroTarget or
            // waiting to patrol into detection range.
            enemy?.SetBreachChargeMode(true);

            if (aggroTarget != null)
                enemy?.SetAggroTarget(aggroTarget);
            if (data.forceAggro)
                enemy?.SetForceAggro(true);

            if (data.showThanksForPlayingOnFlee && enemy != null)
                enemy.OnFleeStarted += HandleFinaleMutantFleeStarted;

            netObj.Spawn(true);
            _activeBreachMutants.Add(netObj);

            if (i < spawnQueue.Count - 1)
                yield return new WaitForSeconds(data.spawnStaggerSeconds);
        }
    }

    /// <summary>
    /// Broadcasts the current remaining/total breach mutant count to all clients so day
    /// scripts can drive a HUD checklist (e.g. Day_01's "Repel the mutants" tutorial objective).
    /// </summary>
    [ClientRpc]
    private void ReportBreachCountClientRpc(int remaining, int total)
    {
        OnBreachCountChangedAllClients?.Invoke(remaining, total);
    }

    /// <summary>
    /// Prunes despawned/dead entries then reports whether every breach mutant is gone.
    /// Only meaningful after <see cref="SpawnBreachMutants"/> has finished populating the list.
    /// </summary>
    private bool AllBreachMutantsDefeated()
    {
        _activeBreachMutants.RemoveAll(netObj =>
        {
            if (netObj == null || !netObj.IsSpawned)
                return true;

            MutantEnemy enemy = netObj.GetComponent<MutantEnemy>();
            return enemy != null && enemy.IsDead;
        });

        return _activeBreachMutants.Count == 0;
    }

    // ── Campaign Finale ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the server the instant a <see cref="MutantBreachData.showThanksForPlayingOnFlee"/>
    /// breach mutant begins fleeing instead of dying. Ends the demo immediately: marks the
    /// campaign complete and shows the Thanks For Playing screen on every client, without
    /// waiting for the mutant to fully leave the scene or for the normal end-of-shift sequence.
    /// </summary>
    private void HandleFinaleMutantFleeStarted()
    {
        Debug.Log("[MutantBreachManager] Finale breach mutant began fleeing — ending the demo.");
        CampaignManager.Instance?.ForceCampaignComplete();
        ShowThanksForPlayingScreenClientRpc();
    }

    /// <summary>
    /// Delay (seconds) between a mutant-breach-triggered demo end and the Thanks For Playing
    /// panel actually appearing. The player is locked/invincible immediately so the beat lands
    /// cleanly with no risk of a stray hit animation during the wait.
    /// </summary>
    private const float ThanksForPlayingRevealDelaySeconds = 5f;

    [ClientRpc]
    private void ShowThanksForPlayingScreenClientRpc()
    {
        UIController.Instance?.ShowThanksForPlayingScreenAfterDelay(ThanksForPlayingRevealDelaySeconds);
    }

    // ── Client FX ────────────────────────────────────────────────────────────

    [ClientRpc]
    private void TriggerBreachEffectsClientRpc(string message, float holdDuration)
    {
        OnBreachStartedAllClients?.Invoke();

        if (alarmLights != null)
            alarmLights.StartAlarm();

        if (alertVolume != null)
            alertVolume.StartAlarm();

        if (alarmAudioSource != null)
        {
            alarmAudioSource.loop = true;
            alarmAudioSource.Play();
        }

        if (breachMusic != null && MusicManager.Instance != null)
            MusicManager.Instance.Play(breachMusic, true, breachMusicFadeInDuration);

        if (PlayerTutorialUI.Instance != null)
            PlayerTutorialUI.Instance.Show(message, holdDuration);
        else
            Debug.LogWarning("[MutantBreachManager] PlayerTutorialUI.Instance is null — could not show breach notification.");
    }

    [ClientRpc]
    private void EndBreachEffectsClientRpc()
    {
        OnBreachClearedAllClients?.Invoke();

        if (alarmLights != null)
            alarmLights.StopAlarm();

        if (alertVolume != null)
            alertVolume.StopAlarm();

        if (alarmAudioSource != null)
            alarmAudioSource.Stop();

        if (breachMusic != null && MusicManager.Instance != null)
            MusicManager.Instance.FadeOut(breachMusicFadeOutDuration);
    }
}
