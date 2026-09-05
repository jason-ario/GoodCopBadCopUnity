using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 — gore/body-part yard cleanup + a scripted mutant breach.
///
/// At day start, spawns gore and body-part junk items across the yard's
/// <see cref="TakeOutTrashTask"/> spawn zones (instead of standard trash), triggering the
/// Take Out Trash task so players must bag up every piece of gore. Each gore piece also drops
/// a blood decal, which arms <see cref="CleanBloodTask"/> so players must mop up every
/// splatter with the <see cref="Mop"/> as a separate task.
///
/// The day's mail delivery ("Sort the mail" task) is skipped entirely for Day 3 (see
/// <see cref="SortMailTask.SkipDeliveryForDay"/>) — no delivery, no crate, no sorting task —
/// since the mechanic is already established on Day 2 and Day 3 is already carrying the
/// gore/blood/fence cleanup plus its own mutant breach. The daily prohibited-goods roll still
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
            OnPowerOutageDialogueComplete();
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
            speakerColorOverride: _powerOutageCallSpeakerColor,
            useAlternateVoice: true,
            useTelephoneAudioSource: true);
    }

    /// <summary>
    /// Server-only (fires from the same server-only chain as <see cref="PlayPowerOutageDialogueAfterGrab"/>).
    /// Called once the HQ power-outage dialogue finishes (or immediately as a fallback if the
    /// dialogue couldn't be played). Grants the "Restore Power" guidebook task and starts
    /// listening for the fuse box to be fixed, then auto-hangs-up the phone so the player isn't
    /// stuck holding the handset.
    /// </summary>
    private void OnPowerOutageDialogueComplete()
    {
        _powerOutageThreat = new RepairPowerThreat();
        TaskRegistry.Instance?.AddThreat(_powerOutageThreat);

        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients += OnPowerOutageResolved;

        Telephone.Instance?.HangUpCurrentCaller();

        Debug.Log("[Day_03] HQ power-outage dialogue complete -- Restore Power task granted, hanging up.");
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
    // Inspector — Bunker Exit Stinger
    // -------------------------------------------------------------------------

    [Header("Day 3 — Bunker Exit Stinger")]
    [Tooltip("One-shot stinger played the first time a player opens the bunker door and " +
             "exits for Day 3. Played locally on every client via SFXController, matching " +
             "the pattern used by UIController's transition stinger.")]
    [SerializeField] private AudioClip _bunkerExitStinger;

    [SerializeField] private float _bunkerExitStingerVolume = 1f;

    private bool _bunkerExitStingerPlayed;

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

    [Tooltip("Subtitle name colour for the HQ power-outage call, matching the alternate-voice " +
             "convention used by Day 4's new voice announcement.")]
    [SerializeField] private Color _powerOutageCallSpeakerColor = new Color(0.85f, 0.05f, 0.05f);

    /// <summary>Runtime "Restore Power" guidebook task, created only while the outage is active.</summary>
    private RepairPowerThreat _powerOutageThreat;

    /// <summary>Runtime "Answer the Phone" guidebook task, created only while HQ's call is ringing.</summary>
    private AnswerPhoneThreat _answerPhoneThreat;

    // -------------------------------------------------------------------------
    // Inspector -- Yard Cleanup Objective Text
    // -------------------------------------------------------------------------
    //
    // NOTE: no hand-scripted objective rows are added by this script anymore.
    // TakeOutTrashTask, CleanBloodTask, and FenceRepairTask are all ISystemicThreats, so
    // HUDTaskList already adds/updates/removes their rows in TutorialObjectiveList
    // automatically via the shared TaskRegistry the moment each is triggered below in
    // DayActivated. Hand-scripting the same rows here (previously gated behind the bunker
    // door opening) just duplicated every row.

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
            // Arm the Clean Blood task BEFORE spawning gore so every blood decal spawned
            // alongside it this cycle gets registered (see CleanBloodTask.TriggerTask doc comment).
            CleanBloodTask.Instance?.TriggerTask();
            TakeOutTrashTask.Instance?.TriggerTask(useGorePrefabs: true);

            // Randomly breaks a batch of perimeter fence segments so the yard has repair work
            // waiting alongside the gore/blood, mirroring the post-breach fence damage from Day 1
            // (see FenceRepairTask.TriggerTask doc comment). Self-guards to server-only.
            FenceRepairTask.Instance?.TriggerTask();
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

        // Plays a one-shot stinger the first time a player opens the bunker door and steps
        // outside for Day 3. Reset per-day so a re-activation (e.g. debug skip) can replay it.
        _bunkerExitStingerPlayed = false;
        BunkerDoorController.OnDoorOpened += OnBunkerDoorOpenedFirstTime;

        Debug.Log("[Day_03] DayActivated -- subscribed to BunkerDoorController.OnDoorOpened. " +
                  "Bunker-exit stinger will play once the player opens the bunker door.");

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
        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpenedFirstTime;
        ShiftManager.OnLastSuspectProcessed -= OnAllSuspectsProcessed_Day3;
        Telephone.OnScriptedCallAnsweredAllClients -= OnPowerOutageCallAnsweredAllClients;

        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients -= OnPowerOutageResolved;
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

        if (_powerOutageThreat != null)
        {
            _powerOutageThreat.Resolve();
            TaskRegistry.Instance?.RemoveThreat(_powerOutageThreat);
            _powerOutageThreat = null;
        }

        // One-shot bottom-centre alert, reusing the same reveal-and-fade notification style as
        // the "shipment is waiting at the gate" alert -- loop: false so it shows once and goes
        // away for good rather than resurfacing. Runs on every client since this method already
        // fires locally on each one via ElectricityController.OnPowerRestoredAllClients.
        UIController.Instance?.ShowMailDeliveryNotification("Power Restored", loop: false);

        Debug.Log("[Day_03] Post-shift power outage resolved -- fuse box repaired.");
        OnDailyTaskCompleted?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Bunker exit stinger -- all clients
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on every client (via <see cref="BunkerDoorController.OnDoorOpened"/>) the moment
    /// the bunker door swings open. Since the door is force-closed at the start of every day
    /// (see <see cref="ShiftManager.InBetweenShiftSequence"/> / <see cref="BunkerDoorController.OnDayChanged"/>),
    /// the first invocation each Day 3 always corresponds to the player's first exit of the day.
    /// Plays the bunker-exit stinger only. Unsubscribes immediately so later door-opens that
    /// day (e.g. going back in and out) stay silent.
    ///
    /// No objective rows are added here — <see cref="TakeOutTrashTask"/>,
    /// <see cref="CleanBloodTask"/>, and <see cref="FenceRepairTask"/> are all
    /// <see cref="ISystemicThreat"/>s, so <see cref="HUDTaskList"/> already adds their rows to
    /// <see cref="TutorialObjectiveList"/> automatically via the shared <see cref="TaskRegistry"/>
    /// the moment they're triggered in <see cref="DayActivated"/> (day start). Adding them again
    /// here on door-open duplicated every row.
    /// </summary>
    private void OnBunkerDoorOpenedFirstTime()
    {
        if (_bunkerExitStingerPlayed) return;
        _bunkerExitStingerPlayed = true;

        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpenedFirstTime;

        if (_bunkerExitStinger == null)
            Debug.LogWarning("[Day_03] _bunkerExitStinger is not assigned -- skipping stinger playback.");
        else
            SFXController.Instance?.Play(_bunkerExitStinger, _bunkerExitStingerVolume);
    }
}

