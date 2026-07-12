using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a single inventory slot: an item icon and a selection border.
/// Call <see cref="SetItem"/> / <see cref="ClearItem"/> to update the icon, and
/// <see cref="SetSelected"/> to toggle the white equip border.
/// </summary>
public class InventorySlotUI : MonoBehaviour
{
    [Tooltip("Image inside the slot that shows the item's icon sprite.")]
    [SerializeField] private Image itemIconImage;

    [Tooltip("Image rendered as a border overlay when this slot is equipped.")]
    [SerializeField] private Image selectedBorder;

    [Tooltip("Optional label showing the hotkey number (e.g. '1' or '2').")]
    [SerializeField] private TMP_Text hotkeyLabel;

    private void Awake()
    {
        SetSelected(false);
        ClearItem();
    }

    /// <summary>Displays the given item's icon. Pass <c>null</c> to clear.</summary>
    public void SetItem(PickableItemData data)
    {
        if (data != null && data.Icon != null)
        {
            itemIconImage.sprite  = data.Icon;
            itemIconImage.enabled = true;
        }
        else
        {
            ClearItem();
        }
    }

    /// <summary>Hides the item icon.</summary>
    public void ClearItem()
    {
        if (itemIconImage == null) return;
        itemIconImage.sprite  = null;
        itemIconImage.enabled = false;
    }

    /// <summary>Shows or hides the white selection border.</summary>
    public void SetSelected(bool selected)
    {
        if (selectedBorder != null)
            selectedBorder.enabled = selected;
    }
}
