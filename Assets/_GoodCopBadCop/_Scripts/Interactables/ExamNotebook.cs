using System.Collections;
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
            page.SetInteractable(false);
        }

        pages[currentPage].SetInteractable(true);
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
        UIController.Instance.ShowBackButton(ExitDrawMode);
    }

    void ExitDrawMode()
    {
        playerPickupController.CanPickUpAndPlace = true;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(true);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(true);
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
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
        pages[currentPage].SetInteractable(false);

        playerPickupController.PlayerAnimationController.SetAnimTrigger("RipOutPage");
        StartCoroutine(WaitAndParent(pages[currentPage],folder));
        Debug.Log("Rip out and add to folder");
    }

    IEnumerator WaitAndParent(ExamPage rippedPage, FolderController folder)
    {
        yield return new WaitForSeconds(.5f);
        rippedPage.pageAnimator.SetTrigger("RipOut");
        yield return new WaitForSeconds(.3f);
        rippedPage.transform.parent = playerPickupController.RightArmCamObjectContainer.transform;
        yield return new WaitForSeconds(.1f);
        folder.AddNotebookDocumentToSlot(ItemData.name, rippedPage);
        rippedPage.pageAnimator.SetTrigger("Reset");
        currentPage += 1;
        pages[currentPage].SetInteractable(true);
        addingToFolder = false;
    }
}
