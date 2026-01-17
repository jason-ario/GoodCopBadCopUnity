using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

public class FolderController : Interactable
{
    private NetworkVariable<bool> inFolderPos = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);

    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] Animator anim;
    private bool isStamping;
    public Transform stampUpTarget;
    public Transform stampDownTarget;
    [SerializeField] StampContainer stampContainer;
    [SerializeField] private AudioClip stampSound;
    PlayerPickupController playerPickupController;

    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        inFolderPos.OnValueChanged += (oldVal, newVal) => HandlePositionChange(newVal);
        isOpen.OnValueChanged += (oldVal, newVal) => anim.SetBool("Open", newVal);

        // Set initial state
        anim.SetBool("Open", isOpen.Value);
        if (inFolderPos.Value)
        {
            transform.position = GameManager.Instance.FolderPos.position;
        }
    }

    private void HandlePositionChange(bool isInFolderPos)
    {
        if (isInFolderPos)
        {
            transform.DOJump(GameManager.Instance.FolderPos.position, .3f, 1, .5f);
        }
        else
        {
            transform.DOJump(SuspectController.Instance.ApplicationSpawnPos.position, .3f, 1, .5f);
        }

        SFXController.Instance.Play(folderPlaceClip);
    }

    public override void Interact(PlayerInteractionController player)
    {
        InteractServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractServerRpc()
    {
        if (inFolderPos.Value == false)
        {
            inFolderPos.Value = true;
        }
        else
        {
            isOpen.Value = !isOpen.Value;
        }
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController,
        PickableItemData heldItem)
    {
        // Stamping sequence involves local player control locking and IK, 
        // so we trigger the visual sequence on all clients via RPC.
        ulong clientId = playerInteractionController.NetworkObjectId;
        var inkStamp = heldItem.PickUpPrefab.GetComponent<InkStampPickup>();
        if (isOpen.Value)
        {
            InteractServerRpc();
            return;
        }

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
        isStamping = true;

        // Only lock controls for the local player who is actually interacting
        bool isLocal = playerPickupController.IsLocalPlayer;
        if (isLocal) PlayerInstance.Instance.CanControl = false;

        playerPickupController.PlayerAnimationController.SetAnimTrigger("UseStamp");
        playerPickupController.PlayerAnimationController.ArmRig.weight = 1;
        playerPickupController.GetComponent<PlayerMovementController>().LookAtTarget(transform);

        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);

        StartCoroutine(LerpRigOnAndOff());
        yield return new WaitForSeconds(.5f);

        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(
            stampDownTarget.rotation.eulerAngles, .25f);
        SFXController.Instance.Play(stampSound);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampDownTarget.position, .25f)
            .OnComplete(() =>
            {
                // Only server handles logic state changes
                if (IsServer) stampContainer.PlaceStamp(stampType);
            });

        yield return new WaitForSeconds(.25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.ArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        yield return new WaitForSeconds(.5f);

        if (isLocal) PlayerInstance.Instance.CanControl = true;

        yield return new WaitForSeconds(.5f);
        playerPickupController.PlayerAnimationController.ArmRig.weight = 0;
        isStamping = false;

        if (IsServer) inFolderPos.Value = false;
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