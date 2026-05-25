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

    /// <summary>Seconds to wait after the punch animation fires before triggering the end-of-shift report.</summary>
    [SerializeField] private float _punchToReportDelay = 1.5f;

    private static readonly int PunchTrigger = Animator.StringToHash("Punch");

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
    /// Enables clock-out interaction on all clients. The tutorial notification is
    /// already shown by ShiftManager.NotifyClockOutReadyClientRpc.
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
        Highlight(false);
    }

    [ClientRpc]
    private void EnableClockOutClientRpc()
    {
        _clockOutReady = true;
        Highlight(true);
    }

    [ClientRpc]
    private void PunchCardClientRpc()
    {
        _clockOutReady = false;
        Highlight(false);

        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);
    }
}
