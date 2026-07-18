using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A networked table bell the player can ring by interacting with it.
/// Also supports autonomous suspect-driven ringing: when a suspect arrives at the booth
/// and no player is inside, the bell rings every <see cref="_suspectRingInterval"/> seconds
/// until a player enters the booth (after which it stops permanently for that suspect).
/// </summary>
public class TableBell : Interactable
{
    [SerializeField] private AudioSource _ringSource;
    [SerializeField] private float _suspectRingInterval = 10f;

    private Coroutine _suspectRingCoroutine;

    /// <summary>
    /// Server-only flag. Once the booth becomes ready for the current suspect
    /// (a player entered), the bell will not ring again until the next suspect starts waiting.
    /// </summary>
    private bool _suspectRingPermanentlyStopped;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Ring Bell";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        SuspectController.OnSuspectWaitingAtBooth += HandleSuspectWaiting;
        SuspectController.OnBoothBecameReady += HandleBoothBecameReady;
    }

    public override void OnNetworkDespawn()
    {
        SuspectController.OnSuspectWaitingAtBooth -= HandleSuspectWaiting;
        SuspectController.OnBoothBecameReady -= HandleBoothBecameReady;
    }

    // -------------------------------------------------------------------------
    // Player interaction
    // -------------------------------------------------------------------------

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsServer)
            PlayBellClientRpc();
        else
            RequestRingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRingServerRpc()
    {
        PlayBellClientRpc();
    }

    // -------------------------------------------------------------------------
    // Suspect-driven ringing (server-side)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called on the server when a suspect starts waiting at the booth with no player present.
    /// Resets the stopped flag and begins the periodic ringing coroutine.
    /// </summary>
    private void HandleSuspectWaiting()
    {
        _suspectRingPermanentlyStopped = false;

        if (_suspectRingCoroutine != null)
            StopCoroutine(_suspectRingCoroutine);

        _suspectRingCoroutine = StartCoroutine(SuspectRingCoroutine());
    }

    /// <summary>
    /// Called on the server when a player enters the booth while a suspect is waiting.
    /// Permanently stops suspect-driven ringing for this suspect.
    /// </summary>
    private void HandleBoothBecameReady()
    {
        _suspectRingPermanentlyStopped = true;

        if (_suspectRingCoroutine != null)
        {
            StopCoroutine(_suspectRingCoroutine);
            _suspectRingCoroutine = null;
        }
    }

    /// <summary>
    /// Rings the bell every <see cref="_suspectRingInterval"/> seconds on all clients
    /// until <see cref="_suspectRingPermanentlyStopped"/> is set.
    /// </summary>
    private IEnumerator SuspectRingCoroutine()
    {
        while (!_suspectRingPermanentlyStopped)
        {
            yield return new WaitForSeconds(_suspectRingInterval);

            if (!_suspectRingPermanentlyStopped)
                PlayBellClientRpc();
        }

        _suspectRingCoroutine = null;
    }

    // -------------------------------------------------------------------------
    // ClientRpc
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void PlayBellClientRpc()
    {
        if (_ringSource != null)
            _ringSource.Play();
    }
}
