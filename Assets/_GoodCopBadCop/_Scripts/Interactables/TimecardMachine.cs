using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A timecard machine the player interacts with to clock out at the end of a shift.
/// Becomes interactable only after ShiftManager signals that all suspects have been processed.
/// Interacting plays the punch animation and sound, then triggers EndShift after a short delay.
/// </summary>
public class TimecardMachine : Interactable
{
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

    [Header("Door")]
    [Tooltip("Optional door to open on all clients when the player punches the timecard. " +
             "When assigned, the door swings open as part of the clock-out interaction.")]
    [SerializeField] private BunkerDoorController _doorToOpen;

    /// <summary>Seconds to wait after the punch animation fires before triggering the end-of-shift.</summary>
    [SerializeField] private float _punchToReportDelay = 1.5f;

    private static readonly int PunchTrigger = Animator.StringToHash("Punch");
    private static readonly int ReadyBool    = Animator.StringToHash("Ready");

    private bool _clockOutReady = false;

    protected override void Awake()
    {
        base.Awake();
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (!_clockOutReady) return;

        base.Interact(player);

        if (IsServer)
            HandleClockOut();
        else
            RequestClockOutServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestClockOutServerRpc()
    {
        HandleClockOut();
    }

    private void HandleClockOut()
    {
        if (!_clockOutReady) return;

        _clockOutReady = false;
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

        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);

        _doorToOpen?.Open();
    }

    /// <summary>Sets the 'Ready' bool on the small light animator to drive the blink animation.</summary>
    private void SetLightReady(bool ready)
    {
        if (_lightAnimator != null)
            _lightAnimator.SetBool(ReadyBool, ready);
    }
}
