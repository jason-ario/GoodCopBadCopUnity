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
    private const string InteractTextNotReady = "Can't sleep yet";

    private PlayerInteractionController _interactingPlayer;

    /// <summary>
    /// Returns true when the shift has ended AND all completable tasks are finished.
    /// Reads <see cref="ShiftManager.shiftStarted"/> directly — a <see cref="NetworkVariable{T}"/>
    /// set by <see cref="ShiftManager.EndShift"/> — so it is always reliable regardless of local
    /// event subscription state.
    /// </summary>
    private bool CanSleep =>
        ShiftManager.Instance != null
        && !ShiftManager.Instance.shiftStarted.Value
        && AllTasksComplete();

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
                return false;
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
        }

        TaskRegistry.OnTaskListChanged  -= UpdateInteractText;
        TaskRegistry.OnTaskStateChanged -= UpdateInteractText;
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    /// <summary>Queues the go-to-bed task on the HUD and refreshes the hover label when the shift ends.</summary>
    private void HandleShiftEnd()
    {
        GoToBunkerTask.CreateAndRegister();
        UpdateInteractText();
    }

    /// <summary>Resets the hover label when a new shift begins and cleans up any leftover go-to-bed task.</summary>
    private void HandleShiftStart()
    {
        GoToBunkerTask.CompleteAndRemove();
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

        if (CanSleep)
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

        if (SFXController.Instance != null && _endDaySFX != null)
            SFXController.Instance.Play(_endDaySFX);

        var reportData = ShiftManager.Instance.BuildEndOfShiftReport();
        UIController.Instance.ShowEndShiftReport(reportData);
    }

    private void OnCancelEndDay()
    {
        CloseBedView();
    }
}
