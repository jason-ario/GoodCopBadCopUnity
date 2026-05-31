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

    /// <summary>The <see cref="ShopItem"/> this view was initialized with.</summary>
    public ShopItem ShopItem => _shopItem;

    public void Initialize(ShopItem shopItem, ToolShopController toolShopController)
    {
        shopItemName.text = shopItem.Name;
        shopItemPrice.text = "<sprite=0>" + shopItem.Price;
        _toolShopController = toolShopController;
        _shopItem = shopItem;
    }

    public void SelectShopItem()
    {
        _toolShopController.Select(_shopItem);
        _anim.SetBool("Selected", true);
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
