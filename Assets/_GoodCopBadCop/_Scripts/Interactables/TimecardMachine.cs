using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A timecard machine the player interacts with to clock in at the start of a shift
/// and clock out at the end.
///
/// Clock-in: enabled by <see cref="EnableClockIn"/> (server). Fires
/// <see cref="OnClockInAllClients"/> on every client when the punch lands.
///
/// Clock-out: enabled by <see cref="EnableClockOut"/> (server) when ShiftManager signals
/// all suspects have been processed. Fires <see cref="OnClockOutServer"/> on the server
/// then triggers EndShift after a short delay.
/// </summary>
public class TimecardMachine : Interactable
{
    /// <summary>
    /// Fired on the server the moment a clock-out punch is accepted.
    /// Currently unused — day-specific post-shift reactions (e.g. Day 3's power outage) are
    /// driven off <see cref="ShiftManager.OnLastSuspectProcessed"/> (Dusk) instead, so they
    /// happen the instant the last suspect is processed rather than waiting for the player to
    /// walk over and clock out. Kept available for any future reaction that should genuinely
    /// wait for the clock-out punch itself.
    /// </summary>
    public static event Action OnClockOutServer;

    /// <summary>
    /// Fired on ALL clients the moment a clock-in punch is accepted.
    /// Subscribe in Day_01 (or any day) to drive tutorial task progression that
    /// should react immediately once the player clocks in.
    /// </summary>
    public static event Action OnClockInAllClients;

    /// <summary>
    /// Fired on the server the moment a clock-in punch is accepted — before the punch
    /// is broadcast to clients. Subscribe to drive server-authoritative day sequencing
    /// (e.g. Day 1 opening the shutter and summoning Vlad) that must run reliably even
    /// on a dedicated server with no local client.
    /// </summary>
    public static event Action OnClockInServer;

    /// <summary>
    /// Fired on ALL clients the moment a clock-out punch is accepted.
    /// Subscribe to drive client-side reactions such as UI notifications.
    /// </summary>
    public static event Action OnClockOutAllClients;

    /// <summary>
    /// Ground-truth, always-current record of whether the player has punched the clock-out
    /// card for the current shift cycle. Unlike subscribing to <see cref="OnClockOutAllClients"/>
    /// (a fire-once event that a late subscriber can simply miss — e.g. an object that
    /// (re)spawns or resubscribes after the punch already landed), this flag can always be
    /// read directly to answer "has the player clocked out yet?" with no risk of desync.
    /// Set true the instant the clock-out punch animation fires on each client, reset false by
    /// <see cref="Reset"/> at the start of each new shift.
    /// </summary>
    public static bool HasClockedOutThisCycle { get; private set; }

    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clockOutSound;
    [SerializeField] private Animator _animator;

    [Header("Small Light")]
    [Tooltip("Animator on the Small Light child — drives the 'Ready' bool for the blink animation.")]
    [SerializeField] private Animator _lightAnimator;
    [Tooltip("World-space AudioSource to play the fanfare through when the machine becomes ready.")]
    [SerializeField] private AudioSource _fanfareSource;
    [Tooltip("Fanfare clip played when the timecard machine is primed for clock-out.")]
    [SerializeField] private AudioClip _fanfareClip;

    [Header("Interact Text")]
    [Tooltip("Text shown on the reticle while the machine is primed for clock-in.")]
    [SerializeField] private string _clockInInteractText = "Clock In";

    [Header("Tutorial Objective List")]
    [Tooltip("Text shown in the tutorial objective overlay while clock-in is available. " +
             "Day 1 drives its own scripted tutorial objectives, so this is skipped on Day 1.")]
    [SerializeField] private string _clockInObjectiveText = "Clock in for the day";
    [Tooltip("Text shown in the tutorial objective overlay while clock-out is available. " +
             "Day 1 drives its own scripted tutorial objectives, so this is skipped on Day 1.")]
    [SerializeField] private string _clockOutObjectiveText = "Clock out for the day";
    [Tooltip("Seconds the completed objective row stays visible (struck through) before it is removed.")]
    [SerializeField] private float _objectiveCompletedLingerDuration = 1f;

