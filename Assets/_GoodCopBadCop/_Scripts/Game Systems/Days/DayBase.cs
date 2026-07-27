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

        RestorePowerIfNoOutageIntended();

        OnDayStart?.Invoke();
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
    }
}
