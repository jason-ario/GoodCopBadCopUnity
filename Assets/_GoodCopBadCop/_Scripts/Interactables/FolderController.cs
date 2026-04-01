using System.Collections;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class FolderController : PickableObject
{
    private NetworkVariable<bool> inFolderPos = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isStamped = new NetworkVariable<bool>(false);

    [Header("Documents")] 
    [SerializeField] private MeshRenderer photoID;
    [SerializeField] EntryPermit entryPermit;
    
    [Header("Set Up")]
    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] Animator anim;
    public Transform stampUpTarget;
    public Transform stampDownTarget;
    [SerializeField] StampContainer stampContainer;
    [SerializeField] private AudioClip stampSound;
    private StampContainer.StampType _stampType;
    
    private bool isStamping;

    [Header("Camera")] 
    [SerializeField] private GameObject cinemachineVirtualCamera;
    [SerializeField] private CinemachineImpulseSource _impulseSource;

    [SerializeField] private Transform cameraRigPos;
    public UnityAction onStampedComplete;
    private bool isOpeningOrClosing;

    
    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        isOpen.OnValueChanged += (oldVal, newVal) => anim.SetBool("Open", newVal);

        // Set initial state
        anim.SetBool("Open", isOpen.Value);
        if (inFolderPos.Value)
        {
            transform.position = GameManager.Instance.FolderPos.position;
        }
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (isStamped.Value) return;

        base.Interact(player);
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData heldItem)
    { 
        if (isStamped.Value) return; 
        base.InteractWithItem(playerInteractionController, heldItem);
        
        // Stamping sequence involves local player control locking and IK, 
        // so we trigger the visual sequence on all clients via RPC.
        ulong clientId = playerInteractionController.NetworkObjectId;
        var inkStamp = heldItem.PickUpPrefab.GetComponent<InkStampPickup>();

        StartUseStampServerRpc(clientId, inkStamp.StampType);
    }

    [ServerRpc(RequireOwnership = false)]
    private void StartUseStampServerRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        if (isStamping) return;
        StartUseStampClientRpc(interactingPlayerId, stampType);
    }

    [ClientRpc]
    private void StartUseStampClientRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        // Find the player instance that initiated the stamp
        NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(interactingPlayerId, out var playerObj);
        if (playerObj == null) return;

        playerPickupController = playerObj.GetComponent<PlayerPickupController>();
        StartCoroutine(UseStampSequence(stampType));
    }

    IEnumerator UseStampSequence(StampContainer.StampType stampType)
    {
        GetComponent<HighlightPlus.HighlightEffect>().highlighted = false;
        isStamping = true;
        isStamped.Value = true;
        
        // Only lock controls for the local player who is actually interacting
        bool isLocal = playerPickupController.IsLocalPlayer;
        if (isLocal) PlayerInstance.Instance.CanControl = false;

        if (IsOwner)
        {
            cinemachineVirtualCamera.SetActive(true);
        }
        yield return new WaitForSeconds(.25f);

        playerPickupController.PlayerAnimationController.SetAnimTrigger("UseStamp");
       // playerPickupController.PlayerAnimationController.ArmRig.weight = 1;
        playerPickupController.GetComponent<PlayerMovementController>().LookAtTarget(transform);

        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);

        
        
        
        playerPickupController.PlayerMovementController.CameraTransform.DOMove(cameraRigPos.transform.position, .25f); 
        playerPickupController.PlayerMovementController.CameraTransform.DORotate(cameraRigPos.transform.rotation.eulerAngles, .25f);
        StartCoroutine(LerpRigOnAndOff());
        yield return new WaitForSeconds(.5f);

        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampDownTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampDownTarget.rotation.eulerAngles, .25f);
        SFXController.Instance.Play(stampSound);
     
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampDownTarget.position, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampDownTarget.position, .25f)
            .OnComplete(() =>
            {
                // Only server handles logic state changes
                if (IsServer) stampContainer.PlaceStamp(stampType);
            });

        yield return new WaitForSeconds(.2f);
        _impulseSource.GenerateImpulse();
        yield return new WaitForSeconds(.25f);


        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        yield return new WaitForSeconds(.5f);


        playerPickupController.PlayerAnimationController.RightArmRig.weight = 0;
        playerPickupController.PlayerAnimationController.CamRightArmRig.weight = 0;

        isStamping = false;
        
        yield return new WaitForSeconds(.5f);
        if (IsOwner)
        {
            cinemachineVirtualCamera.SetActive(false);
        }
        if (isLocal) PlayerInstance.Instance.CanControl = true;
        playerPickupController.PlayerMovementController.ResetCameraPos(false, .5f);
        if (IsServer) inFolderPos.Value = false;
        
        transform.DOJump(SuspectController.Instance.ApplicationSpawnPos.position, .3f, 1, .5f)
            .OnComplete(() => GameManager.Instance.DeliveredVertict(stampContainer.Stamp));
        
        SFXController.Instance.Play(folderPlaceClip);
        
        onStampedComplete?.Invoke();
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
            playerPickupController.PlayerAnimationController.RightArmRig.weight = Mathf.Lerp(0, 1, elapsed / upDuration);
            playerPickupController.PlayerAnimationController.CamRightArmRig.weight = Mathf.Lerp(0, 1, elapsed / upDuration);
            yield return null;
        }

        playerPickupController.PlayerAnimationController.RightArmRig.weight = 1;

        // Phase 2: Lerp Down to 0 (Faster)
        elapsed = 0f;
        while (elapsed < downDuration)
        {
            elapsed += Time.deltaTime;
            playerPickupController.PlayerAnimationController.RightArmRig.weight = Mathf.Lerp(1, 0, elapsed / downDuration);
            playerPickupController.PlayerAnimationController.CamRightArmRig.weight = Mathf.Lerp(1, 0, elapsed / downDuration);

            yield return null;
        }

        playerPickupController.PlayerAnimationController.RightArmRig.weight = 0;
    }

    public override void OnStartUse()
    {
        if (isOpeningOrClosing == false && isOpen.Value == false)
        {
            Debug.Log("Open");
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", true);
            StopAllCoroutines();
            StartCoroutine(WaitAndOpen());
        }
        else
        {
            Debug.Log("Close");
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
            StopAllCoroutines();
            isOpen.Value = false;
            anim.SetBool("Open", false);
            isOpeningOrClosing = false;
        }
    }
    
    IEnumerator WaitAndOpen()
    {
        isOpeningOrClosing = true;
        yield return new WaitForSeconds(.4f);
        isOpen.Value = true;
        anim.SetBool("Open", true);
        isOpeningOrClosing = false;
    }

    public void SetInfo(SuspectCharacter suspectCharacter)
    {
        Debug.Log("Setting info");
        photoID.material.mainTexture = suspectCharacter.IDPhoto;
        entryPermit.SetEntryPermit(suspectCharacter.Data.FirstName + " " + suspectCharacter.Data.LastName, suspectCharacter.ReasonForEntry, suspectCharacter.ExpirationDate, suspectCharacter.SealActive);
    }
}