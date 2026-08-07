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

        // Arm the Clean Blood task BEFORE spawning gore so every blood decal spawned
        // alongside it this cycle gets registered (see CleanBloodTask.TriggerTask doc comment).
        CleanBloodTask.Instance?.TriggerTask();
        TakeOutTrashTask.Instance?.TriggerTask(useGorePrefabs: true);

        // Randomly breaks a batch of perimeter fence segments so the yard has repair work
        // waiting alongside the gore/blood, mirroring the post-breach fence damage from Day 1
        // (see FenceRepairTask.TriggerTask doc comment). Self-guards to server-only.
        FenceRepairTask.Instance?.TriggerTask();

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

