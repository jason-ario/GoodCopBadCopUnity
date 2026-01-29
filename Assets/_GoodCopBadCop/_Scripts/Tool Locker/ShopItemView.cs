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
}
