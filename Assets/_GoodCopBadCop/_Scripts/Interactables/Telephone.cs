using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;

public class Telephone : Interactable
{
    [SerializeField] private ParentConstraint _handSet;
    [SerializeField] private Transform _ikTarget;
    [SerializeField] private Transform _camera;
    [SerializeField] private Transform _handsetPos;
    [SerializeField] private AudioSource phoneSound;
    [SerializeField] private AudioClip phoneGrabSound;
    [SerializeField] private AudioClip phonePlaceSound;

    // Only the server writes this; all clients can read it.
    private NetworkVariable<bool> _isGrabbed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Tracks which client is currently holding the phone.
    private NetworkVariable<ulong> _grabbingClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_isGrabbed.Value == false)
        {
            RequestGrabServerRpc(player.OwnerClientId);
        }
        else if (_grabbingClientId.Value == player.OwnerClientId)
        {
            // Only the player currently holding it can put it down.
            RequestPutDownServerRpc(player.OwnerClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestGrabServerRpc(ulong clientId)
    {
        if (_isGrabbed.Value) return;

        _isGrabbed.Value = true;
        _grabbingClientId.Value = clientId;

        ExecuteGrabSequenceClientRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPutDownServerRpc(ulong clientId)
    {
        if (!_isGrabbed.Value || _grabbingClientId.Value != clientId) return;

        _isGrabbed.Value = false;
        _grabbingClientId.Value = ulong.MaxValue;

        ExecutePutDownSequenceClientRpc(clientId);
    }

    [ClientRpc]
    private void ExecuteGrabSequenceClientRpc(ulong clientId)
    {
        PlayerInteractionController player = FindPlayerByClientId(clientId);
        if (player == null) return;

        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;

        if (isLocalPlayer)
        {
            StartCoroutine(GrabPhoneSequence(player));
        }
        else
        {
            // Observers still need the constraint set up so they see the handset move.
            StartCoroutine(ObserverGrabConstraintSequence(player));
        }
    }

    [ClientRpc]
    private void ExecutePutDownSequenceClientRpc(ulong clientId)
    {
        PlayerInteractionController player = FindPlayerByClientId(clientId);
        if (player == null) return;

        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;

        if (isLocalPlayer)
        {
            StartCoroutine(PutPhoneDownSequence(player));
        }
        else
        {
            StartCoroutine(ObserverPutDownConstraintSequence());
        }
    }

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the grab animation timing then
    /// attaches the ParentConstraint to the remote player's hand socket.
    /// </summary>
    private IEnumerator ObserverGrabConstraintSequence(PlayerInteractionController player)
    {
        // Match the two WaitForSeconds(.25f) in GrabPhoneSequence before the constraint is set.
        yield return new WaitForSeconds(.5f);

        ConstraintSource source = new ConstraintSource
        {
            sourceTransform = player.pickupController.LeftHandSocket.transform,
            weight = 1
        };
        _handSet.SetSource(0, source);
        _handSet.enabled = true;
        _handSet.constraintActive = true;
    }

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the put-down animation timing then
    /// detaches the ParentConstraint and resets the handset to its resting position.
    /// </summary>
    private IEnumerator ObserverPutDownConstraintSequence()
    {
        // Match the WaitForSeconds(.5f) in PutPhoneDownSequence before the constraint is cleared.
        yield return new WaitForSeconds(.5f);

        _handSet.enabled = false;
        _handSet.constraintActive = false;
        _handSet.transform.position = _handsetPos.position;
        _handSet.transform.rotation = _handsetPos.rotation;
    }

    /// <summary>
    /// Finds the PlayerInteractionController belonging to the given client ID.
    /// </summary>
    private PlayerInteractionController FindPlayerByClientId(ulong clientId)
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == clientId && client.PlayerObject != null)
            {
                return client.PlayerObject.GetComponent<PlayerInteractionController>();
            }
        }
        return null;
    }

    private IEnumerator PutPhoneDownSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmRigIKTarget = _ikTarget;

        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);

        phoneSound.PlayOneShot(phonePlaceSound);

        player.playerAnimationController.SetAnimBool("HoldingPhone", false);
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f, .25f);

        yield return new WaitForSeconds(.5f);
        player.playerAnimationController.DisableLeftArmMask();
        _handSet.enabled = false;
        _handSet.constraintActive = false;
        _handSet.transform.position = _handsetPos.position;
        _handSet.transform.rotation = _handsetPos.rotation;
        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmRigIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        interactText = "Pick Up";
    }

    private IEnumerator GrabPhoneSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmRigIKTarget = _ikTarget;

        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        player.playerAnimationController.EnableLeftArmMask();
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f, .25f);
        player.playerAnimationController.SetAnimBool("HoldingPhone", true);
        yield return new WaitForSeconds(.25f);

        phoneSound.PlayOneShot(phoneGrabSound);

        yield return new WaitForSeconds(.25f);
        ConstraintSource source = new ConstraintSource();
        source.sourceTransform = player.pickupController.LeftHandSocket.transform;
        source.weight = 1;
        _handSet.SetSource(0, source);
        _handSet.enabled = true;
        _handSet.constraintActive = true;

        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmRigIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        interactText = "Put Down";
    }
}
