using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Rolls and schedules the rare <see cref="GiorgiPhantomEncounter"/> jumpscare. Server-only logic;
/// a plain scene MonoBehaviour (lives under <c>/---Suspect Controller</c>).
///
/// Guaranteed once per playthrough inside the [<see cref="_firstWindowDay"/>, <see cref="_lastWindowDay"/>]
/// window (default Days 3-4): each shift start in the window the server rolls 1 / (days left in window),
/// so the last day is a certainty. "Visited" is stored on the save slot (<see cref="SaveDataManager.HasSeenGiorgiPhantom"/>).
/// On success it picks a lineup slot and, when the switch is about to summon that slot, arms
/// <see cref="SuspectController.InterceptNextSuspectSpawn"/> (only if no other scripted intercept
/// is pending). The phantom is a bonus visitor like Ocho: once he vanishes the consumed slot is
/// refunded (<see cref="SuspectController.RefundInterceptedLineupSlot"/>) and the switch is re-armed
/// (<see cref="ShiftManager.SetNextSuspectReady"/>), so the real suspect he displaced still comes.
/// </summary>
public class GiorgiPhantomVisitController : MonoBehaviour
{
    public static GiorgiPhantomVisitController Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("Network prefab with GiorgiPhantomEncounter (must be in DefaultNetworkPrefabs).")]
    [SerializeField] private GiorgiPhantomEncounter _phantomPrefab;

    [Header("Scheduling")]
    [Tooltip("First campaign day of the guaranteed visit window.")]
    [SerializeField] private int _firstWindowDay = 3;

    [Tooltip("Last campaign day of the guaranteed visit window. If he hasn't come by this day, he comes on it.")]
    [SerializeField] private int _lastWindowDay = 4;

    [Tooltip("Never take the very first slot of the shift (lets the shift settle in first).")]
    [SerializeField] private bool _skipFirstSlot = true;

    [Header("Aftermath")]
    [Tooltip("Seconds after he vanishes before the switch can summon the next suspect.")]
    [SerializeField] private float _nextSuspectDelay = 2f;

    [SerializeField] private bool _log = true;

    private int _targetSlot = -1;
    private bool _visitedThisSession;
    private System.Action _armedIntercept;
    private ShiftManager _subscribedShiftManager;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        ShiftManager.OnNextSuspectReadyForBell += HandleNextSuspectReady;
        StartCoroutine(SubscribeToShiftManagerWhenReady());
    }

    private void OnDisable()
    {
        ShiftManager.OnNextSuspectReadyForBell -= HandleNextSuspectReady;
        if (_subscribedShiftManager != null)
            _subscribedShiftManager.OnShiftStart -= HandleShiftStart;
        _subscribedShiftManager = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private IEnumerator SubscribeToShiftManagerWhenReady()
    {
        while (ShiftManager.Instance == null) yield return null;
        _subscribedShiftManager = ShiftManager.Instance;
        _subscribedShiftManager.OnShiftStart += HandleShiftStart;
    }

    private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private static int CurrentDay => ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 1;

    /// <summary>
    /// True once he has visited this playthrough. Uses the active save slot when there is one
    /// (so a new campaign in the same session gets its own visit); otherwise the session flag.
    /// </summary>
    private bool HasVisited
    {
        get
        {
            SaveDataManager save = SaveDataManager.Instance;
            if (save != null && save.ActiveSlot != null) return save.HasSeenGiorgiPhantom;
            return _visitedThisSession;
        }
    }

    private void MarkVisited()
    {
        _visitedThisSession = true;
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.HasSeenGiorgiPhantom = true;
    }

    // ── Rolling ────────────────────────────────────────────────────────────

    private void HandleShiftStart()
    {
        if (!IsServer) return;

        // Drop a stale arm from a previous shift that never got consumed.
        ClearArmedIntercept();
        _targetSlot = -1;

        if (_phantomPrefab == null) return;
        if (HasVisited) return;

        int day = CurrentDay;
        if (day < _firstWindowDay || day > _lastWindowDay) return;

        // Spread the guarantee evenly over the days left in the window:
        // Day 3 of a 3-4 window = 1/2, Day 4 = 1/1 (always).
        int daysLeftInWindow = _lastWindowDay - day + 1;
        float chance = 1f / daysLeftInWindow;
        float roll = Random.value;
        if (roll >= chance)
        {
            if (_log) Debug.Log($"[GiorgiPhantom] Day {day} roll {roll:F2} >= {chance:F2} — he'll come on a later day of the window.");
            return;
        }

        // Pick the slot lazily (the lineup is populated on this same OnShiftStart).
        StartCoroutine(PickTargetSlotWhenLineupReady(day >= _lastWindowDay));
    }

    private IEnumerator PickTargetSlotWhenLineupReady(bool isFinalChance)
    {
        float timeoutAt = Time.time + 10f;
        DailySuspectManager lineup = DailySuspectManager.Instance;
        while ((lineup == null || !lineup.IsLineupPopulated) && Time.time < timeoutAt)
        {
            yield return null;
            lineup = DailySuspectManager.Instance;
        }

        if (lineup == null || !lineup.IsLineupPopulated || lineup.TotalLineupSlotsThisShift <= 0) yield break;

        int total = lineup.TotalLineupSlotsThisShift;
        int min = _skipFirstSlot && total > 1 ? 1 : 0;
        // On the last day of the window, keep the final slot free so a deferral (another
        // scripted intercept or a breach on his slot) still has a later slot to land on.
        int maxExclusive = isFinalChance && total - 1 > min ? total - 1 : total;
        _targetSlot = Random.Range(min, maxExclusive);

        if (_log) Debug.Log($"[GiorgiPhantom] He'll come this shift, in place of lineup slot {_targetSlot} (of {total}).");

        // The switch may already be armed for that slot.
        if (ShiftManager.NextSuspectReadyForBell) HandleNextSuspectReady();
    }

    /// <summary>Fires (server) whenever the switch becomes ready to summon the next suspect.</summary>
    private void HandleNextSuspectReady()
    {
        if (!IsServer || _targetSlot < 0 || SuspectController.Instance == null) return;

        int nextSlot = SuspectController.Instance.SuspectIndex + 1;
        if (nextSlot < _targetSlot) return;

        if (SuspectController.InterceptNextSuspectSpawn != null)
        {
            // Another scripted encounter owns the next slot — try again on the one after.
            if (_log) Debug.Log("[GiorgiPhantom] Next slot already has a scripted intercept — deferring a slot.");
            return;
        }

        if (MutantBreachManager.Instance != null && MutantBreachManager.Instance.IsBreachRunning) return;

        ArmIntercept();
    }

    // ── Arming / spawning ──────────────────────────────────────────────────

    private void ArmIntercept()
    {
        _targetSlot = -1;
        _armedIntercept = SpawnPhantomForIntercept;
        SuspectController.InterceptNextSuspectSpawn = _armedIntercept;
        if (_log) Debug.Log("[GiorgiPhantom] Armed — he will be the next 'suspect' summoned.");
    }

    private void ClearArmedIntercept()
    {
        if (_armedIntercept != null && SuspectController.InterceptNextSuspectSpawn == _armedIntercept)
            SuspectController.InterceptNextSuspectSpawn = null;
        _armedIntercept = null;
    }

    /// <summary>Invoked by SuspectController.WaitAndSpawnNextSuspect in place of a lineup spawn.</summary>
    private void SpawnPhantomForIntercept()
    {
        _armedIntercept = null;
        SuspectController controller = SuspectController.Instance;

        if (!IsServer || controller == null || _phantomPrefab == null || controller.SpawnPoint == null)
        {
            Debug.LogWarning("[GiorgiPhantom] Can't spawn (missing controller/prefab/spawn point) — giving the slot back.");
            ResumeLineup();
            return;
        }

        Transform spawn = controller.SpawnPoint;
        GiorgiPhantomEncounter phantom = Instantiate(_phantomPrefab, spawn.position, spawn.rotation);
        NetworkObject netObj = phantom.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[GiorgiPhantom] Phantom prefab has no NetworkObject.");
            Destroy(phantom.gameObject);
            ResumeLineup();
            return;
        }

        netObj.Spawn(true);
        MarkVisited();
        phantom.BeginEncounter(controller.StandPoint, controller.BoothInteriorPoint, ResumeLineup);

        if (_log) Debug.Log("[GiorgiPhantom] Spawned and walking to the window.");
    }

    private void ResumeLineup()
    {
        if (!IsServer) return;
        StartCoroutine(ResumeLineupRoutine());
    }

    private IEnumerator ResumeLineupRoutine()
    {
        // Bonus visitor: give back the slot the intercept consumed so the displaced suspect still comes.
        SuspectController.Instance?.RefundInterceptedLineupSlot();

        yield return new WaitForSeconds(_nextSuspectDelay);
        ShiftManager.Instance?.SetNextSuspectReady();
    }

    // ── Debug ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only cheat: makes the phantom the very next suspect in line. Like every other
    /// suspect, he only comes when a player presses the switch.
    /// </summary>
    public void DebugForceNextVisit()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GiorgiPhantom] DebugForceNextVisit is host-only.");
            return;
        }

        if (_phantomPrefab == null)
        {
            Debug.LogWarning("[GiorgiPhantom] No phantom prefab assigned.");
            return;
        }

        ClearArmedIntercept();
        ArmIntercept();
    }
}
