using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Rolls and schedules the rare <see cref="GiorgiPhantomEncounter"/> jumpscare. Server-only logic;
/// a plain scene MonoBehaviour (lives under <c>/---Suspect Controller</c>).
///
/// Each shift start (from <see cref="_minDay"/> on) the server rolls <see cref="_chancePerShift"/>.
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

    [Header("Rarity")]
    [Tooltip("Chance (0-1) the phantom visits during any given shift.")]
    [Range(0f, 1f)]
    [SerializeField] private float _chancePerShift = 0.12f;

    [Tooltip("First campaign day the phantom can appear on (keeps him out of the Day 1 tutorial).")]
    [SerializeField] private int _minDay = 2;

    [Tooltip("If true, he can appear at most once per play session.")]
    [SerializeField] private bool _oncePerSession = true;

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

    // ── Rolling ────────────────────────────────────────────────────────────

    private void HandleShiftStart()
    {
        if (!IsServer) return;

        // Drop a stale arm from a previous shift that never got consumed.
        ClearArmedIntercept();
        _targetSlot = -1;

        if (_phantomPrefab == null) return;
        if (_oncePerSession && _visitedThisSession) return;
        if (CurrentDay < _minDay) return;

        float roll = Random.value;
        if (roll >= _chancePerShift)
        {
            if (_log) Debug.Log($"[GiorgiPhantom] Shift roll {roll:F2} >= {_chancePerShift:F2} — no visit this shift.");
            return;
        }

        // Pick the slot lazily (the lineup is populated on this same OnShiftStart).
        StartCoroutine(PickTargetSlotWhenLineupReady());
    }

    private IEnumerator PickTargetSlotWhenLineupReady()
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
        _targetSlot = Random.Range(min, total);

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
        _visitedThisSession = true;
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
