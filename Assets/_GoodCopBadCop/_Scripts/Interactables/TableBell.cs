using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A networked table bell the player can ring by interacting with it.
///
/// Two modes of operation:
///
/// 1. SUMMON MODE — when <see cref="ShiftManager.NextSuspectReadyForBell"/> is set, the bell
///    rings periodically (<see cref="_bellReadyRingInterval"/>) to alert the player that a
///    suspect is waiting in line. Interacting with it calls
///    <see cref="SuspectController.NextSuspect"/>, spawning the next suspect.
///
/// 2. WAITING MODE — when a suspect has already arrived at the booth window and no player is
///    present, the bell rings periodically (<see cref="_suspectRingInterval"/>) to prompt the
///    player to approach. Ringing stops once a player enters the booth.
/// </summary>
public class TableBell : Interactable
{
    [SerializeField] private AudioSource _ringSource;

    [Tooltip("How often (seconds) the bell auto-rings while waiting for the player to call the next suspect.")]
    [SerializeField] private float _bellReadyRingInterval = 15f;

    [Tooltip("How often (seconds) the bell auto-rings while a suspect is waiting at the booth window with no player present.")]
    [SerializeField] private float _suspectRingInterval = 10f;

    private Coroutine _bellReadyCoroutine;
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

        ShiftManager.OnNextSuspectReadyForBell  += HandleNextSuspectReady;
        SuspectController.OnSuspectWaitingAtBooth += HandleSuspectWaiting;
        SuspectController.OnBoothBecameReady      += HandleBoothBecameReady;
    }

    public override void OnNetworkDespawn()
    {
        ShiftManager.OnNextSuspectReadyForBell    -= HandleNextSuspectReady;
        SuspectController.OnSuspectWaitingAtBooth -= HandleSuspectWaiting;
        SuspectController.OnBoothBecameReady      -= HandleBoothBecameReady;
    }

    // -------------------------------------------------------------------------
    // Player interaction
    // -------------------------------------------------------------------------

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsServer)
            HandleInteractServer();
        else
            RequestInteractServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractServerRpc()
    {
        HandleInteractServer();
    }

    /// <summary>
    /// Server-side interaction logic. If a suspect is ready to be called, summons them;
    /// otherwise just rings the bell for feedback.
    /// </summary>
    private void HandleInteractServer()
    {
        if (ShiftManager.NextSuspectReadyForBell)
        {
            StopBellReadyCoroutine();
            SuspectController.Instance.NextSuspect();
        }

        PlayBellClientRpc();
    }

    // -------------------------------------------------------------------------
    // Summon-mode ringing (server-side) — bell rings to indicate next suspect is ready
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called on the server when the shift signals the next suspect can be summoned.
    /// Begins a periodic ringing coroutine so the player knows to ring the bell.
    /// </summary>
    private void HandleNextSuspectReady()
    {
        StopBellReadyCoroutine();
        _bellReadyCoroutine = StartCoroutine(BellReadyCoroutine());
    }

    private IEnumerator BellReadyCoroutine()
    {
        while (ShiftManager.NextSuspectReadyForBell)
        {
            yield return new WaitForSeconds(_bellReadyRingInterval);

            if (ShiftManager.NextSuspectReadyForBell)
                PlayBellClientRpc();
        }

        _bellReadyCoroutine = null;
    }

    private void StopBellReadyCoroutine()
    {
        if (_bellReadyCoroutine == null) return;
        StopCoroutine(_bellReadyCoroutine);
        _bellReadyCoroutine = null;
    }

    // -------------------------------------------------------------------------
    // Suspect-at-window ringing (server-side) — suspect arrived but booth not ready
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
