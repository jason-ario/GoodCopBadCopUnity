using Unity.VisualScripting;
using UnityEngine;

public class TrashCan : Interactable
{
    private const string DocumentationCategory = "Documentation";
    private const string DocumentationTutorialIncompleteMessage =
        "Finish the documentation tutorial before throwing this away.";

    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip throwTrashSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);

        if (item is SupplyBox supplyBox && !supplyBox.IsEmpty)
        {
            return;
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
