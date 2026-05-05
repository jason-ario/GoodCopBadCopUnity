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

    private NetworkVariable<bool> _openedIn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
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

    [Header("Lock")]
    [SerializeField] private MachineShake _machineShake;
    [SerializeField] private AudioSource _lockedDoorAudio;
    [SerializeField] private AudioClip _doorShakeClip;
    [SerializeField] private string[] _tryToLeaveTutorialText;
    [Tooltip("When enabled, this door automatically locks when the shift starts and unlocks when it ends.")]
    [SerializeField] private bool _lockDuringShift = false;

    [Header("Unlock")]
    [SerializeField] private AudioClip _unlockSound;
    [Tooltip("Optional visual toggled when the door unlocks (e.g. a green indicator light).")]
    [SerializeField] private GameObject _unlockedIndicator;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _doorOpen.OnValueChanged += OnDoorStateChanged;
        _openedIn.OnValueChanged += OnOpenDirectionChanged;
        _isLocked.OnValueChanged += OnLockedStateChanged;

        if (_lockDuringShift)
        {
            ShiftManager.Instance.OnDoorLock += ForceCloseAndLock;
            ShiftManager.Instance.OnDoorUnlock += Unlock;
        }

        // Sync visual state on late join
        ApplyDoorVisuals(_doorOpen.Value, _openedIn.Value);
        ApplyLockedVisuals(_isLocked.Value);
    }

    public override void OnNetworkDespawn()
    {
        _doorOpen.OnValueChanged -= OnDoorStateChanged;
        _openedIn.OnValueChanged -= OnOpenDirectionChanged;
        _isLocked.OnValueChanged -= OnLockedStateChanged;

        if (_lockDuringShift && ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnDoorLock -= ForceCloseAndLock;
            ShiftManager.Instance.OnDoorUnlock -= Unlock;
        }
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_isLocked.Value)
        {
            if (!_beingInteractedWith)
                StartCoroutine(TryToOpenLockedDoor());
            return;
        }

        if (!_beingInteractedWith)
        {
            StartCoroutine(WaitAndToggleDoor(player));
        }
    }

    private IEnumerator TryToOpenLockedDoor()
    {
        _beingInteractedWith = true;

        if (_lockedDoorAudio != null && _doorShakeClip != null)
            _lockedDoorAudio.PlayOneShot(_doorShakeClip);

        if (_machineShake != null)
            _machineShake.isRunning = true;

        yield return new WaitForSeconds(0.7f);

        if (_machineShake != null)
            _machineShake.isRunning = false;

        if (_tryToLeaveTutorialText != null && _tryToLeaveTutorialText.Length > 0)
            TutorialManager.Instance.ShowTutorialText(_tryToLeaveTutorialText[UnityEngine.Random.Range(0, _tryToLeaveTutorialText.Length)]);

        _beingInteractedWith = false;
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

    private void OnLockedStateChanged(bool oldValue, bool newValue)
    {
        ApplyLockedVisuals(newValue);
    }

    private void ApplyDoorVisuals(bool isOpen, bool openedIn)
    {
        _animator.SetBool("OpenedIn", isOpen && openedIn);
        _animator.SetBool("OpenedOut", isOpen && !openedIn);
        interactText = isOpen ? "Close" : "Open";
    }

    private void ApplyLockedVisuals(bool isLocked)
    {
        if (_unlockedIndicator != null)
            _unlockedIndicator.SetActive(!isLocked);
    }

    public void Reset()
    {
        if (!IsServer) return;
        _doorOpen.Value = false;
        _openedIn.Value = false;
    }

    /// <summary>
    /// Forces the door shut and locks it. Subscribed to ShiftManager.OnDoorLock.
    /// </summary>
    public void ForceCloseAndLock()
    {
        if (!IsServer) return;
        Reset();
        Lock();
    }

    /// <summary>
    /// Locks the door on all clients. Only callable from the server.
    /// </summary>
    public void Lock()
    {
        if (!IsServer) return;
        _isLocked.Value = true;
    }

    /// <summary>
    /// Unlocks the door on all clients and plays the unlock sound. Only callable from the server.
    /// </summary>
    public void Unlock()
    {
        if (!IsServer) return;
        _isLocked.Value = false;
        PlayUnlockSoundClientRpc();
    }

    [ClientRpc]
    private void PlayUnlockSoundClientRpc()
    {
        if (_unlockSound != null)
            SFXController.Instance.Play(_unlockSound);
    }
}
