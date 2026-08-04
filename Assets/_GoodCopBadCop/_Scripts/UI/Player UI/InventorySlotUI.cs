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

    [Tooltip("Small label showing the remaining amount when this slot holds an ammo/consumable item (e.g. PistolAmmo, Pill Bottle, Cigarette Pack, Trash Bag Roll). Hidden otherwise.")]
    [SerializeField] private TMP_Text ammoCountText;

    private IAmmoProvider _ammoProvider;

    private void Awake()
    {
        SetSelected(false);
        ClearItem();
    }

    /// <summary>Displays the given item's icon, and its remaining amount if it's an ammo/consumable item. Pass <c>null</c> to clear.</summary>
    public void SetItem(PickableObject item)
    {
        PickableItemData data = item?.ItemData;
        if (data != null && data.Icon != null)
        {
            itemIconImage.sprite  = data.Icon;
            itemIconImage.enabled = true;
        }
        else
        {
            ClearIcon();
        }

        SetAmmoProvider(item as IAmmoProvider);
    }

    /// <summary>Hides the item icon and remaining-amount label.</summary>
    public void ClearItem()
    {
        ClearIcon();
        SetAmmoProvider(null);
    }

    private void ClearIcon()
    {
        if (itemIconImage == null) return;
        itemIconImage.sprite  = null;
        itemIconImage.enabled = false;
    }

    private void SetAmmoProvider(IAmmoProvider provider)
    {
        if (_ammoProvider != null)
            _ammoProvider.OnAmmoChanged -= RefreshAmmoCount;

        _ammoProvider = provider;

        if (_ammoProvider != null)
            _ammoProvider.OnAmmoChanged += RefreshAmmoCount;

        RefreshAmmoCount();
    }

    private void RefreshAmmoCount()
    {
        if (ammoCountText == null) return;

        if (_ammoProvider == null)
        {
            ammoCountText.gameObject.SetActive(false);
            return;
        }

        ammoCountText.gameObject.SetActive(true);
        ammoCountText.text = Mathf.CeilToInt(_ammoProvider.CurrentAmmo).ToString();
    }

    private void OnDestroy() => SetAmmoProvider(null);

    /// <summary>Shows or hides the white selection border.</summary>
    public void SetSelected(bool selected)
    {
        if (selectedBorder != null)
            selectedBorder.enabled = selected;
    }
}
