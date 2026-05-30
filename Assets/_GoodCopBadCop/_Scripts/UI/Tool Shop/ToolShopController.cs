using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class ToolShopController : MonoBehaviour
{
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private ItemPreviewSpawner _itemPreviewSpawner;
    [SerializeField] private ShopItem[] shopItems;
    [SerializeField] ShopItemView shopItemViewPrefab;
    [SerializeField] private Transform shopItemViewContainer;
    [SerializeField] TextMeshProUGUI itemPreviewText;
    private List<ShopItemView> shopItemViews = new List<ShopItemView>();
    [SerializeField] private TextMeshProUGUI _buyText;

    /// <summary>Transform used as the world spawn point when purchasing an item.</summary>
    [SerializeField] private Transform _itemSpawnPoint;

    private ShopItem _selectedShopItem;

    private static readonly string HoldingObjectMessage = "Put down what you're holding first!";
    private static readonly string PurchaseSuccessMessage = "Item purchased!";

    private void Start()
    {
        FadeIn();
        
        for (var i = 0; i < shopItems.Length; i++)
        {
            var shopItem = shopItems[i];
            if (shopItem == null) continue;

            var shopItemView = Instantiate(shopItemViewPrefab, shopItemViewContainer);
            shopItemView.Initialize(shopItem, this);
            shopItemViews.Add(shopItemView);
        }

        foreach (var shopItemView in shopItemViews)
        {
            shopItemView.Deselect();
        }

        shopItemViews[0].SelectShopItem();
    }

    private void OnDisable()
    {
        _canvasGroup.DOKill();
    }

    private void FadeIn()
    {
        _canvasGroup.alpha = 0;
        _canvasGroup.DOFade(1, .5f);
    }

    public void Select(ShopItem shopItem)
    {
        foreach (var shopItemView in shopItemViews)
        {
            shopItemView.Deselect();
        }
        
        if (shopItem == null)
        {
            Debug.LogWarning("ToolShopController: Attempted to select a null or destroyed ShopItem.");
            return;
        }

        _selectedShopItem = shopItem;
        _itemPreviewSpawner.SpawnAndFrame(shopItem);
        itemPreviewText.text = shopItem.Name;
        _buyText.text = "Buy " + "<sprite=0>" + shopItem.Price;
    }

    /// <summary>
    /// Attempts to purchase the currently selected shop item.
    /// Call this from the Buy button's OnClick event.
    /// </summary>
    public void Buy()
    {
        if (_selectedShopItem == null)
        {
            Debug.LogWarning("ToolShopController: Buy called with no item selected.");
            return;
        }

        PlayerPickupController pickup = GetLocalPlayerPickup();
        if (pickup == null)
        {
            Debug.LogError("ToolShopController: Could not find local PlayerPickupController.");
            return;
        }

        ShopPurchaseAction customAction = _selectedShopItem.CustomPurchaseAction;

        if (customAction != null)
        {
            // Custom action path — no prefab spawned.
            if (customAction.RequiresEmptyHands && pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value < _selectedShopItem.Price)
            {
                UIController.Instance.ShowShopNotification("Not enough coupons!");
                return;
            }

            customAction.Execute(pickup, _selectedShopItem.Price);
        }
        else
        {
            // Default path — spawn and pick up a pickable prefab.
            if (pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value < _selectedShopItem.Price)
            {
                UIController.Instance.ShowShopNotification("Not enough coupons!");
                return;
            }

            Transform spawnPoint = _itemSpawnPoint != null ? _itemSpawnPoint : pickup.transform;
            pickup.PurchaseAndPickUp(_selectedShopItem.pickableItemData, _selectedShopItem.Price, spawnPoint);
        }

        UIController.Instance.ShowShopNotification(PurchaseSuccessMessage);
        UIController.Instance.CloseToolShopUI();
    }

    private PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
