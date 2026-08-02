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
    /// Subscribe to drive day-specific reactions (e.g. Day 3 power outage) that
    /// should feel like a direct consequence of the player clocking out.
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

    /// <summary>Seconds to wait after the punch animation fires before triggering the end-of-shift.</summary>
    [SerializeField] private float _punchToReportDelay = 1.5f;

    private static readonly int PunchTrigger = Animator.StringToHash("Punch");
    private static readonly int ReadyBool    = Animator.StringToHash("Ready");

    private bool _clockOutReady = false;
    private bool _clockInReady  = false;

    // Cached from the Inspector-assigned interactText so we can restore it after clock-in.
    private string _clockOutInteractText;

    protected override void Awake()
    {
        base.Awake();
        // Days other than Day 1 have no scripted opening sequence, so nothing else ever
        // arms clock-in for them — arm it here the instant the shift is reset and ready.
        // Day 1 keeps driving its own EnableClockIn() call from Day_01.OnDayStarted (its
        // tutorial arrow / shutter sequence needs to be wired up first); this handler simply
        // no-ops on Day 1 so the two paths never conflict.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady += OnShiftReady;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _clockOutInteractText = interactText;
    }

    public override void OnNetworkDespawn()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady -= OnShiftReady;
    }

    private void OnShiftReady()
    {
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        EnableClockIn();
    }

    public override void Interact(PlayerInteractionController player)
    {
        // Clock-in takes priority — checked first so it can fire before clock-out is ever armed.
        if (_clockInReady)
        {
            base.Interact(player);
            if (IsServer)
                HandleClockIn();
            else
                RequestClockInServerRpc();
            return;
        }

        if (!_clockOutReady) return;

        base.Interact(player);

        if (IsServer)
            HandleClockOut();
        else
            RequestClockOutServerRpc();
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
    }

    [ClientRpc]
    private void PunchClockInClientRpc()
    {
        _clockInReady = false;
        interactText  = _clockOutInteractText;

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
    }

    [ClientRpc]
    private void EnableClockOutClientRpc()
    {
        _clockOutReady = true;
        SetLightReady(true);

        if (_fanfareSource != null && _fanfareClip != null)
            _fanfareSource.PlayOneShot(_fanfareClip);
    }

    [ClientRpc]
    private void PunchCardClientRpc()
    {
        _clockOutReady = false;
        SetLightReady(false);

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
}
