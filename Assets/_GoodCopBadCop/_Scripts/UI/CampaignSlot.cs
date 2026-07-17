using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a single campaign save slot on the campaign selection screen.
/// Populates itself from <see cref="SaveDataManager"/> and notifies
/// <see cref="CampaignScreenController"/> when the player interacts with it.
/// </summary>
public class CampaignSlot : MonoBehaviour, IPointerEnterHandler
{
    [Header("Layout")]
    [SerializeField] private GameObject emptySlotContainer;
    [SerializeField] private GameObject occupiedSlotContainer;

    [Header("Occupied Slot UI")]
    [SerializeField] private TextMeshProUGUI slotNameText;
    [SerializeField] private TextMeshProUGUI dayNumberText;
    [SerializeField] private TextMeshProUGUI cashAmountText;
    [SerializeField] private TextMeshProUGUI lastSavedText;
    [SerializeField] private Button deleteButton;

    [Header("Slot Index")]
    [SerializeField] private int slotIndex;

    [Header("Audio")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;

    private CampaignScreenController _screen;

    // ---------------------------------------------------------------------------
    // Initialisation
    // ---------------------------------------------------------------------------

    /// <summary>Called by <see cref="CampaignScreenController"/> after instantiation.</summary>
    public void Initialise(CampaignScreenController screen, int index)
    {
        _screen = screen;
        slotIndex = index;
        Refresh();
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>Re-reads save data and refreshes displayed values.</summary>
    public void Refresh()
    {
        SaveSlot slot = SaveDataManager.Instance.GetSlot(slotIndex);
        bool occupied = slot != null && slot.IsOccupied;

        emptySlotContainer.SetActive(!occupied);
        occupiedSlotContainer.SetActive(occupied);

        if (!occupied)
            return;

        slotNameText.text = slot.SlotName;
        dayNumberText.text = $"Day {slot.CurrentDay + 1}";
        cashAmountText.text = $"${slot.TotalCashEarned:N0}";
        lastSavedText.text = slot.LastSaved != default
            ? slot.LastSaved.ToLocalTime().ToString("MMM d, yyyy")
            : string.Empty;
    }

    // ---------------------------------------------------------------------------
    // Button Handlers (wire up in the Inspector)
    // ---------------------------------------------------------------------------

    /// <summary>Called by the slot's main button — selects this slot and proceeds.</summary>
    public void OnSlotSelected()
    {
        SFXController.Instance?.Play(clickClip);
        SaveDataManager.Instance.SelectSlot(slotIndex);
        _screen?.OnSlotChosen(slotIndex);
    }

    /// <summary>Called by the delete button — deletes save data after confirmation.</summary>
    public void OnDeletePressed()
    {
        SFXController.Instance?.Play(clickClip);
        _screen?.RequestDeleteSlot(slotIndex);
    }

    /// <summary>Plays the hover sound when the pointer enters the slot.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        SFXController.Instance?.Play(hoverClip);
    }
}
