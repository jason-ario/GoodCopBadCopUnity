using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 — gore/body-part yard cleanup + a scripted mutant breach.
///
/// At day start, spawns gore and body-part junk items across the yard's
/// <see cref="TakeOutTrashTask"/> spawn zones (instead of standard trash), triggering the
/// Take Out Trash task so players must bag up every piece of gore. Each gore piece also drops
/// a purely cosmetic blood decal — mop-able for the visual, but not tracked by any cleanup task.
///
/// The day's mail delivery ("Sort the mail" task) is skipped entirely for Day 3 (see
/// <see cref="SortMailTask.SkipDeliveryForDay"/>) — no delivery, no crate, no sorting task —
/// since the mechanic is already established on Day 2 and Day 3 is already carrying the
/// gore/fence cleanup plus its own mutant breach. The daily prohibited-goods roll still
/// runs as normal; only the delivery/crate/task is suppressed.
///
/// Right after the last suspect for the day is processed, <see cref="MutantBreachManager"/>
/// (driven by <see cref="DayBase.HasMutantBreach"/> / <see cref="DayBase.PossibleBreaches"/>)
/// schedules and runs this day's mutant breach automatically. This is a regular (non-finale)
/// breach — Day 4's Ocho breach is the demo's actual final boss/ending, so Day 3's assigned
/// <see cref="MutantBreachData"/> preset must NOT have <see cref="MutantBreachData.showThanksForPlayingOnClear"/>
/// set, otherwise the campaign would end here before the player ever reaches Day 4.
/// </summary>
public class Day_03 : DayBase, IDailyTask
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    public static Day_03 Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeAll();
    }

    // -------------------------------------------------------------------------
    // IDailyTask — registers the post-shift power outage / fuse-box repair as a
    // clock-out blocker the instant the last suspect for the day is processed
    // (Dusk), mirroring Day_02's post-shift Vlad sequence. See
    // OnAllSuspectsProcessed_Day3 for the trigger point.
    // -------------------------------------------------------------------------

    string IDailyTask.DailyTaskId => "Day3PowerOutageFuseBox";

    /// <inheritdoc/>
    public event Action OnDailyTaskCompleted;

    /// <summary>
    /// Starts the post-shift power outage the instant the last suspect is processed: cuts power
    /// immediately (server-only, via <see cref="ElectricityController.PowerOffFuseRequired"/>,
    /// see <see cref="CutPowerServer"/>), then after <see cref="_powerOutageCallDelaySeconds"/>
    /// rings HQ's call about it (via <see cref="Telephone.TriggerScriptedCall"/>), showing the
    /// "Answer the Phone" guidebook task on every client. Answering swaps that for the
    /// "Restore Power" task — see <see cref="OnPowerOutageCallAnsweredAllClients"/> — which
    /// resolves automatically via <see cref="ElectricityController.OnPowerRestoredAllClients"/>
    /// once the player fixes the fuse box. The fuse-box puzzle (not the standard circuit box) is
    /// required to restore power regardless of whether the call has been answered yet.
    /// </summary>
    void IDailyTask.TriggerDailyTask()
    {
        CutPowerServer();
        StartCoroutine(RingPowerOutageCallAfterDelay());
    }

    /// <summary>
    /// Waits <see cref="_powerOutageCallDelaySeconds"/> after the power has already gone out,
    /// then rings HQ's call about it and shows the "Answer the Phone" guidebook task on every
    /// client. Only rings on the server (<see cref="Telephone.TriggerScriptedCall"/> is a
    /// server-only no-op on clients); the "Answer the Phone" task itself is added locally on
    /// every client so it always shows regardless of host/client role.
    /// </summary>
    private System.Collections.IEnumerator RingPowerOutageCallAfterDelay()
    {
        yield return new WaitForSeconds(_powerOutageCallDelaySeconds);

        _answerPhoneThreat = new AnswerPhoneThreat();
        TaskRegistry.Instance?.AddThreat(_answerPhoneThreat);

        Telephone.OnScriptedCallAnsweredAllClients += OnPowerOutageCallAnsweredAllClients;

        if (Telephone.Instance == null)
        {
            Debug.LogWarning("[Day_03] No Telephone.Instance found -- skipping the HQ call and delivering the Restore Power task directly.");
            Telephone.OnScriptedCallAnsweredAllClients -= OnPowerOutageCallAnsweredAllClients;
            OnPowerOutageCallAnsweredAllClients();
            GrantRestorePowerTaskLocal();
            yield break;
        }

        if (!NetworkManager.Singleton.IsServer) yield break;

        Telephone.Instance.TriggerScriptedCall(OnPowerOutageCallAnsweredServer);
    }

    /// <summary>
    /// Fired on every client via <see cref="Telephone.OnScriptedCallAnsweredAllClients"/> the
    /// instant the HQ power-outage call is answered. Only clears the "Answer the Phone" task —
    /// the "Restore Power" task isn't granted until the HQ dialogue itself finishes, see
    /// <see cref="OnPowerOutageDialogueComplete"/>. One-shot; unsubscribes itself immediately.
    /// </summary>
    private void OnPowerOutageCallAnsweredAllClients()
    {
        Telephone.OnScriptedCallAnsweredAllClients -= OnPowerOutageCallAnsweredAllClients;

        if (_answerPhoneThreat != null)
        {
            TaskRegistry.Instance?.RemoveThreat(_answerPhoneThreat);
            _answerPhoneThreat = null;
        }
    }

    /// <summary>
    /// Server-only. Passed as the <c>onAnswered</c> callback to <see cref="Telephone.TriggerScriptedCall"/>,
    /// which only ever invokes it on the server. Waits for the phone-grab animation to finish
    /// (per <see cref="Telephone.TriggerScriptedCall"/>'s own doc comment) before locking the
    /// player into <see cref="_powerOutageCallDialogue"/>.
    /// </summary>
    private void OnPowerOutageCallAnsweredServer()
    {
        StartCoroutine(PlayPowerOutageDialogueAfterGrab());
    }

    private System.Collections.IEnumerator PlayPowerOutageDialogueAfterGrab()
    {
        // Block manual hang-up the instant the call is answered — covers both this grab-animation
        // wait and the dialogue itself. Cleared automatically by HangUpCurrentCaller() once the
        // dialogue completes (see OnPowerOutageDialogueComplete).
        Telephone.Instance?.SetHangUpLocked(true);

        // Let the phone-grab-to-ear animation finish before locking the player into the
        // scripted dialogue — mirrors TriggerScriptedCall's own doc comment guidance.
        yield return new WaitForSeconds(2f);

        if (ScriptedDialogueRunner.Instance == null || _powerOutageCallDialogue == null)
        {
            Debug.LogWarning("[Day_03] Missing ScriptedDialogueRunner.Instance or _powerOutageCallDialogue -- granting the Restore Power task directly.");
            OnPowerOutageDialogueComplete();
            yield break;
        }

        ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(
            _powerOutageCallDialogue,
            onComplete: OnPowerOutageDialogueComplete,
            unlocked: true,
            speakerNameOverride: "HQ",
            // White subtitle text (the name tag falls back to the prefab's default colour).
            // Voice clips and in-ear filtering come from Telephone (useTelephoneAudioSource).
            speakerColorOverride: Color.white,
            useAlternateVoice: false,
            useTelephoneAudioSource: true);
    }

    /// <summary>
    /// Server-only (fires from the same server-only chain as <see cref="PlayPowerOutageDialogueAfterGrab"/>).
    /// Called once the HQ power-outage dialogue finishes (or immediately as a fallback if the
    /// dialogue couldn't be played). Broadcasts the call completion so every client grants the
    /// "Restore Power" task (see <see cref="OnPowerOutageCallCompletedAllClients"/>), then
    /// auto-hangs-up the phone so the player isn't stuck holding the handset.
    /// </summary>
    private void OnPowerOutageDialogueComplete()
    {
        if (Telephone.Instance != null)
        {
            Telephone.Instance.NotifyScriptedCallCompleted();
            Telephone.Instance.HangUpCurrentCaller();
        }
        else
        {
            GrantRestorePowerTaskLocal();
        }

        Debug.Log("[Day_03] HQ power-outage dialogue complete -- Restore Power task broadcast, hanging up.");
    }

    /// <summary>
    /// Fired on every client via <see cref="Telephone.OnScriptedCallCompletedAllClients"/>.
    /// Subscribed for the whole day (not just while ringing) so clients that never saw the ring
    /// locally (e.g. the host-only debug trigger) still receive the task.
    /// </summary>
    private void OnPowerOutageCallCompletedAllClients()
    {
        // Clear any leftover "Answer the Phone" row (e.g. if this client missed the answer event).
        OnPowerOutageCallAnsweredAllClients();
        GrantRestorePowerTaskLocal();
    }

    /// <summary>
    /// Local, every client. Grants the "Restore Power" guidebook task, starts listening for the
    /// fuse box to be fixed, and points the local player at the fuse box. Idempotent.
    /// </summary>
    private void GrantRestorePowerTaskLocal()
    {
        if (_powerOutageThreat != null) return;

        _powerOutageThreat = new RepairPowerThreat();
        TaskRegistry.Instance?.AddThreat(_powerOutageThreat);

        if (ElectricityController.Instance != null)
        {
            ElectricityController.Instance.OnPowerRestoredAllClients -= OnPowerOutageResolved;
            ElectricityController.Instance.OnPowerRestoredAllClients += OnPowerOutageResolved;
        }

        BeginInvestigateFuseBoxStep();
    }

    /// <summary>
    /// Server-only. Cuts power via <see cref="ElectricityController.PowerOffFuseRequired"/> the
    /// instant the last suspect for the day is processed — called directly from
    /// <see cref="IDailyTask.TriggerDailyTask"/>, before the HQ call ever rings.
    /// </summary>
    private void CutPowerServer()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (ElectricityController.Instance == null)
        {
            Debug.LogError("[Day_03] No ElectricityController.Instance found -- cannot start the post-shift power outage.");
            return;
        }

        ElectricityController.Instance.PowerOffFuseRequired();
    }

    // -------------------------------------------------------------------------
    // Day-specific override
    // -------------------------------------------------------------------------

    /// <summary>Day 3 hosts the fuse-box puzzle, so an intentional fuse-required outage
    /// should not be force-cleared by <see cref="DayBase.SupportsFuseBoxRestore"/>.</summary>
    protected override bool SupportsFuseBoxRestore => true;

    // -------------------------------------------------------------------------
    // Inspector — Post-Shift Power Outage (Fuse Box)
    // -------------------------------------------------------------------------

    [Header("Day 3 — Post-Shift Power Outage (Fuse Box)")]
    [Tooltip("When true, the instant the last suspect for Day 3 is processed (Dusk), power goes " +
             "out and the player is directed to the power plant's fuse box to restore it — clock-out " +
             "stays locked until the fuse box is fixed. Uncheck to skip this entirely.")]
    [SerializeField] private bool _enablePostShiftPowerOutage = false;

    [Tooltip("Seconds between the power actually going out and HQ's call ringing about it.")]
    [SerializeField] private float _powerOutageCallDelaySeconds = 5f;

    [Tooltip("Scripted dialogue played over the phone once the HQ power-outage call is answered. " +
             "The player is locked (movement + camera) for its duration, same as a normal scripted " +
             "dialogue, and cannot hang up until it finishes. Assign 'Day03PowerOutageCallDialogue'.")]
    [SerializeField] private ScriptedDialogue _powerOutageCallDialogue;


    [Tooltip("The power station's fuse box. Force-highlighted and pointed at with a pooled " +
             "TutorialMarker arrow (see TutorialMarkerManager) the instant the 'Restore Power' " +
             "task is granted, directing the player to investigate it. Both are cleared the " +
             "first time the box is opened (see FuseBoxPuzzleController.OnBoxInteracted), which " +
             "also shows the 'Fix Fuse' tutorial overlay, highlights the spawned fuses, and advances " +
             "the task text to 'find and add the fuses'. The power switch lever " +
             "highlights itself automatically once every fuse slot is filled — see " +
             "PowerSwitch.OnFuseCountChanged — and gets its own tutorial arrow (see _powerSwitch).")]
    [SerializeField] private FuseBoxPuzzleController _fuseBoxController;

    [Tooltip("Gap (m) between the top of the fuse box's rendered bounds and the tutorial arrow's " +
             "pivot. The arrow height is computed from the box's renderer bounds at runtime.")]
    [SerializeField] private float _fuseBoxMarkerClearance = 0.25f;

    [Tooltip("The power station's power switch lever. Pointed at with a pooled TutorialMarker arrow " +
             "once every fuse slot is filled (the same moment the switch highlights itself), and " +
             "cleared when power is restored or a fuse is pulled back out. Falls back to the first " +
             "PowerSwitch in the scene if unassigned.")]
    [SerializeField] private PowerSwitch _powerSwitch;

    [Tooltip("Gap (m) between the top of the power switch's rendered bounds and the tutorial arrow's pivot.")]
    [SerializeField] private float _powerSwitchMarkerClearance = 0.25f;

    private bool _fuseTutorialShown;

    /// <summary>Runtime "Restore Power" guidebook task, created only while the outage is active.</summary>
    private RepairPowerThreat _powerOutageThreat;

    /// <summary>Runtime "Answer the Phone" guidebook task, created only while HQ's call is ringing.</summary>
    private AnswerPhoneThreat _answerPhoneThreat;

    // -------------------------------------------------------------------------
    // Inspector -- Yard Cleanup Objective Text
    // -------------------------------------------------------------------------
    //
    // NOTE: no hand-scripted objective rows are added by this script anymore.
    // TakeOutTrashTask and FenceRepairTask are both ISystemicThreats, so
    // HUDTaskList already adds/updates/removes their rows in TutorialObjectiveList
    // automatically via the shared TaskRegistry the moment each is triggered below in
    // DayActivated. Hand-scripting the same rows here (previously gated behind the bunker
    // door opening) just duplicated every row.

    [Header("Yard Cleanup Amount")]
    [Tooltip("Multiplier on the number of fence segments Day 3 freshly breaks at the start of the day " +
             "(combined with _fenceBreakScale). 1 = the task's full default roll. Fences already broken " +
             "by an earlier breach still count. Gore uses _goreAmountScale instead.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _cleanupAmountScale = 0.33f;

    [Tooltip("Multiplier on the start-of-day gore/body-part junk (and the blood decal each one drops), " +
             "applied to TakeOutTrashTask's _minGoreSpawnCount/_maxGoreSpawnCount roll. 1 = full roll; " +
             "values above 1 spawn extra. Solo play still halves the result.")]
    [Range(0.05f, 3f)]
    [SerializeField] private float _goreAmountScale = 1f;

    [Tooltip("Extra multiplier applied on top of the cleanup scale for fences only " +
             "(gore is unaffected). 0.5 = half as many fence segments broken.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _fenceBreakScale = 0.5f;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Drop any leftover subscriptions/handles from a previous activation (e.g. a debug
        // skip re-triggering Day 3) before arming everything fresh.
        UnsubscribeAll();

        // These are random/dynamic task sources. The host restores their saved object state after
        // bootstrap, so replaying them during a resume would overwrite the saved task with a new
        // roll before that restoration begins.
        bool restoringWorkday = CampaignManager.Instance != null && CampaignManager.Instance.HasPendingWorkdayRestore;
        if (!restoringWorkday)
        {
            // Blood decals are purely cosmetic (see MutantEnemy/TakeOutTrashTask) and need no
            // arming — only the trash/gore task and the fences require an explicit trigger.
            TakeOutTrashTask.Instance?.TriggerTask(useGorePrefabs: true, amountScale: _goreAmountScale);

            // Randomly breaks a batch of perimeter fence segments so the yard has repair work
            // waiting alongside the gore, mirroring the post-breach fence damage from Day 1
            // (see FenceRepairTask.TriggerTask doc comment). Self-guards to server-only.
            FenceRepairTask.Instance?.TriggerTask(_cleanupAmountScale * _fenceBreakScale);
        }

        // Arms the Mutant Ocho / Vlad-corpse roof cutscene right as the player exits the
        // bunker for Day 3 -- see OchoEatingVladCutscene for the full sequence. TriggerTask()
        // is a one-shot no-op while already armed/running (see its own doc comment), so a
        // stale flag left over from an earlier activation this session (e.g. re-running the
        // "Skip to Day 3" debug cheat, or genuinely revisiting Day 3) would silently prevent
        // it from re-arming. DebugReset() clears that flag first so every DayActivated() call
        // re-arms the cutscene fresh, matching the "drop leftover handles before arming
        // everything fresh" contract already followed by the other Day 3 tasks above.
        OchoEatingVladCutscene.Instance?.DebugReset();
        OchoEatingVladCutscene.Instance?.TriggerTask();

        // Skip the Day 3 mail delivery entirely -- no delivery, no crate, no "Sort the Mail"
        // task. The mechanic is already established on Day 2; Day 3 is already carrying the
        // gore/blood/fence cleanup plus the finale breach. Must be set here, before
        // CampaignManager's OnDayChanged fires. The daily prohibited-goods roll in
        // SortMailTask.OnDayChanged still runs as normal -- only the delivery is suppressed.
        SortMailTask.SkipDeliveryForDay = 3;

        // Post-shift power outage — armed here (rather than at Dusk) so a re-activation (e.g.
        // debug skip) always starts with a clean subscription, matching the pattern above.
        ShiftManager.OnLastSuspectProcessed -= OnAllSuspectsProcessed_Day3;
        ShiftManager.OnLastSuspectProcessed += OnAllSuspectsProcessed_Day3;

        // Every client grants "Restore Power" when the server broadcasts the HQ call's end.
        Telephone.OnScriptedCallCompletedAllClients += OnPowerOutageCallCompletedAllClients;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        UnsubscribeAll();
        StopAllCoroutines();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    /// <summary>
    /// Debug-only: force-starts the post-shift power outage / fuse-box sequence immediately,
    /// bypassing <see cref="_enablePostShiftPowerOutage"/> and the "last suspect processed" gate.
    /// Still rings HQ's call first — this only skips ahead to the point where the phone starts
    /// ringing, not past the answer step. Wired to the F12 cheat console's "Trigger Day 3 Power
    /// Outage" button for testing. Server-only.
    /// </summary>
    public void DebugTriggerPowerOutage()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Day_03] DebugTriggerPowerOutage: server-only -- run this on the host.");
            return;
        }

        // Cancel the normal end-of-shift trigger so it doesn't fire a second, duplicate
        // outage later this same shift if suspects are still being processed.
        ShiftManager.OnLastSuspectProcessed -= OnAllSuspectsProcessed_Day3;

        ShiftManager.Instance?.RegisterPendingDailyTask(this);
        ((IDailyTask)this).TriggerDailyTask();

        Debug.Log("[Day_03] DebugTriggerPowerOutage: power outage / fuse-box task force-started via cheat console.");
    }

    private void UnsubscribeAll()
    {
        ShiftManager.OnLastSuspectProcessed -= OnAllSuspectsProcessed_Day3;
        Telephone.OnScriptedCallAnsweredAllClients -= OnPowerOutageCallAnsweredAllClients;
        Telephone.OnScriptedCallCompletedAllClients -= OnPowerOutageCallCompletedAllClients;

        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients -= OnPowerOutageResolved;

        ClearFuseBoxTutorialState();
    }

    // -------------------------------------------------------------------------
    // Fuse-box tutorial highlight / arrow steps
    // -------------------------------------------------------------------------

    /// <summary>
    /// Step 1: force-highlights the fuse box and points a pooled TutorialMarker arrow at it,
    /// then waits for the box to be opened for the first time (either direction — the door
    /// toggles open/closed on every interaction, so <see cref="OnFuseBoxFirstOpened"/> is a
    /// one-shot that unsubscribes itself immediately).
    /// </summary>
    private void BeginInvestigateFuseBoxStep()
    {
        if (_fuseBoxController == null)
        {
            Debug.LogWarning("[Day_03] _fuseBoxController is not assigned -- skipping the fuse-box tutorial highlight/arrow.");
            return;
        }

        _fuseBoxController.SetForceHighlight(true);
        TutorialMarkerManager.Instance?.Mark(_fuseBoxController.transform,
            GetMarkerHoverHeight(_fuseBoxController.transform, _fuseBoxMarkerClearance));

        _fuseBoxController.OnBoxInteracted -= OnFuseBoxFirstOpened;
        _fuseBoxController.OnBoxInteracted += OnFuseBoxFirstOpened;
    }

    /// <summary>
    /// Hover height (above <paramref name="target"/>'s pivot) that places the arrow clearly above
    /// the top of its rendered bounds. Used for the fuse box (large, flipped mesh whose pivot is not
    /// near its top) and the power switch lever.
    /// </summary>
    private static float GetMarkerHoverHeight(Transform target, float clearance)
    {
        bool found = false;
        float top = target.position.y;

        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
            found = true;
        }

        return Mathf.Max(top - target.position.y, 0f) + clearance;
    }

    /// <summary>Resolves <see cref="_powerSwitch"/>, falling back to the first one in the scene.</summary>
    private PowerSwitch ResolvePowerSwitch()
    {
        if (_powerSwitch == null)
            _powerSwitch = FindFirstObjectByType<PowerSwitch>();
        return _powerSwitch;
    }

    /// <summary>
    /// Step 4: shows the power switch arrow while every fuse slot is filled and hides it again if a
    /// fuse is pulled back out (mirroring <see cref="PowerSwitch.OnFuseCountChanged"/>'s highlight).
    /// Stays subscribed until power is restored (see <see cref="ClearFuseBoxTutorialState"/>).
    /// </summary>
    private void OnFuseCountChangedForSwitchArrow(int filled, int total)
    {
        PowerSwitch powerSwitch = ResolvePowerSwitch();
        if (powerSwitch == null || TutorialMarkerManager.Instance == null) return;

        if (_fuseBoxController != null && _fuseBoxController.IsReady)
            TutorialMarkerManager.Instance.Mark(powerSwitch.transform,
                GetMarkerHoverHeight(powerSwitch.transform, _powerSwitchMarkerClearance));
        else
            TutorialMarkerManager.Instance.Unmark(powerSwitch.transform);
    }

    /// <summary>
    /// Step 2: fired the first time the fuse box door is toggled. Shows the one-shot "Fix Fuse"
    /// tutorial overlay, highlights the spawned fuses (see <see cref="FusePickup.SetFindHighlightActive"/>),
    /// advances the guidebook task to "find and add the fuses", then starts listening for every
    /// slot to be filled. The fuse-box highlight and arrow intentionally stay on during this step
    /// so the player knows where the fuses go; they're cleared once every slot is filled.
    /// </summary>
    private void OnFuseBoxFirstOpened()
    {
        _fuseBoxController.OnBoxInteracted -= OnFuseBoxFirstOpened;

        if (!_fuseTutorialShown)
        {
            _fuseTutorialShown = true;
            TutorialOverlay.Instance?.ShowFixFuseTutorial();
        }

        FusePickup.SetFindHighlightActive(true);

        _powerOutageThreat?.SetStep(RepairPowerThreat.Step.InsertFuses);

        _fuseBoxController.OnFuseCountChanged += OnFuseCountChangedDuringOutage;

        // Catch up immediately in case every slot was somehow already filled before the box
        // was first opened (e.g. inserted by another client while this one was mid-dialogue).
        OnFuseCountChangedDuringOutage(_fuseBoxController.FilledSlotCount, _fuseBoxController.FuseSlotCount);
    }

    /// <summary>
    /// Step 3: fired whenever a fuse is inserted/extracted while the "insert fuses" step is
    /// active. Once every slot is filled, clears the fuse highlights plus the fuse-box highlight
    /// and arrow, and advances the guidebook task to "pull the lever". The power switch highlights itself automatically once
    /// <see cref="FuseBoxPuzzleController.IsReady"/> — see <see cref="PowerSwitch.OnFuseCountChanged"/>
    /// — and this step hands off to <see cref="OnFuseCountChangedForSwitchArrow"/> to point an arrow at it.
    /// </summary>
    private void OnFuseCountChangedDuringOutage(int filled, int total)
    {
        if (_fuseBoxController == null || !_fuseBoxController.IsReady) return;

        _fuseBoxController.OnFuseCountChanged -= OnFuseCountChangedDuringOutage;
        FusePickup.SetFindHighlightActive(false);
        _fuseBoxController.SetForceHighlight(false);
        TutorialMarkerManager.Instance?.Unmark(_fuseBoxController.transform);
        _powerOutageThreat?.SetStep(RepairPowerThreat.Step.PullLever);

        _fuseBoxController.OnFuseCountChanged -= OnFuseCountChangedForSwitchArrow;
        _fuseBoxController.OnFuseCountChanged += OnFuseCountChangedForSwitchArrow;
        OnFuseCountChangedForSwitchArrow(filled, total);
    }

    /// <summary>
    /// Defensive cleanup covering every fuse-box tutorial subscription/highlight/arrow above.
    /// Safe to call at any point in the sequence (or if it never started) — every unsubscribe
    /// and highlight-clear is itself idempotent.
    /// </summary>
    private void ClearFuseBoxTutorialState()
    {
        FusePickup.SetFindHighlightActive(false);

        if (_powerSwitch != null)
            TutorialMarkerManager.Instance?.Unmark(_powerSwitch.transform);

        if (_fuseBoxController == null) return;

        _fuseBoxController.OnBoxInteracted -= OnFuseBoxFirstOpened;
        _fuseBoxController.OnFuseCountChanged -= OnFuseCountChangedDuringOutage;
        _fuseBoxController.OnFuseCountChanged -= OnFuseCountChangedForSwitchArrow;
        _fuseBoxController.SetForceHighlight(false);
        TutorialMarkerManager.Instance?.Unmark(_fuseBoxController.transform);
    }

    // -------------------------------------------------------------------------
    // Post-shift power outage / fuse-box repair
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired by <see cref="ShiftManager.OnLastSuspectProcessed"/> on every client the instant the
    /// last suspect for the day is processed — before the timecard machine is ever primed for
    /// clock-out. One-shot per shift; unsubscribes itself immediately. When
    /// <see cref="_enablePostShiftPowerOutage"/> is disabled this is a no-op and the day proceeds
    /// straight to clock-out as normal.
    /// </summary>
    private void OnAllSuspectsProcessed_Day3()
    {
        ShiftManager.OnLastSuspectProcessed -= OnAllSuspectsProcessed_Day3;

        if (!_enablePostShiftPowerOutage) return;

        if (NetworkManager.Singleton.IsServer)
            ShiftManager.Instance?.RegisterPendingDailyTask(this);

        ((IDailyTask)this).TriggerDailyTask();
    }

    /// <summary>
    /// Fired on every client via <see cref="ElectricityController.OnPowerRestoredAllClients"/> once
    /// the fuse box is fixed and power comes back on. Resolves and clears the "Restore Power"
    /// guidebook task, then — since this fires locally on the server too — notifies
    /// <see cref="ShiftManager"/> that clock-out can proceed.
    /// </summary>
    private void OnPowerOutageResolved()
    {
        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients -= OnPowerOutageResolved;

        // Covers any leftover fuse-box tutorial subscription/highlight/arrow in case power was
        // restored via some other path (e.g. a debug cheat) without ever progressing through
        // the normal open-box / insert-fuses steps above.
        ClearFuseBoxTutorialState();

        if (_powerOutageThreat != null)
        {
            _powerOutageThreat.Resolve();
            TaskRegistry.Instance?.RemoveThreat(_powerOutageThreat);
            _powerOutageThreat = null;
        }

        // One-shot alert on the shared bottom-centre notification queue. It gets its own key
        // (the message), so it doesn't replace a pending shipment alert. Runs on every client
        // because this method already fires locally on each one via
        // ElectricityController.OnPowerRestoredAllClients.
        UIController.Instance?.ShowQueuedNotification("Power Restored");

        Debug.Log("[Day_03] Post-shift power outage resolved -- fuse box repaired.");
        OnDailyTaskCompleted?.Invoke();
    }
}

