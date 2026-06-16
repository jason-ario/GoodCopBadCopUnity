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
    private bool _selectedItemIsLocked;
    private bool _initialized;
    private CanvasGroup _buyButtonCanvasGroup;

    private static readonly string HoldingObjectMessage = "Put down what you're holding first!";
    private static readonly string PurchaseSuccessMessage = "Item purchased!";
    private static readonly string LockedButtonLabel = "Locked";
    private const float BuyButtonLockedAlpha = 0.45f;

    private void OnEnable()
    {
        Instance = this;

        // Fire on every subsequent activation (Start already fired once).
        if (_initialized)
        {
            // Re-read prices in case a price override was applied since the last open.
            foreach (ShopItemView view in shopItemViews)
                view.RefreshPrice();

            // Re-apply availability in case an item was unlocked while the shop was closed.
            ApplyAvailabilityFromSave();
            SortViewsByAvailability();

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

        // Lazily create the CanvasGroup used to dim the buy button when a locked item is selected.
        if (_buyText != null)
        {
            var buyButtonRoot = _buyText.transform.parent;
            _buyButtonCanvasGroup = buyButtonRoot.GetComponent<CanvasGroup>()
                                    ?? buyButtonRoot.gameObject.AddComponent<CanvasGroup>();
        }
        
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

        ApplyAvailabilityFromSave();
        SortViewsByAvailability();
        shopItemViews[0].SelectShopItem();

        _initialized = true;
        OnShopOpened?.Invoke();
    }

    private void FadeIn()
    {
        _canvasGroup.alpha = 0;
        _canvasGroup.DOFade(1, .5f);
    }

    public void Select(ShopItem shopItem, bool isLocked = false)
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

        bool isUnavailable = !shopItem.IsAvailable;
        _selectedShopItem = shopItem;
        _selectedItemIsLocked = isLocked || isUnavailable;

        _itemPreviewSpawner.SpawnAndFrame(shopItem, isUnavailable);
        itemPreviewText.text = isUnavailable ? "???" : shopItem.Name;

        if (isUnavailable)
        {
            _buyText.text = "???";
            if (_buyButtonCanvasGroup != null)
                _buyButtonCanvasGroup.alpha = BuyButtonLockedAlpha;
        }
        else if (isLocked)
        {
            _buyText.text = LockedButtonLabel;
            if (_buyButtonCanvasGroup != null)
                _buyButtonCanvasGroup.alpha = BuyButtonLockedAlpha;
        }
        else
        {
            _buyText.text = "Buy " + "<sprite=0>" + shopItem.Price;
            if (_buyButtonCanvasGroup != null)
                _buyButtonCanvasGroup.alpha = 1f;
        }
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

        if (_selectedItemIsLocked)
        {
            Debug.Log($"ToolShopController: Cannot purchase '{_selectedShopItem.Name}' — item is locked.");
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
    /// Also updates the buy button text when the given item is currently selected and not locked.
    /// Call this after a price override is applied or cleared so the open UI stays in sync.
    /// </summary>
    public void RefreshPriceForItem(ShopItem item)
    {
        GetViewForItem(item)?.RefreshPrice();

        if (_selectedShopItem == item && !_selectedItemIsLocked)
            _buyText.text = "Buy " + "<sprite=0>" + item.Price;
    }

    /// <summary>
    /// Refreshes the availability display for a specific shop item and, if it is currently
    /// selected, re-triggers selection so the preview and buy button also update.
    /// Call this after <see cref="ShopItem.SetAvailable"/> is applied at runtime.
    /// </summary>
    public void RefreshItemAvailability(ShopItem item)
    {
        var view = GetViewForItem(item);
        view?.RefreshAvailability();

        // Re-sort so the newly-available item moves back to its original position.
        SortViewsByAvailability();

        // Re-select so the preview and buy button reflect the new state.
        if (_selectedShopItem == item)
            Select(item, view != null && view.IsLocked);
    }

    /// <summary>
    /// Reads each shop item's unlock state from <see cref="SaveDataManager"/> and calls
    /// <see cref="ShopItem.SetAvailable"/> for items whose save data says they are unlocked.
    /// Items with <c>_unlockedByDefault = true</c> are already available and are skipped.
    /// </summary>
    private void ApplyAvailabilityFromSave()
    {
        if (SaveDataManager.Instance == null) return;

        foreach (var view in shopItemViews)
        {
            ShopItem item = view.ShopItem;
            if (item == null || item.IsAvailable) continue;

            if (SaveDataManager.Instance.IsShopItemUnlocked(item.Name))
            {
                item.SetAvailable(true);
                view.RefreshAvailability();
            }
        }
    }

    /// <summary>
    /// Reorders the shop item view GameObjects in the scroll list so that available items
    /// appear first (in their original Inspector order) and unavailable items follow at the
    /// end (also in their original relative order).
    /// <para>
    /// The <see cref="shopItemViews"/> list itself is never reordered — it always reflects
    /// the canonical Inspector order and is used as the stable source of truth when a newly
    /// unlocked item needs to return to its correct position.
    /// </para>
    /// </summary>
    private void SortViewsByAvailability()
    {
        int siblingIndex = 0;

        // Pass 1 — available items, in original Inspector order.
        foreach (var view in shopItemViews)
        {
            if (view.ShopItem != null && view.ShopItem.IsAvailable)
                view.transform.SetSiblingIndex(siblingIndex++);
        }

        // Pass 2 — unavailable items, in original Inspector order.
        foreach (var view in shopItemViews)
        {
            if (view.ShopItem == null || !view.ShopItem.IsAvailable)
                view.transform.SetSiblingIndex(siblingIndex++);
        }
    }

    private PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
