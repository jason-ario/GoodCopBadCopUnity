using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the start-shift gate.
/// Before the intro cutscene completes, interacting opens the start-shift screen.
/// Once the intro cutscene is done (<see cref="ShiftManager.OnShiftReady"/> fires),
/// the gate behaves like a standard gate — toggling open and closed, synced across the network.
/// </summary>
public class GateStartShiftController : Interactable
{
    private NetworkVariable<bool> _gateOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _openedIn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Gate Animation")]
    [SerializeField] private Animator gateAnimator;
    [SerializeField] private Transform forwardMarker;

    [Header("Gate Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip doorOpenClip;
    [SerializeField] private AudioClip doorCloseClip;

    [Header("Gate Settings")]
    [SerializeField] private float waitDelay = 0.5f;

    private bool _introComplete = false;
    private bool _beingInteractedWith = false;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Start Shift";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _gateOpen.OnValueChanged += OnGateStateChanged;
        _openedIn.OnValueChanged += OnOpenDirectionChanged;

        ShiftManager.Instance.OnShiftReady += OnIntroComplete;

        // Sync visual state on late join.
        ApplyGateVisuals(_gateOpen.Value, _openedIn.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _gateOpen.OnValueChanged -= OnGateStateChanged;
        _openedIn.OnValueChanged -= OnOpenDirectionChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady -= OnIntroComplete;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (!_introComplete)
        {
            UIController.Instance.OpenStartShiftScreen();
        }
        else
        {
            if (!_beingInteractedWith)
                StartCoroutine(WaitAndToggleGate(player));
        }
    }

    private IEnumerator WaitAndToggleGate(PlayerInteractionController player)
    {
        _beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();

        // Determine open direction before the delay so local prediction is correct.
        bool openedIn = true;
        if (forwardMarker != null)
        {
            Vector3 doorForward = forwardMarker.forward;
            Vector3 playerToDoor = transform.position - player.transform.position;
            openedIn = Vector3.Dot(doorForward, playerToDoor) > 0f;
        }

        bool willBeOpen = !_gateOpen.Value;

        if (willBeOpen && audioSource != null && doorOpenClip != null)
            PlayGateSoundClientRpc(true);

        yield return new WaitForSeconds(waitDelay);

        // Apply visuals immediately on the interacting client — no RTT wait.
        ApplyGateVisuals(willBeOpen, openedIn);
        if (!willBeOpen && audioSource != null && doorCloseClip != null)
            audioSource.PlayOneShot(doorCloseClip);

        ToggleGateServerRpc(openedIn, NetworkManager.Singleton.LocalClientId);

        yield return new WaitForSeconds(waitDelay);
        _beingInteractedWith = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleGateServerRpc(bool openedIn, ulong senderClientId)
    {
        if (_gateOpen.Value)
        {
            _gateOpen.Value = false;
        }
        else
        {
            _openedIn.Value = openedIn;
            _gateOpen.Value = true;
        }

        // Broadcast to all clients except the one that already predicted locally.
        BroadcastGateStateClientRpc(_gateOpen.Value, _openedIn.Value, senderClientId);
    }

    /// <summary>Applies gate visuals on all clients except the one that predicted locally.</summary>
    [ClientRpc]
    private void BroadcastGateStateClientRpc(bool isOpen, bool openedIn, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        ApplyGateVisuals(isOpen, openedIn);
        if (!isOpen && audioSource != null && doorCloseClip != null)
            audioSource.PlayOneShot(doorCloseClip);
    }

    [ClientRpc]
    private void PlayGateSoundClientRpc(bool opening)
    {
        if (audioSource == null) return;
        audioSource.PlayOneShot(opening ? doorOpenClip : doorCloseClip);
    }

    private void OnGateStateChanged(bool oldValue, bool newValue)
    {
        interactText = newValue ? "Close" : "Open";
    }

    private void OnOpenDirectionChanged(bool oldValue, bool newValue) { }

    private void ApplyGateVisuals(bool isOpen, bool openedIn)
    {
        gateAnimator.SetBool("OpenedIn", isOpen && openedIn);
        gateAnimator.SetBool("OpenedOut", isOpen && !openedIn);
        interactText = isOpen ? "Close" : "Open";
    }

    /// <summary>Opens the gate on all clients. Must be called on the server.</summary>
    public void OpenGate()
    {
        if (!IsServer) return;
        _openedIn.Value = true;
        _gateOpen.Value = true;
        BroadcastGateStateClientRpc(true, true, ulong.MaxValue);
    }

    /// <summary>Closes the gate on all clients. Must be called on the server.</summary>
    public void CloseGate()
    {
        if (!IsServer) return;
        _gateOpen.Value = false;
        _openedIn.Value = false;
        BroadcastGateStateClientRpc(false, false, ulong.MaxValue);
    }

    /// <summary>
    /// Called when the intro cutscene ends. Switches the gate into simple open/close mode.
    /// </summary>
    private void OnIntroComplete()
    {
        _introComplete = true;
        interactText = "Open";
    }
}
