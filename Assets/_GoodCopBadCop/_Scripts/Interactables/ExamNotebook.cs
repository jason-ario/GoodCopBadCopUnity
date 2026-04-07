using UnityEngine;

public class ExamNotebook : PickableObject
{
    [SerializeField] private ExamPage[] pages;
    
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
}
