using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopItemView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI shopItemName;
    [SerializeField] TextMeshProUGUI shopItemPrice;
    ToolShopController _toolShopController;
    private ShopItem _shopItem;
    [SerializeField] private Animator _anim;
    public GameObject arrow;

    private CanvasGroup _canvasGroup;
    private bool _isLocked;

    private const float LockedAlpha = 0.55f;
    private const string UnavailableLabel = "???";

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>The <see cref="ShopItem"/> this view was initialized with.</summary>
    public ShopItem ShopItem => _shopItem;

    /// <summary>Whether this item is currently in the tutorial-locked state.</summary>
    public bool IsLocked => _isLocked;

    public void Initialize(ShopItem shopItem, ToolShopController toolShopController)
    {
        _shopItem = shopItem;
        _toolShopController = toolShopController;
        RefreshAvailability();
    }

    /// <summary>
    /// Re-reads availability from the bound <see cref="ShopItem"/> and updates the name and price display.
    /// Shows '???' for both when the item is not yet available to the player.
    /// </summary>
    public void RefreshAvailability()
    {
        if (_shopItem == null) return;
        bool available = _shopItem.IsAvailable;
        shopItemName.text = available ? _shopItem.Name : UnavailableLabel;
        shopItemPrice.text = available ? "<sprite=0>" + _shopItem.Price : UnavailableLabel;
    }

    /// <summary>
    /// Re-reads the price from the bound <see cref="ShopItem"/> and updates the displayed text.
    /// No-ops when the item is unavailable (price display stays as '???').
    /// Call this after a price override is applied so the UI stays in sync.
    /// </summary>
    public void RefreshPrice()
    {
        if (_shopItem == null) return;
        if (!_shopItem.IsAvailable) return;
        shopItemPrice.text = "<sprite=0>" + _shopItem.Price;
    }

    public void SelectShopItem()
    {
        _toolShopController.Select(_shopItem, _isLocked);
        _anim.SetBool("Selected", true);
    }

    /// <summary>
    /// Greys out this item when <paramref name="locked"/> is true — dims the whole card
    /// so it reads as unavailable, while keeping it interactable so the player can still
    /// click it and see the "Locked" state on the buy button.
    /// Restores full visibility when false.
    /// </summary>
    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = locked ? LockedAlpha : 1f;
    }

    public void Deselect()
    {
        _anim.SetBool("Selected", false);
    }

    /// <summary>Shows or hides the tutorial arrow on this shop item row.</summary>
    public void SetArrowVisible(bool visible)
    {
        if (arrow != null)
            arrow.SetActive(visible);
    }
}
