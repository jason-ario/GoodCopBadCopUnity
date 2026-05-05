using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DoorController : Interactable
{
    private NetworkVariable<bool> _doorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // True = opened toward the inside (positive dot product side)
    private NetworkVariable<bool> _openedIn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private Animator _animator;
    private bool _beingInteractedWith = false;
    [SerializeField] private float waitDelay = .5f;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip doorOpenClip;
    [SerializeField] AudioClip doorCloseClip;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _doorOpen.OnValueChanged += OnDoorStateChanged;
        _openedIn.OnValueChanged += OnOpenDirectionChanged;

        // Sync visual state on late join
        ApplyDoorVisuals(_doorOpen.Value, _openedIn.Value);
    }

    public override void OnNetworkDespawn()
    {
        _doorOpen.OnValueChanged -= OnDoorStateChanged;
        _openedIn.OnValueChanged -= OnOpenDirectionChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (!_beingInteractedWith)
        {
            StartCoroutine(WaitAndToggleDoor(player));
        }
    }

    private IEnumerator WaitAndToggleDoor(PlayerInteractionController player)
    {
        _beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();

        // Determine open direction before the delay so prediction is correct.
        Vector3 doorForward = transform.forward;
        Vector3 playerToDoor = transform.position - player.transform.position;
        bool openedIn = Vector3.Dot(doorForward, playerToDoor) > 0f;
        bool willBeOpen = !_doorOpen.Value;

        if (willBeOpen)
        {
            PlayDoorSoundClientRpc(true);
        }

        yield return new WaitForSeconds(waitDelay);

        // Apply visuals immediately on the interacting client — no RTT wait.
        ApplyDoorVisuals(willBeOpen, openedIn);
        if (!willBeOpen)
        {
            audioSource.PlayOneShot(doorCloseClip);
        }

        ToggleDoorServerRpc(openedIn, NetworkManager.Singleton.LocalClientId);

        yield return new WaitForSeconds(waitDelay);
        _beingInteractedWith = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDoorServerRpc(bool openedIn, ulong senderClientId)
    {
        if (_doorOpen.Value)
        {
            _doorOpen.Value = false;
        }
        else
        {
            _openedIn.Value = openedIn;
            _doorOpen.Value = true;
        }

        // Broadcast visuals to all clients except the one that already predicted.
        BroadcastDoorStateClientRpc(_doorOpen.Value, _openedIn.Value, senderClientId);
    }

    /// <summary>
    /// Applies door visuals on all clients except the one that predicted it locally.
    /// </summary>
    [ClientRpc]
    private void BroadcastDoorStateClientRpc(bool isOpen, bool openedIn, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        ApplyDoorVisuals(isOpen, openedIn);
        if (!isOpen)
        {
            audioSource.PlayOneShot(doorCloseClip);
        }
    }

    [ClientRpc]
    private void PlayDoorSoundClientRpc(bool opening)
    {
        audioSource.PlayOneShot(opening ? doorOpenClip : doorCloseClip);
    }

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastDoorStateClientRpc.
        interactText = newValue ? "Close" : "Open";
    }

    private void OnOpenDirectionChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastDoorStateClientRpc.
    }

    private void ApplyDoorVisuals(bool isOpen, bool openedIn)
    {
        _animator.SetBool("OpenedIn", isOpen && openedIn);
        _animator.SetBool("OpenedOut", isOpen && !openedIn);
        interactText = isOpen ? "Close" : "Open";
    }

    public void Reset()
    {
        if (!IsServer) return;
        _doorOpen.Value = false;
        _openedIn.Value = false;
    }
}