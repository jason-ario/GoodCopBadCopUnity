using UnityEngine;

/// <summary>
/// Day 3 — gore/body-part yard cleanup + a scripted mutant breach that ends the demo.
///
/// At day start, spawns gore and body-part junk items across the yard's
/// <see cref="TakeOutTrashTask"/> spawn zones (instead of standard trash), triggering the
/// Take Out Trash task so players must bag up every piece of gore. Each gore piece also drops
/// a blood decal, which arms <see cref="CleanBloodTask"/> so players must mop up every
/// splatter with the <see cref="Mop"/> as a separate task.
///
/// The day's mail delivery ("Sort the mail" task) is deferred (see
/// <see cref="SortMailTask.DeferAutoTriggerForDay"/>) so it does NOT appear automatically at
/// day start — instead it triggers mid-shift, right after <see cref="_mailDeliverySuspectThreshold"/>
/// suspects have been processed (see <see cref="OnSuspectProcessedForMailDelivery"/>), with a
/// fallback that fires it at end of shift if that threshold is never reached.
///
/// Right after the last suspect for the day is processed, <see cref="MutantBreachManager"/>
/// (driven by <see cref="DayBase.HasMutantBreach"/> / <see cref="DayBase.PossibleBreaches"/>)
/// schedules and runs this day's mutant breach automatically. This is currently the demo's
/// final scripted breach, so its assigned <see cref="MutantBreachData"/> preset should have
/// <see cref="MutantBreachData.showThanksForPlayingOnClear"/> set — once every breach mutant
/// is defeated, the campaign is marked complete and the Thanks For Playing screen is shown.
/// </summary>
public class Day_03 : DayBase
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
    // Inspector -- Yard Cleanup Objectives (gore, blood, fences)
    // -------------------------------------------------------------------------

    [Header("Day 3 -- Yard Cleanup Objectives")]
    [Tooltip("Objective list text for the gore/corpse pickup task, shown once the player " +
             "steps outside for the day.")]
    [SerializeField] private string _taskTakeOutGoreText = "Take out the gore";

    [Tooltip("Objective list text for the blood splatter cleanup task.")]
    [SerializeField] private string _taskCleanBloodSplatterText = "Clean up the blood";

    [Tooltip("Objective list text for the perimeter fence repair task.")]
    [SerializeField] private string _taskFixFencesText = "Fix perimeter fences";

    private TutorialObjectiveItem _taskTakeOutGore;
    private TutorialObjectiveItem _taskCleanBloodSplatter;
    private TutorialObjectiveItem _taskFixFences;

    // -------------------------------------------------------------------------
    // Inspector -- Mid-Shift Mail Delivery Timing
    // -------------------------------------------------------------------------

    [Header("Day 3 -- Mail Delivery Timing")]
    [Tooltip("Number of suspects that must be processed this shift before the mail delivery " +
             "(\"Sort the mail\" task) triggers mid-shift, instead of appearing automatically " +
             "at the start of the day.")]
    [SerializeField] private int _mailDeliverySuspectThreshold = 2;

    private int _mailDeliveryProcessedCount;
    private bool _mailDeliveryTriggered;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Drop any leftover subscriptions/handles from a previous activation (e.g. a debug
        // skip re-triggering Day 3) before arming everything fresh.
        UnsubscribeAll();
        _taskTakeOutGore = null;
        _taskCleanBloodSplatter = null;
        _taskFixFences = null;

        // Arm the Clean Blood task BEFORE spawning gore so every blood decal spawned
        // alongside it this cycle gets registered (see CleanBloodTask.TriggerTask doc comment).
        CleanBloodTask.Instance?.TriggerTask();
        TakeOutTrashTask.Instance?.TriggerTask(useGorePrefabs: true);

        // Randomly breaks a batch of perimeter fence segments so the yard has repair work
        // waiting alongside the gore/blood, mirroring the post-breach fence damage from Day 1
        // (see FenceRepairTask.TriggerTask doc comment). Self-guards to server-only.
        FenceRepairTask.Instance?.TriggerTask();

        // Arms the Mutant Ocho / Vlad-corpse roof cutscene right as the player exits the
        // bunker for Day 3 -- see OchoEatingVladCutscene for the full sequence.
        OchoEatingVladCutscene.Instance?.TriggerTask();

        // Plays a one-shot stinger and reveals the yard-cleanup objective list the first time
        // a player opens the bunker door and steps outside for Day 3. Reset per-day so a
        // re-activation (e.g. debug skip) can replay it.
        _bunkerExitStingerPlayed = false;
        BunkerDoorController.OnDoorOpened += OnBunkerDoorOpenedFirstTime;

        // Defer the automatic Day 3 mail delivery -- it should trigger mid-shift, right after
        // the _mailDeliverySuspectThreshold-th suspect is processed, rather than appearing
        // immediately at day start (see OnSuspectProcessedForMailDelivery /
        // SortMailTask.DeferAutoTriggerForDay). Must be set here, before CampaignManager's
        // OnDayChanged fires.
        SortMailTask.DeferAutoTriggerForDay = 3;
        _mailDeliveryProcessedCount = 0;
        _mailDeliveryTriggered = false;
        FolderController.OnFolderHandedOff += OnSuspectProcessedForMailDelivery;
        ShiftManager.OnLastSuspectProcessed += OnLastSuspectProcessed_TriggerMailFallback;
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

    private void UnsubscribeAll()
    {
        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpenedFirstTime;

        FolderController.OnFolderHandedOff  -= OnSuspectProcessedForMailDelivery;
        ShiftManager.OnLastSuspectProcessed -= OnLastSuspectProcessed_TriggerMailFallback;

        TakeOutTrashTask.OnProgressChanged   -= OnTakeOutGoreProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited -= OnTakeOutGoreTaskComplete;

        CleanBloodTask.OnProgressChanged -= OnCleanBloodSplatterProgressChanged;
        if (CleanBloodTask.Instance != null)
            CleanBloodTask.Instance.OnDailyTaskCompleted -= OnCleanBloodSplatterTaskComplete;

        FenceRepairTask.OnProgressChanged   -= OnFixFencesProgressChanged;
        FenceRepairTask.OnAllFencesRepaired -= OnFixFencesTaskComplete;
    }

    // -------------------------------------------------------------------------
    // Mid-Shift Mail Delivery Trigger
    // -------------------------------------------------------------------------

    /// <summary>
    /// Counts processed suspects via <see cref="FolderController.OnFolderHandedOff"/> (fired on
    /// all clients) and fires the deferred Day 3 mail delivery once
    /// <see cref="_mailDeliverySuspectThreshold"/> suspects have been processed this shift.
    /// <see cref="SortMailTask.TriggerDeferredDelivery"/> is server-only internally, so it's
    /// safe to call unconditionally from every client here.
    /// </summary>
    private void OnSuspectProcessedForMailDelivery()
    {
        _mailDeliveryProcessedCount++;
        if (_mailDeliveryProcessedCount < _mailDeliverySuspectThreshold) return;

        TriggerDeferredMailDelivery();
    }

    /// <summary>
    /// Safety net: if the shift ends (last suspect processed) before
    /// <see cref="_mailDeliverySuspectThreshold"/> suspects were processed -- e.g. this day
    /// rolled fewer suspects than the threshold -- fire the deferred mail delivery anyway so
    /// it's never permanently skipped for the day.
    /// </summary>
    private void OnLastSuspectProcessed_TriggerMailFallback()
    {
        TriggerDeferredMailDelivery();
    }

    /// <summary>
    /// One-shot: fires the Day 3 mail delivery that was deferred in <see cref="DayActivated"/>
    /// and unsubscribes both triggers above so it can never fire twice in one shift.
    /// </summary>
    private void TriggerDeferredMailDelivery()
    {
        if (_mailDeliveryTriggered) return;
        _mailDeliveryTriggered = true;

        FolderController.OnFolderHandedOff  -= OnSuspectProcessedForMailDelivery;
        ShiftManager.OnLastSuspectProcessed -= OnLastSuspectProcessed_TriggerMailFallback;

        if (SortMailTask.DeferAutoTriggerForDay == 3)
            SortMailTask.DeferAutoTriggerForDay = -1;

        SortMailTask.Instance?.TriggerDeferredDelivery();
    }

    // -------------------------------------------------------------------------
    // Bunker exit stinger + yard cleanup objectives -- all clients
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on every client (via <see cref="BunkerDoorController.OnDoorOpened"/>) the moment
    /// the bunker door swings open. Since the door is force-closed at the start of every day
    /// (see <see cref="ShiftManager.InBetweenShiftSequence"/> / <see cref="BunkerDoorController.OnDayChanged"/>),
    /// the first invocation each Day 3 always corresponds to the player's first exit of the day.
    /// Plays the bunker-exit stinger and reveals the yard-cleanup objective list (gore, blood,
    /// fences) in lockstep. Unsubscribes immediately so later door-opens that day (e.g. going
    /// back in and out) stay silent.
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

        EnsureTakeOutGoreObjective();
        EnsureCleanBloodSplatterObjective();
        EnsureFixFencesObjective();
    }

    // -------------------------------------------------------------------------
    // Take Out Gore Objective
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds the "Take out the gore" objective if the day's gore/body-part junk hasn't already
    /// been fully collected -- <see cref="TakeOutTrashTask"/>'s counts are already accurate by
    /// the time the player walks outside, since it was triggered back in <see cref="DayActivated"/>.
    /// No-op if there's nothing left to collect.
    /// </summary>
    private void EnsureTakeOutGoreObjective()
    {
        if (TakeOutTrashTask.Instance != null &&
            TakeOutTrashTask.Instance.TotalCount > TakeOutTrashTask.Instance.DepositedCount)
        {
            _taskTakeOutGore = TutorialObjectiveList.Instance?.AddObjective(GetTakeOutGoreTaskText());

            TakeOutTrashTask.OnProgressChanged   += OnTakeOutGoreProgressChanged;
            TakeOutTrashTask.OnAllItemsDeposited += OnTakeOutGoreTaskComplete;
        }
    }

    private void OnTakeOutGoreProgressChanged()
    {
        if (TakeOutTrashTask.Instance == null) return;
        _taskTakeOutGore?.SetText(GetTakeOutGoreTaskText());
    }

    private void OnTakeOutGoreTaskComplete()
    {
        TakeOutTrashTask.OnProgressChanged   -= OnTakeOutGoreProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited -= OnTakeOutGoreTaskComplete;

        TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(_taskTakeOutGore, preHideDelay: 1.5f);
        _taskTakeOutGore = null;
    }

    private string GetTakeOutGoreTaskText() =>
        TakeOutTrashTask.Instance != null && TakeOutTrashTask.Instance.TotalCount > 0
            ? $"{_taskTakeOutGoreText} {TakeOutTrashTask.Instance.DepositedCount}/{TakeOutTrashTask.Instance.TotalCount}"
            : _taskTakeOutGoreText;

    // -------------------------------------------------------------------------
    // Clean Blood Splatter Objective
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds the "Clean up the blood" objective if any blood splatters from the day's gore are
    /// still unscrubbed -- <see cref="CleanBloodTask"/>'s counts are already accurate by the
    /// time the player walks outside, since it was triggered back in <see cref="DayActivated"/>.
    /// No-op if there's nothing left to scrub.
    /// </summary>
    private void EnsureCleanBloodSplatterObjective()
    {
        if (CleanBloodTask.Instance != null &&
            CleanBloodTask.Instance.TotalCount > CleanBloodTask.Instance.ScrubbedCount)
        {
            _taskCleanBloodSplatter = TutorialObjectiveList.Instance?.AddObjective(GetCleanBloodSplatterTaskText());

            CleanBloodTask.OnProgressChanged             += OnCleanBloodSplatterProgressChanged;
            CleanBloodTask.Instance.OnDailyTaskCompleted += OnCleanBloodSplatterTaskComplete;
        }
    }

    private void OnCleanBloodSplatterProgressChanged()
    {
        if (CleanBloodTask.Instance == null) return;
        _taskCleanBloodSplatter?.SetText(GetCleanBloodSplatterTaskText());
    }

    private void OnCleanBloodSplatterTaskComplete()
    {
        if (CleanBloodTask.Instance != null)
            CleanBloodTask.Instance.OnDailyTaskCompleted -= OnCleanBloodSplatterTaskComplete;
        CleanBloodTask.OnProgressChanged -= OnCleanBloodSplatterProgressChanged;

        TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(_taskCleanBloodSplatter, preHideDelay: 1.5f);
        _taskCleanBloodSplatter = null;
    }

    private string GetCleanBloodSplatterTaskText() =>
        CleanBloodTask.Instance != null && CleanBloodTask.Instance.TotalCount > 0
            ? $"{_taskCleanBloodSplatterText} {CleanBloodTask.Instance.ScrubbedCount}/{CleanBloodTask.Instance.TotalCount}"
            : _taskCleanBloodSplatterText;

    // -------------------------------------------------------------------------
    // Fix Perimeter Fences Objective
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds the "Fix perimeter fences" objective if any fence segments broken this cycle are
    /// still unrepaired -- <see cref="FenceRepairTask"/> was triggered back in
    /// <see cref="DayActivated"/>, so its counts are already accurate by the time the player
    /// walks outside. No-op if no fences came out damaged.
    /// </summary>
    private void EnsureFixFencesObjective()
    {
        if (FenceRepairTask.Instance != null && FenceRepairTask.Instance.TotalCount > 0)
        {
            _taskFixFences = TutorialObjectiveList.Instance?.AddObjective(GetFixFencesTaskText());

            FenceRepairTask.OnProgressChanged   += OnFixFencesProgressChanged;
            FenceRepairTask.OnAllFencesRepaired += OnFixFencesTaskComplete;
        }
    }

    private void OnFixFencesProgressChanged()
    {
        _taskFixFences?.SetText(GetFixFencesTaskText());
    }

    private void OnFixFencesTaskComplete()
    {
        FenceRepairTask.OnProgressChanged   -= OnFixFencesProgressChanged;
        FenceRepairTask.OnAllFencesRepaired -= OnFixFencesTaskComplete;

        TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(_taskFixFences, preHideDelay: 1.5f);
        _taskFixFences = null;
    }

    private string GetFixFencesTaskText() =>
        FenceRepairTask.Instance != null && FenceRepairTask.Instance.TotalCount > 0
            ? $"{_taskFixFencesText} {FenceRepairTask.Instance.RepairedCount}/{FenceRepairTask.Instance.TotalCount}"
            : _taskFixFencesText;
}

