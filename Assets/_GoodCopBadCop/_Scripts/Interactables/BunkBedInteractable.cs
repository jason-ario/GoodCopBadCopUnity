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
    /// Returns true when the shift has ended. Reads <see cref="ShiftManager.shiftStarted"/>
    /// directly — a <see cref="NetworkVariable{T}"/> set by <see cref="ShiftManager.EndShift"/>
    /// — so it is always reliable regardless of local event subscription state.
    /// </summary>
    private bool CanSleep =>
        ShiftManager.Instance != null
        && !ShiftManager.Instance.shiftStarted.Value;

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftEnd   -= HandleShiftEnd;
            ShiftManager.Instance.OnShiftStart -= HandleShiftStart;
        }
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    /// <summary>Updates the hover tooltip when the shift ends.</summary>
    private void HandleShiftEnd()
    {
        interactText = InteractTextReady;
    }

    /// <summary>Resets the hover tooltip when a new shift begins.</summary>
    private void HandleShiftStart()
    {
        interactText = InteractTextNotReady;
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
