using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Base class for every campaign day. Attach a concrete subclass (e.g. Day_01) to the
/// matching child GameObject under CampaignManager. Each day owns its own configuration
/// and exposes C# events that the day subclass and external systems can subscribe to.
///
/// CampaignManager activates the correct child GameObject and calls the virtual lifecycle
/// methods below. Override them in your day-specific subclass to add custom behaviour.
/// All day GameObjects are kept inactive by default; exactly one is active at a time.
/// </summary>
public abstract class DayBase : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector Configuration
    // -------------------------------------------------------------------------

    [Header("Day Identity")]
    [Tooltip("1-based day number. Must match the child position under CampaignManager.")]
    public int DayNumber;

    [Header("Cutscenes")]
    [Tooltip("The intro cutscene to play at the start of this day.")]
    public PlayableDirector IntroCutscene;

    [Header("Suspects")]
    [Tooltip("Optional. When assigned, overrides the global DailySuspectManager suspect pool for this day only. " +
             "Leave null (the default for all days) to draw randomly from the global pool, " +
             "which respects kill and quarantine-cooldown exclusions automatically. " +
             "Only use for fully scripted days (e.g. Day 1) that require a hand-authored lineup.")]
    public SuspectSet SuspectSet;

    [Tooltip("Number of suspects the player must process this shift. Read by DailySuspectManager " +
             "when it randomly populates the day's lineup (ignored when SuspectSet above is assigned, " +
             "since that fully authors its own lineup). Default 5.")]
    [Min(0)]
    public int SuspectsToProcess = 5;

    // -------------------------------------------------------------------------
    // Day Schedule — Tasks
    // -------------------------------------------------------------------------
    //
    // Every entry in the three lists below must be a MonoBehaviour that implements
    // IDailyTask (e.g. CleanGraffitiTask, TakeOutTrashTask, SortMailTask). Assigning a
    // task here does not change how the task itself works — it only controls WHEN
    // TriggerDailyTask() is called for this day. This is the single place to read a
    // day's full task schedule at a glance.

    [Header("Schedule — Pre-Shift Tasks (Dawn)")]
    [Tooltip("Daily tasks triggered automatically at Dawn, the moment this day is activated — " +
             "before the player clocks in (e.g. mail sorting, morning prep). " +
             "Each entry must implement IDailyTask.")]
    public MonoBehaviour[] PreShiftTasks;

    [Header("Schedule — Mid-Shift Tasks (Work Shift)")]
    [Tooltip("Daily tasks that MAY be triggered during the work shift. These are not fired " +
             "automatically — call TriggerMidShiftTasks() from wherever in your day script the " +
             "trigger should happen (e.g. after a specific suspect is processed). " +
             "Each entry must implement IDailyTask.")]
    public MonoBehaviour[] MidShiftTasks;

    [Header("Schedule — Post-Shift Tasks (Dusk)")]
    [Tooltip("Daily tasks triggered automatically at Dusk — the instant the last suspect for the " +
             "day is processed (e.g. clean graffiti, take out the trash). Clock-out stays locked " +
             "until every triggered task reports complete. Each entry must implement IDailyTask.")]
    public MonoBehaviour[] PostShiftTasks;

    [Header("Door")]
    [Tooltip("Lock the exit door for the entire shift on this day. Use for tutorial days.")]
    public bool LockDoorDuringShift;

    [Header("Supply Box Delivery")]
    [Tooltip("When true, a supply box delivery sequence plays at the start of this day.")]
    public bool HasSupplyBoxDelivery;

    [Tooltip("Item prefabs to spawn inside the supply box. Each prefab must have a NetworkObject component and be registered in the NetworkManager prefab list.")]
    public List<GameObject> SupplyBoxItemPrefabs = new List<GameObject>();

    /// <summary>
    /// Optional per-day override for where the supply box spawns, resolved fresh by
    /// <see cref="SupplyBoxDeliveryController"/> every time it spawns a box (rather than a
    /// one-shot mutable property set ahead of time, which could be silently missed if
    /// <see cref="ShiftManager.OnDayStart"/> ends up firing more than once or in an unexpected
    /// order). Return null to use the delivery controller's default spawn point. Override in a
    /// day subclass (e.g. Day_02) that needs a unique delivery position.
    /// </summary>
    public virtual Transform GetSupplyBoxSpawnPointOverride() => null;

    [Header("Events")]
    [Tooltip("When true, the 'Follow the Trail' event can trigger on this day.")]
    public bool CanFollowTrailEvent;

    [Header("Breach Event")]
    [Tooltip("Whether this day has a mutant breach event or not. When true, MutantBreachManager " +
             "triggers one random breach at the end of the day, once every suspect has been " +
             "processed AND every post-shift task above has been completed (never before Day 2, " +
             "regardless of this flag). Leave false on days that should never have a breach.")]
    public bool HasMutantBreach;

    [Tooltip("Which breach data this day uses. Pool of breach presets this day can roll from when " +
             "the breach triggers — one is chosen at random. Required (non-empty) when " +
             "HasMutantBreach is true.")]
    public MutantBreachData[] PossibleBreaches;

    [Header("Default Guards")]
    [Tooltip("Plain standing guard/soldier NPCs present in the scene by default (not tied to a " +
             "GuardPurchasePoint). Deactivated when this day starts. Assign on every day this " +
             "should hold true for (e.g. all days) so debug day-skipping still enforces it " +
             "regardless of entry point.")]
    public GameObject[] DefaultGuardsToDeactivate;

    [Tooltip("GuardPurchasePoint instances to unlock when this day starts. Leave empty on " +
             "days before purchase points should be available. The purchase points remain " +
             "active NetworkObjects at all times (required by Netcode) but stay locked and " +
             "hidden until unlocked.")]
    public GuardPurchasePoint[] GuardPurchasePointsToActivate;

    // -------------------------------------------------------------------------
    // C# Events — subscribe from external systems or day subclasses
    // -------------------------------------------------------------------------

    /// <summary>Fired by CampaignManager immediately after this day's GameObject is activated.</summary>
    public event Action OnDayStart;

    /// <summary>Fired when the player's shift ends (clock-out) on this day.</summary>
    public event Action OnShiftEnd;

    /// <summary>
    /// Fired after the end-of-shift report is dismissed and the between-shift sequence begins.
    /// Equivalent to the start of the night phase for this day.
    /// </summary>
    public event Action OnNightPhaseStart;

    /// <summary>
    /// Fired when all night tasks are complete and the day is fully resolved.
    /// CampaignManager will advance to the next day after this.
    /// </summary>
    public event Action OnDayComplete;

    // -------------------------------------------------------------------------
    // Lifecycle — called by CampaignManager; override in subclasses
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by CampaignManager when this day begins.
    /// The GameObject will already be active when this is invoked.
    /// </summary>
    public virtual void DayActivated()
    {
        Debug.Log($"[Day {DayNumber}] Day activated.");

        if (DefaultGuardsToDeactivate != null)
        {
            foreach (GameObject guard in DefaultGuardsToDeactivate)
            {
                if (guard != null)
                    guard.SetActive(false);
            }
        }

        if (GuardPurchasePointsToActivate != null)
        {
            foreach (GuardPurchasePoint purchasePoint in GuardPurchasePointsToActivate)
            {
                if (purchasePoint != null)
                    purchasePoint.SetUnlocked(true);
            }
        }

        // Best-effort immediate attempt — works on the normal (non-debug-skip) day-advance path.
        RestorePowerIfNoOutageIntended();

        // DayActivated runs before NGO (re)spawns scene NetworkObjects on the debug-skip path
        // (see Day_01.OnDayStarted for the same issue with ink-stamp locks), so the immediate
        // PowerOn() call above can be silently dropped. Re-apply once ShiftManager.OnDayStart
        // fires, by which point every NetworkBehaviour is guaranteed to be spawned.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += RestorePowerIfNoOutageIntendedOnDayStart;

        // Dawn — trigger this day's pre-shift tasks before the player clocks in.
        TriggerPreShiftTasks();

        OnDayStart?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Day Schedule — Task Triggers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Triggers every task in <see cref="PreShiftTasks"/>. Called automatically by
    /// <see cref="DayActivated"/> at Dawn, before the player clocks in.
    /// </summary>
    public void TriggerPreShiftTasks() => TriggerTaskList(PreShiftTasks, "pre-shift");

    /// <summary>
    /// Triggers every task in <see cref="MidShiftTasks"/>. Not called automatically — call this
    /// from a day subclass (or an event it subscribes to) at whatever moment during the work
    /// shift the trigger should happen, e.g. after a specific suspect is processed.
    /// </summary>
    public void TriggerMidShiftTasks() => TriggerTaskList(MidShiftTasks, "mid-shift");

    /// <summary>
    /// Triggers every task in <see cref="PostShiftTasks"/>. Called automatically by
    /// <see cref="ShiftManager"/> at Dusk, the instant the last suspect for the day is processed.
    /// </summary>
    public void TriggerPostShiftTasks() => TriggerTaskList(PostShiftTasks, "post-shift");

    private void TriggerTaskList(MonoBehaviour[] tasks, string scheduleLabel)
    {
        if (tasks == null) return;

        foreach (MonoBehaviour behaviour in tasks)
        {
            if (behaviour == null) continue;

            if (behaviour is IDailyTask task)
            {
                task.TriggerDailyTask();
            }
            else
            {
                Debug.LogWarning($"[Day {DayNumber}] {scheduleLabel} task '{behaviour.name}' does not implement IDailyTask — skipping.");
            }
        }
    }

    private void RestorePowerIfNoOutageIntendedOnDayStart()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= RestorePowerIfNoOutageIntendedOnDayStart;

        RestorePowerIfNoOutageIntended();
    }

    /// <summary>
    /// Override to true on the day(s) that actually host a fuse-box puzzle (e.g. Day 4).
    /// <see cref="RestorePowerIfNoOutageIntended"/> uses this to tell an intentional
    /// fuse-required outage (left alone so the puzzle can resolve it) apart from a stray
    /// one left over from a debug cheat or another day that has no fuse box to clear it.
    /// </summary>
    protected virtual bool SupportsFuseBoxRestore => false;

    /// <summary>
    /// Safety net so a scripted or debug-triggered blackout from a previous day never leaks
    /// into a later day's start. <see cref="ElectricityController"/>'s <c>_isPowerOn</c>
    /// NetworkVariable persists on its scene object across day transitions and debug day-skips,
    /// so without this, power left off by e.g. Day 3's scripted outage or a debug cheat would
    /// stay off going into Day 2/4/etc. Only restores power when the automatic random-outage
    /// feature is disabled. A fuse-box-required outage is left alone only on a day that
    /// declares <see cref="SupportsFuseBoxRestore"/> — on any other day it is treated as stray
    /// leftover state (e.g. from the SkipToDay4FusePowerOutage debug cheat) and force-cleared,
    /// since that day has no fuse box to resolve it and the standard CircuitBox refuses to
    /// restore power while the flag is set.
    /// </summary>
    private void RestorePowerIfNoOutageIntended()
    {
        ElectricityController ec = ElectricityController.Instance;
        if (ec == null) return;
        if (ec.EnablePowerOutage) return;

        if (ec.RequiresFuseBoxRestore && !SupportsFuseBoxRestore)
        {
            Debug.Log($"[Day {DayNumber}] Clearing a stray fuse-required power outage left over from another day — this day has no fuse box to resolve it.");
            ec.PowerOn();
            return;
        }

        if (ec.RequiresFuseBoxRestore) return;
        if (ec.IsPowerOn) return;

        Debug.Log($"[Day {DayNumber}] Power was left off from a previous day — restoring since automatic outages are disabled.");
        ec.PowerOn();
    }

    /// <summary>
    /// Called when the player clocks out and the shift ends.
    /// </summary>
    public virtual void ShiftEnded()
    {
        Debug.Log($"[Day {DayNumber}] Shift ended.");
        OnShiftEnd?.Invoke();
    }

    /// <summary>
    /// Called when the end-of-shift report is dismissed and the night phase begins.
    /// </summary>
    public virtual void NightPhaseStarted()
    {
        Debug.Log($"[Day {DayNumber}] Night phase started.");
        OnNightPhaseStart?.Invoke();
    }

    /// <summary>
    /// Called when all night-phase tasks are done and the day is fully complete.
    /// CampaignManager will call AdvanceDay after this returns.
    /// </summary>
    public virtual void DayCompleted()
    {
        Debug.Log($"[Day {DayNumber}] Day completed.");
        OnDayComplete?.Invoke();
    }

    /// <summary>
    /// Called by CampaignManager when this day is deactivated (next day is starting).
    /// The GameObject is deactivated immediately after this returns.
    /// </summary>
    public virtual void DayDeactivated()
    {
        Debug.Log($"[Day {DayNumber}] Day deactivated.");

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= RestorePowerIfNoOutageIntendedOnDayStart;
    }
}
