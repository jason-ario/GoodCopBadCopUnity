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
///
/// MULTIPLAYER CONTRACT — two invariants this class exists to guarantee:
/// <list type="number">
/// <item><b>Clocked out means sleepable, unconditionally, on every peer.</b> <see cref="CanSleep"/>
/// reads <see cref="TimecardMachine.HasClockedOutThisCycle"/>, which is backed by a replicated
/// <c>NetworkVariable</c>. There is deliberately NO local cached copy of that flag: a cache can be
/// missed (event subscription races, a respawned bed, a late joiner who never received the
/// fire-once punch event) and a missed cache is a player who is locked out of ending the day with
/// no way to recover. Reading replicated state means the answer converges for everyone, always.</item>
/// <item><b>Ending the day is a team action.</b> Whichever player confirms, <see cref="_endDayConfirmed"/>
/// replicates and every peer runs <see cref="ApplyEndDayConfirmed"/> — so the other player's bed
/// view / "End the day?" popup is dismissed and their control restored instead of leaving them
/// frozen behind a stale popup while the end-of-shift report comes up underneath it.</item>
/// </list>
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
    ///
    /// Raised on EVERY peer (driven off <see cref="_endDayConfirmed"/>), not just the player who
    /// pressed the button — the tutorial marker/highlight this drives is per-client presentation,
    /// so firing it only on the confirming client left the other player staring at a bunk-bed
    /// arrow for a day that had already ended.
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
    /// Server-owned latch: true once ANY player has confirmed ending the day for the current
    /// cycle. Serves three purposes at once:
    /// <list type="bullet">
    /// <item>replicates the confirmation so every peer tears down its own bed view (see
    /// <see cref="ApplyEndDayConfirmed"/>) — the fix for "the other player's clock-out screen
    /// stayed up";</item>
    /// <item>de-duplicates simultaneous confirms into a single end-of-shift report, since the
    /// server clears/sets it before broadcasting;</item>
    /// <item>tells a late joiner the day is already over rather than offering them a popup that
    /// would fire a second report.</item>
    /// </list>
    /// Reset by the server on shift/day start.
    /// </summary>
    private readonly NetworkVariable<bool> _endDayConfirmed = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Debug/tutorial escape hatch. When true, <see cref="CanSleep"/> always returns true.
    /// Set by <see cref="Day_01"/> while its "go to bed" tutorial marker is active — by the time
    /// that marker shows, Day 1's own tutorial gating (clock-out already succeeded, bunker door
    /// already opened) has already proven the day is genuinely done, so sleeping should never be
    /// blocked here even on a path that bypassed part of the normal clock-out bookkeeping (e.g. a
    /// debug day skip). Reset to false once sleep is confirmed or the marker is dismissed.
    /// </summary>
    public static bool ForceAllowSleep = false;

    /// <summary>
    /// True once the team has clocked out for the day — that is the ONLY condition, by design.
    /// Clocking out is already gated by <see cref="ShiftManager.TryEnableClockOut"/> on every
    /// other end-of-day requirement (all suspects processed, all pending daily tasks complete, no
    /// live mutant breach), so re-checking any of that here could only ever produce a false
    /// negative that traps the player.
    ///
    /// Reads <see cref="TimecardMachine.HasClockedOutThisCycle"/> — replicated server-authoritative
    /// state — directly, with no local cached copy. A cache is what previously made clocking out
    /// "sometimes" fail to unlock the bed: it was set from the fire-once
    /// <see cref="TimecardMachine.OnClockOutAllClients"/> event, which a peer can miss entirely
    /// (subscription-order race, respawned bed object, or a player who joined after the punch).
    /// </summary>
    private bool CanSleep => ForceAllowSleep || TimecardMachine.HasClockedOutThisCycle;

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

        // State-driven (not fire-once), so this ALSO fires at spawn on a late joiner with the
        // inherited value — which is what makes the bed correctly sleepable for a player who
        // joined after the team already punched out.
        TimecardMachine.OnClockedOutStateChanged += HandleClockedOutStateChanged;

        _endDayConfirmed.OnValueChanged += HandleEndDayConfirmedChanged;

        // Keep the interact label in sync whenever task state changes.
        TaskRegistry.OnTaskListChanged  += UpdateInteractText;
        TaskRegistry.OnTaskStateChanged += UpdateInteractText;

        // Resolve the label from replicated state immediately. Without this a late joiner whose
        // team had already clocked out would keep the "Finish remaining tasks" reticle hint
        // forever, since every event that refreshes it had already fired before they connected.
        UpdateInteractText();
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

        TimecardMachine.OnClockedOutStateChanged -= HandleClockedOutStateChanged;

        _endDayConfirmed.OnValueChanged -= HandleEndDayConfirmedChanged;

        TaskRegistry.OnTaskListChanged  -= UpdateInteractText;
        TaskRegistry.OnTaskStateChanged -= UpdateInteractText;
    }

    // ─── Event handlers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reacts to the replicated clock-out flag on every peer — including the value a late joiner
    /// inherits at spawn. Refreshes the reticle label, and if this player happens to be sitting in
    /// the bed view on the "Can't sleep yet" popup at the moment the OTHER player punches out,
    /// upgrades it in place to the confirmable "End the day?" popup instead of making them back
    /// out and re-interact.
    /// </summary>
    private void HandleClockedOutStateChanged(bool clockedOut)
    {
        UpdateInteractText();

        Debug.Log($"[BunkBedInteractable] HandleClockedOutStateChanged({clockedOut}) — CanSleep={CanSleep}.");

        if (!clockedOut || _interactingPlayer == null || _endDayConfirmed.Value) return;
        if (!CanSleep) return;

        UIController.Instance.OpenEndDayPopup(OnConfirmEndDay, OnCancelEndDay);
    }

    /// <summary>Queues the go-to-bed task on the HUD once the shift ends.</summary>
    private void HandleShiftEnd()
    {
        GoToBunkerTask.CreateAndRegister();
        UpdateInteractText();
    }

    /// <summary>Resets end-of-day state when a new shift begins and cleans up any leftover go-to-bed task.</summary>
    private void HandleShiftStart()
    {
        GoToBunkerTask.CompleteAndRemove();
        interactText = InteractTextNotReady;
        _goToBedObjective = null;
        ClearEndDayConfirmed();
    }

    /// <summary>Resets end-of-day state as soon as a new day begins, before the next shift starts.</summary>
    private void HandleDayStart()
    {
        interactText = InteractTextNotReady;
        _goToBedObjective = null;
        ClearEndDayConfirmed();
    }

    /// <summary>Server-only clear of the end-of-day latch; replicates the reset to every peer.</summary>
    private void ClearEndDayConfirmed()
    {
        if (!IsSpawned || !IsServer) return;
        _endDayConfirmed.Value = false;
    }

    /// <summary>
    /// Syncs <see cref="Interactable.interactText"/> with the current <see cref="CanSleep"/> state,
    /// and — the first time <see cref="CanSleep"/> becomes true for the current cycle — adds the
    /// "go to bed" row to the tutorial objective overlay (skipped on Day 1; see
    /// <see cref="_goToBedObjective"/>). Called on shift-end, on spawn (so a late joiner resolves
    /// it from replicated state), whenever the replicated clock-out flag changes, and whenever
    /// <see cref="TaskRegistry"/> reports a state change.
    /// </summary>
    private void UpdateInteractText()
    {
        // The day is already over — don't re-offer it, and never hand out a fresh "go to bed" row
        // during the end-of-shift transition.
        if (_endDayConfirmed.Value)
        {
            interactText = InteractTextNotReady;
            return;
        }

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

        // The day is already over because the other player confirmed it — opening a second
        // "End the day?" popup here would let this player fire a duplicate end-of-shift report.
        if (_endDayConfirmed.Value) return;

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
                   $"HasClockedOutThisCycle={TimecardMachine.HasClockedOutThisCycle}.");

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

    /// <summary>
    /// The local player pressed "Yes". Closes this player's view immediately for responsiveness,
    /// then routes the decision through the server so it becomes the whole team's decision —
    /// <see cref="ApplyEndDayConfirmed"/> then runs on every peer, including the other player who
    /// may be sitting in their own bed view right now.
    /// </summary>
    private void OnConfirmEndDay()
    {
        if (_endDayConfirmed.Value) return;

        // Immediate local response — don't make the confirming player wait out a server
        // round-trip staring at their own popup. The replicated apply below is idempotent.
        CloseBedView();

        // Analytics: track when a player uses the bunk bed to end the day, e.g. "BunkBed:Sleep:3".
        // Logged only here, on the client that actually pressed the button, so a two-player
        // session doesn't double-count a single end-of-day.
        int day = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
        GameAnalyticsSDK.GameAnalytics.NewDesignEvent($"BunkBed:Sleep:{day}");
        GameAnalyticsSDK.GameAnalytics.NewProgressionEvent(GameAnalyticsSDK.GAProgressionStatus.Complete, $"Day{day}");

        if (IsServer)
            ConfirmEndDayServer();
        else
            RequestConfirmEndDayServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestConfirmEndDayServerRpc() => ConfirmEndDayServer();

    /// <summary>
    /// Single server-side resolution point for ending the day, whichever player asked for it.
    /// The latch is set before the report is triggered, so if both players confirm on the same
    /// frame the second request is dropped and only one end-of-shift report is broadcast.
    /// </summary>
    private void ConfirmEndDayServer()
    {
        if (!IsServer || _endDayConfirmed.Value) return;

        _endDayConfirmed.Value = true;

        // Route through the server so the end-of-shift report is shown on ALL clients.
        ShiftManager.Instance.TriggerEndOfShiftReportServerRpc();
    }

    /// <summary>
    /// Replicated confirmation arriving on every peer — this is what makes the end of day sync
    /// regardless of who initiated it.
    /// </summary>
    private void HandleEndDayConfirmedChanged(bool previous, bool current)
    {
        if (previous == current || !current) return;
        ApplyEndDayConfirmed();
    }

    /// <summary>
    /// Runs on EVERY peer the moment the day is confirmed ended. Idempotent and safe on a client
    /// that was nowhere near the bed.
    /// </summary>
    private void ApplyEndDayConfirmed()
    {
        Debug.Log($"[BunkBedInteractable] ApplyEndDayConfirmed — inBedView={_interactingPlayer != null}.");

        // The other player may be sitting in the bed view right now, on either the "End the day?"
        // or "Can't sleep yet" popup. Tear it down and hand their controls back, otherwise they
        // stay frozen behind a stale popup while the end-of-shift report fades in underneath it.
        // Guarded on actually being in the view so this never yanks the cursor/back button away
        // from a player who has some other UI open.
        if (_interactingPlayer != null)
            CloseBedView();

        GoToBunkerTask.CompleteAndRemove();

        if (_goToBedObjective != null)
        {
            TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(_goToBedObjective, _objectiveCompletedLingerDuration);
            _goToBedObjective = null;
        }

        // Per-client presentation cleanup (Day 1's bunk-bed marker/highlight) now runs for both
        // players rather than only the one who pressed Yes.
        OnSleepConfirmed?.Invoke();

        if (SFXController.Instance != null && _endDaySFX != null)
            SFXController.Instance.Play(_endDaySFX);
    }

    private void OnCancelEndDay()
    {
        CloseBedView();
    }
}
