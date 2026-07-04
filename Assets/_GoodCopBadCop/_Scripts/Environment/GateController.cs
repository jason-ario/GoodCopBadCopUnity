using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshObstacle))]
public class GateController : Interactable, IMutantPassable, IHeldItemPassthrough
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

    [SerializeField] private Animator _animator;
    private bool _beingInteractedWith = false;
    [SerializeField] private float waitDelay = .5f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip doorOpenClip;
    [SerializeField] private AudioClip doorCloseClip;
    [SerializeField] private Transform forwardMarker;
    private NavMeshObstacle _navMeshObstacle;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _navMeshObstacle = GetComponent<NavMeshObstacle>();
        _gateOpen.OnValueChanged += OnGateStateChanged;
        _openedIn.OnValueChanged += OnOpenDirectionChanged;

        // Sync visual state on late join.
        ApplyGateVisuals(_gateOpen.Value, _openedIn.Value);
    }

    public override void OnNetworkDespawn()
    {
        _gateOpen.OnValueChanged -= OnGateStateChanged;
        _openedIn.OnValueChanged -= OnOpenDirectionChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (!_beingInteractedWith)
            StartCoroutine(WaitAndToggleGate(player));
    }

    private IEnumerator WaitAndToggleGate(PlayerInteractionController player)
    {
        _beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();

        // Determine open direction before the delay so local prediction is correct.
        Vector3 doorForward = forwardMarker.forward;
        Vector3 playerToDoor = transform.position - player.transform.position;
        bool openedIn = Vector3.Dot(doorForward, playerToDoor) > 0f;
        bool willBeOpen = !_gateOpen.Value;

        if (willBeOpen)
            PlayGateSoundClientRpc(true);

        // Apply visuals immediately on the interacting client — no pre-visual delay.
        ApplyGateVisuals(willBeOpen, openedIn);
        if (!willBeOpen)
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
        if (!isOpen)
            audioSource.PlayOneShot(doorCloseClip);
    }

    [ClientRpc]
    private void PlayGateSoundClientRpc(bool opening)
    {
        audioSource.PlayOneShot(opening ? doorOpenClip : doorCloseClip);
    }

    private void OnGateStateChanged(bool oldValue, bool newValue)
    {
        // Used to keep interactText consistent for late-joining clients.
    }

    private void OnOpenDirectionChanged(bool oldValue, bool newValue) { }

    private void ApplyGateVisuals(bool isOpen, bool openedIn)
    {
        _animator.SetBool("OpenedIn", isOpen && openedIn);
        _animator.SetBool("OpenedOut", isOpen && !openedIn);

        if (_navMeshObstacle != null)
            _navMeshObstacle.enabled = !isOpen;
    }

    /// <summary>Resets the gate to its closed state. Must be called on the server.</summary>
    public void Reset()
    {
        if (!IsServer) return;
        _gateOpen.Value = false;
        _openedIn.Value = false;
        ForceCloseVisualsClientRpc();
    }

    [ClientRpc]
    private void ForceCloseVisualsClientRpc()
    {
        ApplyGateVisuals(false, false);
        audioSource.PlayOneShot(doorCloseClip);
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
        Reset();
    }

    // ── IMutantPassable ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsBlockingMutant => !_gateOpen.Value;

    /// <inheritdoc/>
    public void OpenForMutant()
    {
        if (!IsServer) return;
        OpenGate();
        Debug.Log($"[GateController] Gate '{gameObject.name}' forced open by mutant.");
    }
}
