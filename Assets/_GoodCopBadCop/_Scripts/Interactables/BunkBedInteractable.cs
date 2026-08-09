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

    [Header("Tutorial Objective List")]
    [Tooltip("Text shown in the tutorial objective overlay once the shift is over and the player " +
             "may go to bed. Day 1 drives its own scripted go-to-bed sequence, so this is skipped on Day 1.")]
    [SerializeField] private string _goToBedObjectiveText = "Go to bed for the night";
    [Tooltip("Seconds the completed objective row stays visible (struck through) before it is removed.")]
    [SerializeField] private float _objectiveCompletedLingerDuration = 1f;

    private const string InteractTextReady    = "Sleep";
    private const string InteractTextNotReady = "Finish remaining tasks to end the day  ";

    /// <summary>
    /// Fired locally when the player confirms ending the day at the bunk bed.
    /// Subscribe server-side (e.g. Day_01) to dismiss the "go to bed" marker/highlight.
    /// </summary>
    public static event Action OnSleepConfirmed;

    private PlayerInteractionController _interactingPlayer;

    /// <summary>
    /// Tracks the tutorial objective overlay row shown once <see cref="CanSleep"/> first becomes
    /// true for the current cycle. Day 1 drives its own scripted go-to-bed sequence (a world-space
    /// arrow/marker, see Day_01.ShowGoToBedMarker), so this row is only added on days other than
    /// Day 1 — see <see cref="UpdateInteractText"/>.
    /// </summary>
    private TutorialObjectiveItem _goToBedObjective;

    /// <summary>
    /// True once the player has punched out at the <see cref="TimecardMachine"/> for the
    /// current day. This is the single source of truth for "the day's work is done" — clocking
    /// out already implies every gating task (suspects processed, pending daily tasks, mutant
    /// breach) has been satisfied, since <see cref="ShiftManager"/> only arms the timecard
    /// machine's clock-out punch once all of that is true (see
    /// <see cref="ShiftManager.RecheckClockOutGate"/>/<c>TryEnableClockOut</c>). Reset to false
    /// whenever a new day/shift begins.
    /// </summary>
    private bool _clockedOutThisCycle;

    /// <summary>
    /// Debug/tutorial escape hatch. When true, <see cref="CanSleep"/> always returns true,
    /// bypassing <see cref="_clockedOutThisCycle"/> entirely. Set by <see cref="Day_01"/> while
    /// its "go to bed" tutorial marker is active — by the time that marker shows, Day 1's own
    /// tutorial gating (clock-out already succeeded, bunker door already opened) has already
    /// proven the day is genuinely done, so sleeping should never be blocked here even if
    /// <see cref="_clockedOutThisCycle"/> is out of sync (e.g. after a debug skip that bypasses
    /// part of the normal clock-out bookkeeping). Reset to false once sleep is confirmed or the
    /// marker is dismissed.
    /// </summary>
    public static bool ForceAllowSleep = false;

    /// <summary>
    /// Returns true once the player has clocked out for the day. Clocking out is already
    /// gated by <see cref="ShiftManager"/> on every other end-of-day requirement, so no
    /// additional task check is needed here. Reads <see cref="TimecardMachine.HasClockedOutThisCycle"/>
    /// directly (rather than relying solely on the cached <see cref="_clockedOutThisCycle"/> flag
    /// flipped by the <see cref="TimecardMachine.OnClockOutAllClients"/> event) so a clocked-out
    /// player can NEVER be blocked from sleeping even if this object missed that event —
    /// e.g. a subscription-order race, a respawned bed object, or any other desync between the
    /// event firing and this component being ready to receive it.
    /// </summary>
    private bool CanSleep => ForceAllowSleep || _clockedOutThisCycle || TimecardMachine.HasClockedOutThisCycle;

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

        TimecardMachine.OnClockOutAllClients += HandleClockedOut;

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

        TimecardMachine.OnClockOutAllClients -= HandleClockedOut;

        TaskRegistry.OnTaskListChanged  -= UpdateInteractText;
        TaskRegistry.OnTaskStateChanged -= UpdateInteractText;
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    /// <summary>Marks the day's work done the instant the clock-out punch lands — see <see cref="CanSleep"/>.</summary>
    private void HandleClockedOut()
    {
        _clockedOutThisCycle = true;
        UpdateInteractText();
        Debug.Log("[BunkBedInteractable] HandleClockedOut — _clockedOutThisCycle=true.");
    }

    /// <summary>Queues the go-to-bed task on the HUD once the shift ends.</summary>
    private void HandleShiftEnd()
    {
        GoToBunkerTask.CreateAndRegister();
        UpdateInteractText();
    }

    /// <summary>Resets clocked-out state when a new shift begins and cleans up any leftover go-to-bed task.</summary>
    private void HandleShiftStart()
    {
        _clockedOutThisCycle = false;
        GoToBunkerTask.CompleteAndRemove();
        interactText = InteractTextNotReady;
        _goToBedObjective = null;
    }

    /// <summary>Resets clocked-out state as soon as a new day begins, before the next shift starts.</summary>
    private void HandleDayStart()
    {
        _clockedOutThisCycle = false;
        interactText = InteractTextNotReady;
        _goToBedObjective = null;
    }

    /// <summary>
    /// Syncs <see cref="Interactable.interactText"/> with the current <see cref="CanSleep"/> state,
    /// and — the first time <see cref="CanSleep"/> becomes true for the current cycle — adds the
    /// "go to bed" row to the tutorial objective overlay (skipped on Day 1; see
    /// <see cref="_goToBedObjective"/>). Called on shift-end and whenever <see cref="TaskRegistry"/>
    /// reports a state change so both stay in sync as tasks are completed during the night phase.
    /// </summary>
    private void UpdateInteractText()
    {
        bool canSleep = CanSleep;
        interactText = canSleep ? InteractTextReady : InteractTextNotReady;

        if (canSleep && _goToBedObjective == null &&
            !(ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1))
        {
            _goToBedObjective = TutorialObjectiveList.Instance?.AddObjective(_goToBedObjectiveText);
        }
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
                   $"_clockedOutThisCycle={_clockedOutThisCycle}.");

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

        if (_goToBedObjective != null)
        {
            TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(_goToBedObjective, _objectiveCompletedLingerDuration);
            _goToBedObjective = null;
        }

        OnSleepConfirmed?.Invoke();

        // Analytics: track when a player uses the bunk bed to end the day, e.g. "BunkBed:Sleep:3".
        int day = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
        GameAnalyticsSDK.GameAnalytics.NewDesignEvent($"BunkBed:Sleep:{day}");
        GameAnalyticsSDK.GameAnalytics.NewProgressionEvent(GameAnalyticsSDK.GAProgressionStatus.Complete, $"Day{day}");

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
