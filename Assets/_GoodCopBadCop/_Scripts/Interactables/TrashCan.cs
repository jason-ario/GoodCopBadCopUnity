using Unity.VisualScripting;
using UnityEngine;

public class TrashCan : Interactable
{
    private const string DocumentationCategory = "Documentation";
    private const string DocumentationTutorialIncompleteMessage =
        "Can't throw away yet";
    private const string SupplyBoxNotEmptyMessage = "Empty the box first";

    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip throwTrashSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);

        if (item is SupplyBox supplyBox)
        {
            var remaining = new System.Collections.Generic.List<string>();
            if (supplyBox.GetItemsStillInBox(remaining) > 0)
            {
                Debug.Log($"[TrashCan] Supply box not empty — still contains: {string.Join(", ", remaining)}", this);
                UIController.Instance?.ShowShopNotification(SupplyBoxNotEmptyMessage);
                return;
            }
        }

        if (item is ExamNotebook examNotebook &&
            examNotebook.CategoryName == DocumentationCategory &&
            Day_01.Instance != null &&
            !Day_01.Instance.DocumentationExamTutorialComplete)
        {
            UIController.Instance?.ShowShopNotification(DocumentationTutorialIncompleteMessage);
            return;
        }

        playerInteractionController.pickupController.DestroyEquippedItem();
        audioSource.PlayOneShot(throwTrashSound);
    }
}
