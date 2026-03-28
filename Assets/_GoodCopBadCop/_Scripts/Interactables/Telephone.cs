using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;

public class Telephone : Interactable
{
    [SerializeField] private ParentConstraint _handSet;
    [SerializeField] private Transform _ikTarget;
    [SerializeField] Transform _camera;
    private NetworkVariable<bool> isGrabbed = new NetworkVariable<bool>(false);
    [SerializeField] private Transform _handsetPos;
    [SerializeField] private AudioSource phoneSound;
    [SerializeField] AudioClip phoneGrabSound;
    [SerializeField] AudioClip phonePlaceSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        // Interact
        base.Interact(player);

        if (isGrabbed.Value == false)
        {
            StartCoroutine(GrabPhoneSequence(player));
        }
        else
        {
            StartCoroutine(PutPhoneDownSequence(player));
        }
    }
    
    IEnumerator PutPhoneDownSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        
        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmRigIKTarget = _ikTarget;

        Vector3 cameraPos = player.playerMovementController.CameraTransform.localPosition;
        Vector3 cameraRot = player.playerMovementController.CameraTransform.localEulerAngles;
        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f); 
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        
        phoneSound.PlayOneShot(phonePlaceSound);
        
        player.playerAnimationController.SetAnimBool("HoldingPhone", false);
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f,.25f);
        
        yield return new WaitForSeconds(.5f);
        player.playerAnimationController.DisableLeftArmMask();
        _handSet.enabled = false;
        _handSet.constraintActive = false;
        _handSet.transform.position = _handsetPos.position;
        _handSet.transform.rotation = _handsetPos.rotation;
        player.playerMovementController.ResetCameraPos(false,.25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmRigIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        interactText = "Pick Up";
        isGrabbed.Value = false;
    }
    
    
    IEnumerator GrabPhoneSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        
        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmRigIKTarget = _ikTarget;

        Vector3 cameraPos = player.playerMovementController.CameraTransform.localPosition;
        Vector3 cameraRot = player.playerMovementController.CameraTransform.localEulerAngles;
        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f); 
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        player.playerAnimationController.EnableLeftArmMask();
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f,.25f);
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
        
        player.playerMovementController.ResetCameraPos(false,.25f);
        
        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmRigIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        interactText = "Put Down";
        isGrabbed.Value = true;
    }
    
}
