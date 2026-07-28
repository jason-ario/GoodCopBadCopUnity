using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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

    [Header("Scene References")]
    [Tooltip("Fixed world locations mutants can spawn at for a breach. At least one is required.")]
    [SerializeField] private Transform[] breachPoints;

    [Tooltip("Optional target breached mutants head toward when a breach's forceAggro is enabled. " +
             "Leave null to fall back to each mutant's normal nearest-player targeting.")]
    [SerializeField] private Transform aggroTarget;

    [Tooltip("Controls the red pulsing alarm lights on all clients.")]
    [SerializeField] private AlarmLightController alarmLights;

    [Tooltip("Looping alarm siren AudioSource. Assign a clip (e.g. Alarm v1) and leave it stopped by default.")]
    [SerializeField] private AudioSource alarmAudioSource;

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

        CancelSchedule();
        _breachCoroutine = StartCoroutine(RunBreach(data));
        Debug.Log($"[MutantBreachManager] DEBUG — force-triggered breach '{data.breachName}'.");
    }

    // ── Breach Flow ──────────────────────────────────────────────────────────

    private IEnumerator RunBreach(MutantBreachData data)
    {
        if (data.mutantPrefabs == null || data.mutantPrefabs.Length == 0)
        {
            Debug.LogError("[MutantBreachManager] Chosen MutantBreachData has no mutantPrefabs — aborting breach.", this);
            yield break;
        }

        if (breachPoints == null || breachPoints.Length == 0)
        {
            Debug.LogError("[MutantBreachManager] No breachPoints assigned — aborting breach.", this);
            yield break;
        }

        _isBreachActive = true;
        _hasTriggeredToday = true;

        Debug.Log($"[MutantBreachManager] Breach triggered: '{data.breachName}' — {data.mutantCount} mutant(s).");
        TriggerBreachEffectsClientRpc(data.notificationMessage, data.notificationHoldDuration);

        yield return new WaitForSeconds(data.alarmLeadTimeSeconds);

        yield return StartCoroutine(SpawnBreachMutants(data));

        yield return new WaitUntil(AllBreachMutantsDefeated);

        Debug.Log("[MutantBreachManager] Breach cleared — ending alarm.");
        EndBreachEffectsClientRpc();

        _isBreachActive = false;
        _breachCoroutine = null;
        _activeBreachMutants.Clear();
    }

    private IEnumerator SpawnBreachMutants(MutantBreachData data)
    {
        for (int i = 0; i < data.mutantCount; i++)
        {
            Transform point = breachPoints[Random.Range(0, breachPoints.Length)];
            GameObject prefab = data.mutantPrefabs[Random.Range(0, data.mutantPrefabs.Length)];

            GameObject instance = Instantiate(prefab, point.position, point.rotation);
            NetworkObject netObj = instance.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError("[MutantBreachManager] A prefab in MutantBreachData.mutantPrefabs is missing a NetworkObject component.", this);
                Destroy(instance);
                continue;
            }

            MutantEnemy enemy = instance.GetComponent<MutantEnemy>();
            if (aggroTarget != null)
                enemy?.SetAggroTarget(aggroTarget);
            if (data.forceAggro)
                enemy?.SetForceAggro(true);

            netObj.Spawn(true);
            _activeBreachMutants.Add(netObj);

            if (i < data.mutantCount - 1)
                yield return new WaitForSeconds(data.spawnStaggerSeconds);
        }
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

    // ── Client FX ────────────────────────────────────────────────────────────

    [ClientRpc]
    private void TriggerBreachEffectsClientRpc(string message, float holdDuration)
    {
        if (alarmLights != null)
            alarmLights.StartAlarm();

        if (alarmAudioSource != null)
        {
            alarmAudioSource.loop = true;
            alarmAudioSource.Play();
        }

        if (PlayerTutorialUI.Instance != null)
            PlayerTutorialUI.Instance.Show(message, holdDuration);
        else
            Debug.LogWarning("[MutantBreachManager] PlayerTutorialUI.Instance is null — could not show breach notification.");
    }

    [ClientRpc]
    private void EndBreachEffectsClientRpc()
    {
        if (alarmLights != null)
            alarmLights.StopAlarm();

        if (alarmAudioSource != null)
            alarmAudioSource.Stop();
    }
}
