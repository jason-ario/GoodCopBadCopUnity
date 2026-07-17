using UnityEngine;

/// <summary>
/// Manages the campaign slot-selection screen.
/// Displays the three save slots and routes the player into the pre-game lobby
/// once a slot is chosen.
/// </summary>
public class CampaignScreenController : MonoBehaviour
{
    private const string DeleteSaveTitle = "DELETE ASSIGNMENT?";
    private const string DeleteSaveBodyFormat = "This will permanently delete Save {0}. This cannot be undone.";
    private const string DeleteSaveConfirmText = "Delete";
    private const string DeleteSaveCancelText = "Cancel";
    [SerializeField] private CampaignSlot[] slots;
    [SerializeField] private ConfirmationDialogController confirmationOverlay;


private void OnEnable()
{
    confirmationOverlay?.Hide();
    RefreshAllSlots();
}

    // ---------------------------------------------------------------------------
    // Slot Management
    // ---------------------------------------------------------------------------

    private void RefreshAllSlots()
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i].Initialise(this, i);
    }

    /// <summary>
    /// Called by <see cref="CampaignSlot"/> when the player confirms a slot.
    /// Creates a lobby (so another player can join) and proceeds to the pre-game lobby screen.
    /// </summary>
    public void OnSlotChosen(int slotIndex)
    {
        Debug.Log($"[CampaignScreenController] Slot {slotIndex} chosen.");
        MainMenuController.Instance.StartNewGame();
    }

    /// <summary>
    /// Opens the campaign-specific confirmation overlay before destructive save operations.
    /// The overlay stays generic: its text and callbacks are supplied by this caller.
    /// </summary>
    public void RequestDeleteSlot(int slotIndex)
    {
        if (confirmationOverlay == null)
        {
            Debug.LogError("[CampaignScreenController] Confirmation Overlay is not assigned.", this);
            return;
        }

        confirmationOverlay.Show(
            DeleteSaveTitle,
            string.Format(DeleteSaveBodyFormat, slotIndex + 1),
            DeleteSaveConfirmText,
            DeleteSaveCancelText,
            () => DeleteSlot(slotIndex));
    }

    private void DeleteSlot(int slotIndex)
    {
        SaveDataManager.Instance.DeleteSlot(slotIndex);
        RefreshAllSlots();
    }
}
