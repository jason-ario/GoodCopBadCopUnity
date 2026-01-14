using System.Collections;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;

public class PaperPickup : PickableObject
{
    private bool isStamping;
    public Transform stampUpTarget;
    public Transform stampDownTarget;
    [SerializeField] StampContainer stampContainer;
    [SerializeField] private AudioClip stampSound;

    public override void OnStartUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        UIController.Instance.OpenNewspaper();
    }
    
    public override void OnStopUse()
    {
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        UIController.Instance.CloseNewspaper();
    }
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData heldItem)
    {
        playerPickupController = playerInteractionController.GetComponent<PlayerPickupController>();
        StartCoroutine(UseStamp(heldItem.PickUpPrefab.GetComponent<InkStampPickup>()));
    }
    
    IEnumerator UseStamp(InkStampPickup inkStamp)
    {
        if (isStamping) yield break;
        PlayerInstance.Instance.CanControl = false;
        isStamping = true;
        playerPickupController.PlayerAnimationController.SetAnimTrigger("UseStamp");
        playerPickupController.PlayerAnimationController.ArmRig.weight = 1;
        playerPickupController.GetComponent<PlayerMovementController>().LookAtTarget(transform);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);
        StartCoroutine(LerpRigOnAndOff());
        yield return new WaitForSeconds(.5f); 
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(stampDownTarget.rotation.eulerAngles, .25f);
        SFXController.Instance.Play(stampSound);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampDownTarget.position, .25f).OnComplete(() => PlaceStamp(inkStamp));
        yield return new WaitForSeconds(.25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        yield return new WaitForSeconds(.5f);
        PlayerInstance.Instance.CanControl = true;
        yield return new WaitForSeconds(.5f);
        playerPickupController.PlayerAnimationController.ArmRig.weight = 0;

        isStamping = false;
    }

    void PlaceStamp(InkStampPickup inkStamp)
    {
        stampContainer.PlaceStamp(inkStamp.StampType);
    }

    IEnumerator LerpRigOnAndOff()
    {
        float upDuration = 1f;
        float downDuration = 0.6f;
        float elapsed = 0f;

        // Phase 1: Lerp Up to 1
        while (elapsed < upDuration)
        {
            elapsed += Time.deltaTime;
            playerPickupController.PlayerAnimationController.ArmRig.weight = Mathf.Lerp(0, 1, elapsed / upDuration);
            yield return null;
        }
        playerPickupController.PlayerAnimationController.ArmRig.weight = 1;

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < downDuration)
        {
            elapsed += Time.deltaTime;
            playerPickupController.PlayerAnimationController.ArmRig.weight = Mathf.Lerp(1, 0, elapsed / downDuration);
            yield return null;
        }
        playerPickupController.PlayerAnimationController.ArmRig.weight = 0;
    }
}
