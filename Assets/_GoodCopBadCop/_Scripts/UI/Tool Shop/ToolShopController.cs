using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class ToolShopController : MonoBehaviour
{
    /// <summary>The currently active <see cref="ToolShopController"/> instance.</summary>
    public static ToolShopController Instance { get; private set; }

    /// <summary>
    /// Fired after the shop item views are ready (each time the shop screen becomes active).
    /// Subscribe to this to apply tutorial state such as showing arrows or locking the back button.
    /// </summary>
    public static event System.Action OnShopOpened;

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

    /// <summary>The back button in the shop screen. Toggle with <see cref="SetBackButtonActive"/>.</summary>
    public GameObject backButton;

    private ShopItem _selectedShopItem;
    private bool _initialized;

    private static readonly string HoldingObjectMessage = "Put down what you're holding first!";
    private static readonly string PurchaseSuccessMessage = "Item purchased!";

    private void OnEnable()
    {
        Instance = this;

        // Fire on every subsequent activation (Start already fired once).
        if (_initialized)
        {
            // Re-read prices in case a price override was applied since the last open.
            foreach (ShopItemView view in shopItemViews)
                view.RefreshPrice();

            FadeIn();
            OnShopOpened?.Invoke();
        }
    }

    private void OnDisable()
    {
        _canvasGroup.DOKill();
        if (Instance == this)
            Instance = null;
    }

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

        _initialized = true;
        OnShopOpened?.Invoke();
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

        bool shouldClose = customAction == null || customAction.CloseShopOnPurchase;
        if (shouldClose)
            UIController.Instance.CloseToolShopUI();
    }

    /// <summary>Shows or hides the shop's back button.</summary>
    public void SetBackButtonActive(bool active)
    {
        if (backButton != null)
            backButton.SetActive(active);
    }

    /// <summary>
    /// Locks all shop item views except those whose <see cref="ShopItem"/> is in
    /// <paramref name="allowedItems"/>, greying them out and making them non-interactable.
    /// </summary>
    public void SetItemsLockedExcept(params ShopItem[] allowedItems)
    {
        var allowed = new System.Collections.Generic.HashSet<ShopItem>(allowedItems);
        foreach (var view in shopItemViews)
            view.SetLocked(!allowed.Contains(view.ShopItem));
    }

    /// <summary>Removes the locked state from all shop item views.</summary>
    public void UnlockAllItems()
    {
        foreach (var view in shopItemViews)
            view.SetLocked(false);
    }

    /// <summary>Returns the <see cref="ShopItemView"/> that was created for <paramref name="item"/>, or null if not found.</summary>
    public ShopItemView GetViewForItem(ShopItem item)
    {
        if (item == null) return null;
        foreach (var view in shopItemViews)
        {
            if (view.ShopItem == item)
                return view;
        }
        return null;
    }

    /// <summary>
    /// Refreshes the displayed price for a specific shop item while the shop is open.
    /// Also updates the buy button text when the given item is currently selected.
    /// Call this after a price override is applied or cleared so the open UI stays in sync.
    /// </summary>
    public void RefreshPriceForItem(ShopItem item)
    {
        GetViewForItem(item)?.RefreshPrice();

        if (_selectedShopItem == item)
            _buyText.text = "Buy " + "<sprite=0>" + item.Price;
    }

    private PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
