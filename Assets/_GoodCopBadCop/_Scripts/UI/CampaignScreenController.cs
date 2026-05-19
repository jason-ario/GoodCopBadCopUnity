using UnityEngine;

/// <summary>
/// Manages the campaign slot-selection screen.
/// Displays the three save slots and routes the player into the pre-game lobby
/// once a slot is chosen.
/// </summary>
public class CampaignScreenController : MonoBehaviour
{
    [SerializeField] private CampaignSlot[] slots;

    private void OnEnable()
    {
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
}
