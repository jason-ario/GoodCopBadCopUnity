using System.Collections;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

public class SwitchButton : Interactable
{
    [SerializeField] private AudioSource buttonPressSound;
    [SerializeField] private Animator anim;
    [SerializeField] Transform ikTarget;
    [SerializeField] private CinemachineCamera _camera;
    public bool buttonReady = false;

    protected override void Awake()
    {
        base.Awake();
        ShiftManager.Instance.OnShiftReady += OnShiftReady;
    }

    void OnShiftReady()
    {
        SetReady(true);
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        StartCoroutine(PressButtonSequence(player));
    }

    IEnumerator PressButtonSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        
        player.playerAnimationController.RightArmRigIKTarget = ikTarget;
        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f); 
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        
        yield return new WaitForSeconds(.5f);
        player.playerAnimationController.EnableRightArmMask();
        player.playerAnimationController.TurnRightArmRigOnAndOff(.2f,.5f);
        player.playerAnimationController.SetAnimTrigger("PressButton");

        if (buttonReady)
        {
            SetReady(false);

            if (ShiftManager.Instance.shiftStarted.Value == false)
            {
                ShiftManager.Instance.TryStartShift();
            }
            else
            {
                SuspectController.Instance.NextSuspect();
            }
        }
        
        PlayButtonSoundClientRpc();

        yield return new WaitForSeconds(1);
        player.playerMovementController.ResetCameraPos(false,.25f);

        
        yield return new WaitForSeconds(.25f);

        player.playerAnimationController.DisableRightArmMask();
        player.playerMovementController.SetCanControl(true);
    }

    [ClientRpc]
    private void PlayButtonSoundClientRpc()
    {
        if (buttonPressSound != null)
        {
            buttonPressSound.Play();
        }
        
        anim.SetTrigger("Push");
    }

    public void SetReady(bool b)
    {
        anim.SetBool("Ready", b);
        buttonReady = b;
    }
}
