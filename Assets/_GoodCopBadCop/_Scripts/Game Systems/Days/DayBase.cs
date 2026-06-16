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
    [Tooltip("The set of suspects processed during this day's shift.")]
    public SuspectSet SuspectSet;

    [Header("Door")]
    [Tooltip("Lock the exit door for the entire shift on this day. Use for tutorial days.")]
    public bool LockDoorDuringShift;

    [Header("Tutorial Steps")]
    [Tooltip("Tutorial steps fired by CampaignManager when this day activates.")]
    public List<TutorialStep> TutorialStepsToFire;

    [Header("Supply Box Delivery")]
    [Tooltip("When true, a supply box delivery sequence plays at the start of this day.")]
    public bool HasSupplyBoxDelivery;

    [Tooltip("Item prefabs to spawn inside the supply box. Each prefab must have a NetworkObject component and be registered in the NetworkManager prefab list.")]
    public List<GameObject> SupplyBoxItemPrefabs = new List<GameObject>();

    [Header("Events")]
    [Tooltip("When true, the 'Follow the Trail' event can trigger on this day.")]
    public bool CanFollowTrailEvent;

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
        AnomalyManager.Instance?.ApplyUnlocksFromSave();
        OnDayStart?.Invoke();
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
