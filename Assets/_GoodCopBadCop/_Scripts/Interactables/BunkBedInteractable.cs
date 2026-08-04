using System;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable placed on the bunk bed. Always opens the bed camera view on interact.
/// When the shift is over, shows the "End the day?" confirmation popup.
/// Otherwise shows "Can't sleep yet" with only the Back UI for exit.
/// On confirmation plays a sound effect and triggers
/// <see cref="ShiftManager.StartInBetweenShiftSequence"/>.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BunkBedInteractable : Interactable, IHeldItemPassthrough
{
    [Header("Bed Camera")]
    [Tooltip("CinemachineCamera that becomes active while the End Day popup is shown.")]
    [SerializeField] private CinemachineCamera _bedCamera;

    [Header("Audio")]
    [Tooltip("Sound played when the player confirms ending the day.")]
    [SerializeField] private AudioClip _endDaySFX;

    private const string InteractTextReady    = "Sleep";
    private const string InteractTextNotReady = "Finish remaining tasks to end the day  ";

    /// <summary>
    /// Fired locally when the player confirms ending the day at the bunk bed.
    /// Subscribe server-side (e.g. Day_01) to dismiss the "go to bed" marker/highlight.
    /// </summary>
    public static event Action OnSleepConfirmed;

    private PlayerInteractionController _interactingPlayer;

    /// <summary>
    /// True only once <see cref="ShiftManager.OnShiftEnd"/> has fired for the current day.
    /// <see cref="ShiftManager.shiftStarted"/> is <c>false</c> both "before the shift has
    /// started" and "after the shift has ended", so it cannot distinguish those two cases on
    /// its own. Without this flag, the brief window at the start of a new day — after
    /// <see cref="ShiftManager.OnDayStart"/> resets <c>shiftStarted</c> to false but before the
    /// player has started the next shift, and while no tasks are registered yet — would make
    /// <see cref="AllTasksComplete"/> report true and let the bed be used to "end the day" again
    /// immediately. Reset to false whenever a new day/shift begins.
    /// </summary>
    private bool _shiftEndedThisCycle;

    /// <summary>
    /// Debug/tutorial escape hatch. When true, <see cref="CanSleep"/> always returns true,
    /// bypassing <see cref="_shiftEndedThisCycle"/>, <see cref="ShiftManager.shiftStarted"/>, and
    /// <see cref="AllTasksComplete"/> entirely. Set by <see cref="Day_01"/> while its "go to bed"
    /// tutorial marker is active — by the time that marker shows, Day 1's own tutorial gating
    /// (clock-out already succeeded, bunker door already opened) has already proven the day is
    /// genuinely done, so sleeping should never be blocked here even if one of the normal
    /// conditions above is out of sync (e.g. after a debug skip that bypasses part of the
    /// normal shift-end bookkeeping). Reset to false once sleep is confirmed or the marker
    /// is dismissed.
    /// </summary>
    public static bool ForceAllowSleep = false;

    /// <summary>
    /// Returns true when the shift has ended AND all completable tasks are finished.
    /// Reads <see cref="ShiftManager.shiftStarted"/> directly — a <see cref="NetworkVariable{T}"/>
    /// set by <see cref="ShiftManager.EndShift"/> — so it is always reliable regardless of local
    /// event subscription state.
    /// </summary>
    private bool CanSleep =>
        ForceAllowSleep
        || (_shiftEndedThisCycle
            && ShiftManager.Instance != null
            && !ShiftManager.Instance.shiftStarted.Value
            && AllTasksComplete());

    /// <summary>
    /// Returns true when every <see cref="IBetweenShiftTask"/> registered in the
    /// <see cref="TaskRegistry"/> reports <c>IsComplete</c>.
    /// Navigation-only entries such as <see cref="GoToBunkerTask"/> are excluded.
    /// Returns true when no completable tasks are present (e.g. early in the night phase).
    /// </summary>
    private bool AllTasksComplete()
    {
        if (TaskRegistry.Instance == null) return true;

        foreach (ISystemicThreat threat in TaskRegistry.Instance.Threats)
        {
            if (threat is GoToBunkerTask) continue;

#pragma warning disable CS0618
            if (threat is IBetweenShiftTask task && !task.IsComplete)
            {
                Debug.Log($"[BunkBedInteractable] AllTasksComplete: blocked by incomplete task '{threat.ThreatName}' ({threat.GetType().Name}).");
                return false;
            }
#pragma warning restore CS0618
        }

        return true;
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractTextNotReady;

        // Bed camera starts inactive; Cinemachine blends to it when activated.
        if (_bedCamera != null)
            _bedCamera.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe here rather than OnEnable — OnNetworkSpawn is called after the network
        // is fully initialised so ShiftManager.Instance is guaranteed to be available.
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftEnd   += HandleShiftEnd;
            ShiftManager.Instance.OnShiftStart += HandleShiftStart;
            ShiftManager.Instance.OnDayStart   += HandleDayStart;
        }

        // Keep the interact label in sync whenever task state changes.
        TaskRegistry.OnTaskListChanged  += UpdateInteractText;
        TaskRegistry.OnTaskStateChanged += UpdateInteractText;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftEnd   -= HandleShiftEnd;
            ShiftManager.Instance.OnShiftStart -= HandleShiftStart;
            ShiftManager.Instance.OnDayStart   -= HandleDayStart;
        }

        TaskRegistry.OnTaskListChanged  -= UpdateInteractText;
        TaskRegistry.OnTaskStateChanged -= UpdateInteractText;
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    /// <summary>Queues the go-to-bed task on the HUD and refreshes the hover label when the shift ends.</summary>
    private void HandleShiftEnd()
    {
        _shiftEndedThisCycle = true;
        GoToBunkerTask.CreateAndRegister();
        UpdateInteractText();
        Debug.Log("[BunkBedInteractable] HandleShiftEnd — _shiftEndedThisCycle=true.");
    }

    /// <summary>Resets the hover label when a new shift begins and cleans up any leftover go-to-bed task.</summary>
    private void HandleShiftStart()
    {
        _shiftEndedThisCycle = false;
        GoToBunkerTask.CompleteAndRemove();
        interactText = InteractTextNotReady;
    }

    /// <summary>
    /// Resets the end-of-day flag as soon as a new day begins — this fires before the next
    /// shift officially starts, closing the window where <see cref="ShiftManager.shiftStarted"/>
    /// is still false (carried over from the previous day's end) and no tasks have been
    /// registered yet for the new day, which would otherwise make <see cref="CanSleep"/>
    /// incorrectly report true.
    /// </summary>
    private void HandleDayStart()
    {
        _shiftEndedThisCycle = false;
        interactText = InteractTextNotReady;
    }

    /// <summary>
    /// Syncs <see cref="Interactable.interactText"/> with the current <see cref="CanSleep"/> state.
    /// Called on shift-end and whenever <see cref="TaskRegistry"/> reports a state change so the
    /// tooltip updates as tasks are completed during the night phase.
    /// </summary>
    private void UpdateInteractText()
    {
        interactText = CanSleep ? InteractTextReady : InteractTextNotReady;
    }

    // ─── IInteractable ───────────────────────────────────────────────────────

    /// <summary>Always opens the bed view. Popup content depends on whether sleeping is allowed.</summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_interactingPlayer != null) return; // Already open.

        _interactingPlayer = player;
        OpenBedView(player);
    }

    // ─── View management ─────────────────────────────────────────────────────

    private void OpenBedView(PlayerInteractionController player)
    {
        // Suppress movement, reticle, and interaction while the popup is up.
        player.playerMovementController.SetCanControl(false);
        player.SetSuspectCamMode(true);

        // Activate the bed Cinemachine camera so Cinemachine blends to it.
        if (_bedCamera != null)
            _bedCamera.gameObject.SetActive(true);

        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(OnCancelEndDay);

        bool canSleep = CanSleep;
        Debug.Log($"[BunkBedInteractable] OpenBedView — CanSleep={canSleep}, ForceAllowSleep={ForceAllowSleep}, " +
                   $"_shiftEndedThisCycle={_shiftEndedThisCycle}, " +
                   $"shiftStarted.Value={(ShiftManager.Instance != null ? ShiftManager.Instance.shiftStarted.Value : (bool?)null)}, " +
                   $"AllTasksComplete={AllTasksComplete()}.");

        if (canSleep)
            UIController.Instance.OpenEndDayPopup(OnConfirmEndDay, OnCancelEndDay);
        else
            UIController.Instance.OpenEndDayBlockedPopup(OnCancelEndDay);
    }

    private void CloseBedView()
    {
        UIController.Instance.CloseEndDayPopup();
        UIController.Instance.HideBackButton();
        UIController.Instance.HideCursor();

        if (_bedCamera != null)
            _bedCamera.gameObject.SetActive(false);

        if (_interactingPlayer != null)
        {
            _interactingPlayer.SetSuspectCamMode(false);
            _interactingPlayer.playerMovementController.SetCanControl(true);
            _interactingPlayer = null;
        }
    }

    // ─── Popup callbacks ─────────────────────────────────────────────────────

    private void OnConfirmEndDay()
    {
        CloseBedView();

        GoToBunkerTask.CompleteAndRemove();

        OnSleepConfirmed?.Invoke();

        if (SFXController.Instance != null && _endDaySFX != null)
            SFXController.Instance.Play(_endDaySFX);

        // Route through the server so the end-of-shift report is shown on ALL clients.
        ShiftManager.Instance.TriggerEndOfShiftReportServerRpc();
    }

    private void OnCancelEndDay()
    {
        CloseBedView();
    }
}
