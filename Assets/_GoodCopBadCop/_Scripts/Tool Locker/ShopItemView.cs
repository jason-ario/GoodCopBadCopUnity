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

    private const float LockedAlpha = 0.35f;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>The <see cref="ShopItem"/> this view was initialized with.</summary>
    public ShopItem ShopItem => _shopItem;

    public void Initialize(ShopItem shopItem, ToolShopController toolShopController)
    {
        shopItemName.text = shopItem.Name;
        shopItemPrice.text = "<sprite=0>" + shopItem.Price;
        _toolShopController = toolShopController;
        _shopItem = shopItem;
    }

    /// <summary>
    /// Re-reads the price from the bound <see cref="ShopItem"/> and updates the displayed text.
    /// Call this after a price override is applied so the UI stays in sync.
    /// </summary>
    public void RefreshPrice()
    {
        if (_shopItem != null)
            shopItemPrice.text = "<sprite=0>" + _shopItem.Price;
    }

    public void SelectShopItem()
    {
        if (_isLocked) return;
        _toolShopController.Select(_shopItem);
        _anim.SetBool("Selected", true);
    }

    /// <summary>
    /// Greys out this item and prevents interaction when <paramref name="locked"/> is true.
    /// Restores full visibility and interactability when false.
    /// </summary>
    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = locked ? LockedAlpha : 1f;
        _canvasGroup.interactable = !locked;
        _canvasGroup.blocksRaycasts = !locked;
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
