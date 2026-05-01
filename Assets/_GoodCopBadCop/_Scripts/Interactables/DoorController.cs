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

        if (!_doorOpen.Value)
        {
            PlayDoorSoundClientRpc(true);
        }

        yield return new WaitForSeconds(waitDelay);

        // Determine open direction from the interacting player's position
        Vector3 doorForward = transform.forward;
        Vector3 playerToDoor = transform.position - player.transform.position;
        bool openedIn = Vector3.Dot(doorForward, playerToDoor) > 0f;

        ToggleDoorServerRpc(openedIn);

        yield return new WaitForSeconds(waitDelay);
        _beingInteractedWith = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDoorServerRpc(bool openedIn)
    {
        if (_doorOpen.Value)
        {
            _doorOpen.Value = false;
            PlayDoorSoundClientRpc(false);
        }
        else
        {
            _openedIn.Value = openedIn;
            _doorOpen.Value = true;
        }
    }

    [ClientRpc]
    private void PlayDoorSoundClientRpc(bool opening)
    {
        audioSource.PlayOneShot(opening ? doorOpenClip : doorCloseClip);
    }

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        ApplyDoorVisuals(newValue, _openedIn.Value);
        interactText = newValue ? "Close" : "Open";
    }

    private void OnOpenDirectionChanged(bool oldValue, bool newValue)
    {
        if (_doorOpen.Value)
        {
            ApplyDoorVisuals(true, newValue);
        }
    }

    private void ApplyDoorVisuals(bool isOpen, bool openedIn)
    {
        _animator.SetBool("OpenedIn", isOpen && openedIn);
        _animator.SetBool("OpenedOut", isOpen && !openedIn);
    }

    public void Reset()
    {
        if (!IsServer) return;
        _doorOpen.Value = false;
        _openedIn.Value = false;
    }
}