    /// <summary>Seconds to wait after the punch animation fires before triggering the end-of-shift.</summary>
    [SerializeField] private float _punchToReportDelay = 1.5f;

    private static readonly int PunchTrigger = Animator.StringToHash("Punch");
    private static readonly int ReadyBool    = Animator.StringToHash("Ready");

    private bool _clockOutReady = false;
    private bool _clockInReady  = false;

    // Tracks the tutorial objective overlay rows added while each punch is armed. Day 1 drives
    // its own scripted objective sequence for clock-in/out (see Day_01.ShowClockOutTask and its
    // arrow-based clock-in tutorial), so these are only added on days other than Day 1.
    private TutorialObjectiveItem _clockInObjective;
    private TutorialObjectiveItem _clockOutObjective;

    // Cached from the Inspector-assigned interactText so we can restore it after clock-in.
    private string _clockOutInteractText;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _clockOutInteractText = interactText;

        // Days other than Day 1 have no scripted opening sequence, so nothing else ever
        // arms clock-in for them — arm it here the instant the shift is reset and ready.
        // Day 1 keeps driving its own EnableClockIn() call from Day_01.OnDayStarted (its
        // tutorial arrow / shutter sequence needs to be wired up first); this handler simply
        // no-ops on Day 1 so the two paths never conflict.
        //
        // Subscribed here (OnNetworkSpawn) rather than Awake: Awake() execution order across
        // different components is not guaranteed, so if this ran before ShiftManager.Awake()
        // set ShiftManager.Instance, the subscription would silently never happen — leaving
        // clock-in permanently unable to re-arm itself past Day 1 for that whole session.
        // OnNetworkSpawn always runs after every scene object's Awake() has completed, so
        // ShiftManager.Instance is guaranteed to be ready here.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady += OnShiftReady;
    }

    public override void OnNetworkDespawn()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady -= OnShiftReady;
    }

    private void OnShiftReady()
    {
        Debug.Log($"[TimecardMachine] OnShiftReady fired. CurrentDay={ShiftManager.Instance?.CurrentDay ?? -1}.");

        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        EnableClockIn();
    }

    public override void Interact(PlayerInteractionController player)
    {
        // Clock-out takes priority. In the normal flow the two flags are mutually
        // exclusive, but if a stray EnableClockIn() (e.g. ShiftManager.OnShiftReady arming
        // the *next* day) ever lands while clock-out is still armed for the *current* day,
        // checking clock-in first would silently reinterpret the player's clock-OUT tap as a
        // clock-in — re-running TryStartShift() and re-populating the suspect lineup. Clock-out
        // must win that race since it always represents finishing the day already in progress.
        if (_clockOutReady)
        {
            base.Interact(player);

            if (IsServer)
                HandleClockOut();
            else
                RequestClockOutServerRpc();
            return;
        }

        if (!_clockInReady) return;

        base.Interact(player);

        if (IsServer)
            HandleClockIn();
        else
            RequestClockInServerRpc();
    }

    // ── Clock-In ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the server (e.g. Day_01) when it is time for the player to clock in.
    /// Enables the clock-in interaction on all clients.
    /// </summary>
    public void EnableClockIn()
    {
        if (!IsServer) return;
        _clockInReady = true;
        EnableClockInClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestClockInServerRpc() => HandleClockIn();

    private void HandleClockIn()
    {
        if (!_clockInReady) return;
        _clockInReady = false;
        OnClockInServer?.Invoke();
        PunchClockInClientRpc();

        // Day 1 drives its own scripted opening (shutter, Vlad reveal) and calls
        // ShiftManager.TryStartShift() itself once that sequence is ready — see
        // Day_01.OnPlayerClockedIn / Day1OpeningSequence. Every other day starts the
        // shift the instant the clock-in punch lands.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay != 1)
            ShiftManager.Instance.TryStartShift();
    }

    [ClientRpc]
    private void EnableClockInClientRpc()
    {
        _clockInReady = true;
        interactText  = _clockInInteractText;
        SetLightReady(true);
        AddObjectiveIfNotDay1(ref _clockInObjective, _clockInObjectiveText);
    }

    [ClientRpc]
    private void PunchClockInClientRpc()
    {
        _clockInReady = false;
        interactText  = _clockOutInteractText;
        SetLightReady(false);
        CompleteObjective(ref _clockInObjective);

        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);

        OnClockInAllClients?.Invoke();
    }

    // ── Clock-Out ─────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestClockOutServerRpc()
    {
        HandleClockOut();
    }

    private void HandleClockOut()
    {
        if (!_clockOutReady) return;

        _clockOutReady = false;
        OnClockOutServer?.Invoke();
        PunchCardClientRpc();
        StartCoroutine(EndShiftAfterDelay(_punchToReportDelay));
    }

    private IEnumerator EndShiftAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShiftManager.Instance.EndShift();
    }

    /// <summary>
    /// Called by ShiftManager on the server when all suspects have been processed.
    /// Enables clock-out interaction on all clients and triggers the fanfare and light.
    /// </summary>
    public void EnableClockOut()
    {
        if (!IsServer) return;

        _clockOutReady = true;
        EnableClockOutClientRpc();
    }

    /// <summary>Resets the machine to its default inactive state. Call at the start of each shift.</summary>
    public void Reset()
    {
        _clockOutReady = false;
        _clockInReady  = false;
        SetLightReady(false);
        _clockInObjective  = null;
        _clockOutObjective = null;
        HasClockedOutThisCycle = false;
    }

    [ClientRpc]
    private void EnableClockOutClientRpc()
    {
        _clockOutReady = true;
        SetLightReady(true);
        AddObjectiveIfNotDay1(ref _clockOutObjective, _clockOutObjectiveText);

        if (_fanfareSource != null && _fanfareClip != null)
            _fanfareSource.PlayOneShot(_fanfareClip);
    }

    [ClientRpc]
    private void PunchCardClientRpc()
    {
        _clockOutReady = false;
        SetLightReady(false);
        CompleteObjective(ref _clockOutObjective);

        // Ground truth: the punch has landed on this client, full stop. Set this before firing
        // OnClockOutAllClients so anything reacting to the event can also safely read this flag.
        HasClockedOutThisCycle = true;

        // Silence the fanfare immediately so the power-cut feels like a direct consequence.
        if (_fanfareSource != null)
            _fanfareSource.Stop();

        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);

        OnClockOutAllClients?.Invoke();
    }

    /// <summary>Sets the 'Ready' bool on the small light animator to drive the blink animation.</summary>
    private void SetLightReady(bool ready)
    {
        if (_lightAnimator != null)
            _lightAnimator.SetBool(ReadyBool, ready);
    }

    /// <summary>
    /// Adds a row to the tutorial objective overlay, unless the current day is Day 1 — Day 1
    /// drives its own scripted objective sequence for clock-in/out (see Day_01.ShowClockOutTask
    /// and its arrow-based clock-in tutorial), so adding a duplicate row here would conflict.
    /// </summary>
    private static void AddObjectiveIfNotDay1(ref TutorialObjectiveItem slot, string text)
    {
        int day = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : -1;
        Debug.Log($"[TimecardMachine] AddObjectiveIfNotDay1(\"{text}\") -- CurrentDay={day}, " +
                  $"TutorialObjectiveList.Instance={(TutorialObjectiveList.Instance != null)}.");

        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        slot = TutorialObjectiveList.Instance?.AddObjective(text);
    }

    /// <summary>Marks the given objective row complete and removes it shortly after, if one is tracked.</summary>
    private void CompleteObjective(ref TutorialObjectiveItem slot)
    {
        if (slot == null) return;
        TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(slot, _objectiveCompletedLingerDuration);
        slot = null;
    }
}
