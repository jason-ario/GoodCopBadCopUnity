using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Diegetic view for the tool locker. Extends <see cref="DiegeticViewController"/> with
/// shop-specific logic: responding to <see cref="ShopItem"/> hover/click events dispatched
/// by <see cref="ClickDetector"/>, displaying a purchase prompt, and executing the buy flow.
/// </summary>
public class ToolLockerDiegeticController : DiegeticViewController
{
    [Header("Shop Items")]
    [Tooltip("All shop items physically placed inside the locker that the player can buy.")]
    [SerializeField] private ShopItem[] _shopItems;

    [Tooltip("The locker's own collider — disabled while the diegetic view is open so it doesn't block item raycasts.")]
    [SerializeField] private Collider _lockerCollider;

    [Header("UI")]
    [Tooltip("Cursor-following prompt shown when hovering a purchasable item. Optional.")]
    [SerializeField] private CursorPromptController _cursorPrompt;

    // ─── Runtime state ───────────────────────────────────────────────────────

    private ToolsLocker _locker;
    private IHoverable _lastHoverable;
    private bool _popupOpen;

    /// <summary>Cached delegates so we can unsubscribe cleanly on close.</summary>
    private readonly Dictionary<ShopItem, (Action hovered, Action unhovered, Action clicked)> _subs = new();

    // ─── Constants ───────────────────────────────────────────────────────────

    private const string HoldingObjectMessage  = "Put down what you're holding first!";
    private const string PurchaseSuccessMessage = "Item purchased!";
    private const string NotEnoughMoneyMessage  = "Not enough coupons!";

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the locker diegetic view for <paramref name="player"/>.
    /// Stores the <paramref name="locker"/> reference so it can be notified on close.
    /// </summary>
    public void Open(PlayerInteractionController player, ToolsLocker locker)
    {
        _locker = locker;
        base.Open(player);
    }

    // ─── DiegeticViewController hooks ────────────────────────────────────────

    protected override void OnOpened()
    {
        if (_lockerCollider != null)
            _lockerCollider.enabled = false;

        // Start hidden — shown on hover, hidden on exit
        _cursorPrompt?.Hide();

        UIController.OnPauseMenuOpened += CloseItemPopup;

        foreach (ShopItem item in _shopItems)
        {
            if (item == null) continue;

            ShopItem captured = item;
            Action hovered   = () => ShowPrompt(captured);
            Action unhovered = ClearPrompt;
            Action clicked   = () => ShowItemPopup(captured);

            item.Hovered   += hovered;
            item.Unhovered += unhovered;
            item.Clicked   += clicked;

            _subs[item] = (hovered, unhovered, clicked);
        }
    }

    protected override void OnClosed()
    {
        UIController.OnPauseMenuOpened -= CloseItemPopup;

        ClearHover();

        foreach (var (item, subs) in _subs)
        {
            item.Hovered   -= subs.hovered;
            item.Unhovered -= subs.unhovered;
            item.Clicked   -= subs.clicked;
        }
        _subs.Clear();

        _cursorPrompt?.Hide();

        UIController.Instance.CloseShopItemPurchasePopup();

        if (_locker != null)
        {
            _locker.NotifyPlayerClosedServerRpc();
            _locker = null;
        }

        if (_lockerCollider != null)
            _lockerCollider.enabled = true;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Each frame while the locker is open: raycast from the cursor through the
    /// scene camera to fire <see cref="IHoverable"/> and <see cref="IClickable"/> events
    /// on shop items. Skipped while the purchase popup is open so UI buttons take priority.
    /// Cursor tracking for the prompt is handled by <see cref="CursorPromptController"/> itself.
    /// </summary>
    protected override void OnUpdate()
    {
        if (_popupOpen) return;

        Camera cam = RaycastCamera;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide);

        IHoverable hoverable = didHit ? hit.collider.GetComponentInParent<IHoverable>() : null;
        if (hoverable != _lastHoverable)
        {
            _lastHoverable?.OnHoverExit();
            hoverable?.OnHoverEnter();
            _lastHoverable = hoverable;
        }

        if (Input.GetMouseButtonDown(0) && didHit)
            hit.collider.GetComponentInParent<IClickable>()?.OnClick();
    }

    private void ClearHover()
    {
        _lastHoverable?.OnHoverExit();
        _lastHoverable = null;
    }

    private void ShowPrompt(ShopItem item)
    {
        _cursorPrompt?.Show($"{item.Name}  <sprite=0>{item.Price}");
    }

    private void ClearPrompt()
    {
        _cursorPrompt?.Hide();
    }

    private void ShowItemPopup(ShopItem item)
    {
        ClearHover();
        ClearPrompt();
        _popupOpen = true;
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(CloseItemPopup);
        UIController.Instance.OpenShopItemPurchasePopup(item.Name, item.Price, () => OnPopupBuyConfirmed(item));
    }

    private void CloseItemPopup()
    {
        _popupOpen = false;
        UIController.Instance.CloseShopItemPurchasePopup();
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(Close);
    }

    private void OnPopupBuyConfirmed(ShopItem item)
    {
        CloseItemPopup();
        TryPurchase(item);
    }

    private void TryPurchase(ShopItem item)
    {
        if (item == null || !item.IsAvailable) return;

        PlayerPickupController pickup = GetLocalPlayerPickup();
        if (pickup == null)
        {
            Debug.LogError("ToolLockerDiegeticController: Could not find local PlayerPickupController.");
            return;
        }

        ShopPurchaseAction customAction = item.CustomPurchaseAction;

        if (customAction != null)
        {
            if (customAction.RequiresEmptyHands && pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (!HasEnoughMoney(item.Price)) return;

            customAction.Execute(pickup, item.Price);
        }
        else
        {
            if (pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (!HasEnoughMoney(item.Price)) return;

            pickup.PurchaseAndPickUp(item.pickableItemData, item.Price, pickup.holdPoint);
        }

        UIController.Instance.ShowShopNotification(PurchaseSuccessMessage);

        bool shouldClose = customAction == null || customAction.CloseShopOnPurchase;
        if (shouldClose)
        {
            DespawnItem(item);
            Close();
        }
    }

    /// <summary>
    /// Broadcasts a hide request to all clients via the locker's ServerRpc,
    /// then the ClientRpc calls <see cref="HideItem"/> on every machine.
    /// </summary>
    private void DespawnItem(ShopItem item)
    {
        int index = GetItemIndex(item);
        if (index < 0 || _locker == null) return;
        _locker.HideShopItemServerRpc(index);
    }

    /// <summary>
    /// Hides the shop item at <paramref name="itemIndex"/> and marks it unavailable.
    /// Called via ClientRpc on every client so the physical item disappears for everyone.
    /// </summary>
    public void HideItem(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= _shopItems.Length) return;
        ShopItem item = _shopItems[itemIndex];
        if (item == null) return;
        item.SetAvailable(false);
        item.gameObject.SetActive(false);
    }

    private int GetItemIndex(ShopItem item)
    {
        for (int i = 0; i < _shopItems.Length; i++)
            if (_shopItems[i] == item) return i;
        return -1;
    }

    private bool HasEnoughMoney(int price)
    {
        if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value < price)
        {
            UIController.Instance.ShowShopNotification(NotEnoughMoneyMessage);
            return false;
        }

        return true;
    }

    private static PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
