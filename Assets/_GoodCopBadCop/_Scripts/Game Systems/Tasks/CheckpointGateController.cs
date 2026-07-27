using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative open/closed state for the checkpoint gate, driven by its own
/// Animator ('IsOpen' bool — see 'checkpoint gate.controller'). Anything that wants to open or
/// close the gate (e.g. <see cref="GateButtonInteractable"/>) goes through
/// <see cref="RequestOpen"/>/<see cref="RequestClose"/>, and anything that needs to react to the
/// gate actually opening (e.g. <see cref="DeliveryTruckController"/> waiting to drive through)
/// subscribes to <see cref="OnGateOpened"/>/<see cref="OnGateClosed"/>.
///
/// Scene setup: attach to the "checkpoint gate" GameObject alongside its Animator. Requires a
/// NetworkObject (place as an in-scene NetworkObject, never despawned).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CheckpointGateController : NetworkBehaviour
{
    [Tooltip("The gate's own Animator (drives the 'IsOpen' bool parameter). Defaults to the Animator on this GameObject.")]
    [SerializeField] private Animator gateAnimator;

    [Tooltip("AudioSource used to play openSfx. Defaults to the AudioSource on this GameObject.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("One-shot sound effect played on every client when the gate opens.")]
    [SerializeField] private AudioClip openSfx;
    [Tooltip("Volume for openSfx.")]
    [SerializeField, Range(0f, 1f)] private float openSfxVolume = 1f;

    [Tooltip("One-shot sound effect played on every client when the gate closes.")]
    [SerializeField] private AudioClip closeSfx;
    [Tooltip("Volume for closeSfx.")]
    [SerializeField, Range(0f, 1f)] private float closeSfxVolume = 1f;

    [Tooltip("Seconds after opening before the gate automatically closes itself.")]
    [SerializeField] private float autoCloseDelay = 5f;

    private static readonly int IsOpenParam = Animator.StringToHash("IsOpen");

    private Coroutine _autoCloseCoroutine;

    private readonly NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Whether the gate is currently open (replicated to all clients).</summary>
    public bool IsOpen => _isOpen.Value;

    /// <summary>Fired on every client (including the host) when the gate transitions to open.</summary>
    public event Action OnGateOpened;

    /// <summary>Fired on every client (including the host) when the gate transitions to closed.</summary>
    public event Action OnGateClosed;

    private void Awake()
    {
        if (gateAnimator == null)
            gateAnimator = GetComponent<Animator>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isOpen.OnValueChanged += OnIsOpenChanged;
        ApplyVisual(_isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isOpen.OnValueChanged -= OnIsOpenChanged;
    }

    /// <summary>
    /// Requests the gate open. Safe to call from any client — routes through a ServerRpc when
    /// not already running on the server. No-ops if the gate is already open.
    /// </summary>
    public void RequestOpen()
    {
        if (IsServer)
            OpenOnServer();
        else
            RequestOpenServerRpc();
    }

    /// <summary>
    /// Requests the gate close. Safe to call from any client — routes through a ServerRpc when
    /// not already running on the server. No-ops if the gate is already closed.
    /// </summary>
    public void RequestClose()
    {
        if (IsServer)
            CloseOnServer();
        else
            RequestCloseServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenServerRpc() => OpenOnServer();

    [ServerRpc(RequireOwnership = false)]
    private void RequestCloseServerRpc() => CloseOnServer();

    private void OpenOnServer()
    {
        if (!IsServer) return;
        _isOpen.Value = true;

        if (_autoCloseCoroutine != null)
            StopCoroutine(_autoCloseCoroutine);
        _autoCloseCoroutine = StartCoroutine(AutoCloseAfterDelay());
    }

    private void CloseOnServer()
    {
        if (!IsServer) return;

        if (_autoCloseCoroutine != null)
        {
            StopCoroutine(_autoCloseCoroutine);
            _autoCloseCoroutine = null;
        }

        _isOpen.Value = false;
    }

    private IEnumerator AutoCloseAfterDelay()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        _autoCloseCoroutine = null;
        CloseOnServer();
    }

    private void OnIsOpenChanged(bool previousValue, bool newValue)
    {
        ApplyVisual(newValue);

        if (newValue)
        {
            PlayOpenSfx();
            OnGateOpened?.Invoke();
        }
        else
        {
            PlayCloseSfx();
            OnGateClosed?.Invoke();
        }
    }

    private void ApplyVisual(bool isOpen)
    {
        if (gateAnimator != null)
            gateAnimator.SetBool(IsOpenParam, isOpen);
    }

    private void PlayOpenSfx()
    {
        if (audioSource != null && openSfx != null)
            audioSource.PlayOneShot(openSfx, openSfxVolume);
    }

    private void PlayCloseSfx()
    {
        if (audioSource != null && closeSfx != null)
            audioSource.PlayOneShot(closeSfx, closeSfxVolume);
    }
}
