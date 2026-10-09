using Unity.VisualScripting;
using UnityEngine;

public class TrashCan : Interactable
{
    private const string DocumentationCategory = "Documentation";
    private const string DocumentationTutorialIncompleteMessage =
        "Can't throw away yet";
    private const string SupplyBoxNotEmptyMessage = "Empty the box first";
    private const string FolderNotEmptyMessage = "Empty the folder first";
    private const string SuspectStillAtWindowMessage = "Deliver a verdict first";
    private const string ConfirmDiscardMessage = "Are you sure you want to throw this away?";

    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip throwTrashSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    /// <summary>Interact (empty-handed) does nothing here — held items are thrown away with E via InteractWithItem.</summary>
    public override bool ShowsInteractPrompt(PlayerInteractionController player) => false;

    /// <summary>
    /// Folders are accepted by type (no inspector list entry needed); non-empty ones are
    /// rejected with an error notification (negative sound, same as "Trash is full") in
    /// <see cref="InteractWithItem"/>, like supply boxes.
    /// </summary>
    public override bool CanInteractWithItem(PickableObject item)
    {
        return base.CanInteractWithItem(item) || item is FolderController;
    }

    public override string GetItemUseVerb(PlayerInteractionController player, PickableObject item) => "Throw away";

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);

        if (!CanThrowAway(item)) return;

        // Ask first. If the dialog can't open (unassigned / already open / paused), don't discard.
        UIController ui = UIController.Instance;
        if (ui == null) return;

        ui.ShowGameplayConfirmation(
            title: "Throw Away",
            body: ConfirmDiscardMessage,
            confirmText: "Yes",
            cancelText: "No",
            onConfirm: () => ConfirmThrowAway(playerInteractionController, item));
    }

    private void ConfirmThrowAway(PlayerInteractionController player, PickableObject item)
    {
        // State may have changed while the popup was open (item dropped/despawned, verdict...).
        if (player == null || item == null || player.pickupController == null) return;
        if (player.pickupController.HeldObject != item) return;
        if (!CanThrowAway(item)) return;

        player.pickupController.DestroyEquippedItem();
        audioSource.PlayOneShot(throwTrashSound);
    }

    /// <summary>
    /// All discard rules. Shows the matching error notification and returns false when the
    /// item can't be thrown away right now.
    /// </summary>
    private bool CanThrowAway(PickableObject item)
    {
        if (item is FolderItem folderItem && folderItem.BelongsToUnresolvedSuspectAtWindow())
        {
            UIController.Instance?.ShowErrorNotification(SuspectStillAtWindowMessage);
            return false;
        }

        if (item is FolderController folder && !folder.IsEmpty())
        {
            UIController.Instance?.ShowErrorNotification(FolderNotEmptyMessage);
            return false;
        }

        if (item is SupplyBox supplyBox)
        {
            var remaining = new System.Collections.Generic.List<string>();
            if (supplyBox.GetItemsStillInBox(remaining) > 0)
            {
                Debug.Log($"[TrashCan] Supply box not empty — still contains: {string.Join(", ", remaining)}", this);
                UIController.Instance?.ShowErrorNotification(SupplyBoxNotEmptyMessage);
                return false;
            }
        }

        if (item is ExamNotebook examNotebook &&
            examNotebook.CategoryName == DocumentationCategory &&
            Day_01.Instance != null &&
            !Day_01.Instance.DocumentationExamTutorialComplete)
        {
            UIController.Instance?.ShowShopNotification(DocumentationTutorialIncompleteMessage);
            return false;
        }

        return true;
    }
}
