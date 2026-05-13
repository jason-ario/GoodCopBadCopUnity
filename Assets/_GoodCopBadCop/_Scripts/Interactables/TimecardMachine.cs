using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A timecard machine the player interacts with to clock out at the end of a shift.
/// Becomes interactable only after ShiftManager signals that all suspects have been processed.
/// Interacting calls ShiftManager.EndShift() through the server.
/// </summary>
public class TimecardMachine : Interactable
{
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clockOutSound;

    private bool _clockOutReady = false;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Clock Out";
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
        DisableClockOutClientRpc();
        ShiftManager.Instance.EndShift();
    }

    /// <summary>
    /// Called by ShiftManager on the server when all suspects have been processed.
    /// Enables clock-out interaction on all clients.
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

        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);
    }

    [ClientRpc]
    private void DisableClockOutClientRpc()
    {
        _clockOutReady = false;
        Highlight(false);
    }
}
