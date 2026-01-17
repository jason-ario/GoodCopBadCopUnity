using System.Collections;
using DG.Tweening;
using UnityEngine;

public class FolderController : Interactable
{
    public bool inFolderPos;
    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] Animator anim;
    private bool isStamping;
    public Transform stampUpTarget;
    public Transform stampDownTarget;
    [SerializeField] StampContainer stampContainer;
    [SerializeField] private AudioClip stampSound;
    PlayerPickupController playerPickupController;
    
    public override void Interact(PlayerInteractionController player)
    {
        if (inFolderPos == false)
        {
            MoveToFolderPos();
        }
        else
        {
            ToggleFolderOpen();
        }
    }

    void ToggleFolderOpen()
    {
        anim.SetBool("Open", !anim.GetBool("Open"));
    }

    void MoveToFolderPos()
    {
        inFolderPos = true;
        transform.DOJump(GameManager.Instance.FolderPos.position, .3f, 1, .5f);
        SFXController.Instance.Play(folderPlaceClip);
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
        
        MoveToFolderOriginalPos();
    }

    void MoveToFolderOriginalPos()
    {
        transform.DOJump(SuspectController.Instance.ApplicationSpawnPos.position, .3f, 1, .5f);
        SFXController.Instance.Play(folderPlaceClip);
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
