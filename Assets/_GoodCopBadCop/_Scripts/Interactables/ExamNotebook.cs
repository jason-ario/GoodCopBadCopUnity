using System.Collections;
using HighlightPlus;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class ExamNotebook : PickableObject
{
    [SerializeField] private ExamPage[] pages;
    bool addingToFolder = false;
    public bool IsChecking { get; set; }

    private int currentPage = 0;


    protected override void Awake()
    {
        base.Awake();
        foreach (var page in pages)
        {
            page.SetChecklistInteractable(false);
            page.SetInteractable(false);
            page.Initialize(this);
        }

        pages[currentPage].SetChecklistInteractable(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Pages are scene-hierarchy children of the notebook NetworkObject, so NGO's
        // AutoObjectParentSync keeps them under the correct parent on all clients.
        // Their own NetworkTransform would independently interpolate world-space position,
        // causing them to lag behind the notebook on non-server clients. Disable it here
        // on every client — it will be re-evaluated (and left disabled) by PlaceInSlotServerRpc
        // when a page is eventually ripped out and slotted into the folder.
        foreach (var page in pages)
        {
            NetworkTransform nt = page.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = false;
        }
    }

    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);

        foreach (var itemHeld in  player.RightArmCamObjectContainer.ItemsHeld)
        {
            if (itemHeld.ItemData.name == "RedPencil")
            {
                itemHeld.gameObject.SetActive(true);
            }
        }
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);
        foreach (var itemHeld in  player.RightArmCamObjectContainer.ItemsHeld)
        {
            if (itemHeld.ItemData.name == "RedPencil")
            {
                itemHeld.gameObject.SetActive(false);
            }
        }
    }

    public override void OnStartUse()
    {
        if (addingToFolder)
        {
            return;
        }
        
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        playerPickupController.CanPickUpAndPlace = false;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(false);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(false);
        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(ExitDrawMode);
    }

    void ExitDrawMode()
    {
        playerPickupController.CanPickUpAndPlace = true;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(true);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(true);
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();
    }

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        IsChecking = true;
        playerPickupController.PlayerAnimationController.RightArmRigIKTarget = ikAnimationTarget;
        StartCoroutine(TurnRightArmRigOff());
    }

    IEnumerator TurnRightArmRigOff()
    {
        playerPickupController.PlayerAnimationController.TurnRightArmRigOnAndOff(.25f, .5f);
        yield return new WaitForSeconds(.5f);
        IsChecking = false;
    }

    public void AddToFolder(FolderController folder)
    {
        addingToFolder = true;
        pages[currentPage].SetChecklistInteractable(false);

        playerPickupController.PlayerAnimationController.SetAnimTrigger("RipOutPage");
        StartCoroutine(WaitAndParent(pages[currentPage],folder));
        Debug.Log("Rip out and add to folder");
    }

    IEnumerator WaitAndParent(ExamPage rippedPage, FolderController folder)
    {
        yield return new WaitForSeconds(.5f);
        rippedPage.pageAnimator.SetTrigger("RipOut");
        yield return new WaitForSeconds(.4f);

        // Place the page directly into the folder slot via the network-safe path.
        // FolderController.AddDocument (dropObject=false) calls PlaceInSlotServerRpc so
        // NT is disabled and all clients register the document in LateUpdate — no local
        // hand-parent step needed, which was only visible on the owner anyway.
        folder.AddDocument(rippedPage, playerPickupController, false);

        GetComponent<HighlightEffect>().SetupMaterial();
        rippedPage.pageAnimator.SetTrigger("Reset");
        currentPage += 1;
        pages[currentPage].SetChecklistInteractable(true);
        addingToFolder = false;
    }
}
