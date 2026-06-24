using System;
using System.Collections.Generic;
using Unity.Cinemachine;
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

    [Header("Item Zoom Camera")]
    [Tooltip("Secondary CinemachineCamera that activates and zooms in on the selected shop item when the purchase popup is open.")]
    [SerializeField] private CinemachineCamera _itemZoomCamera;

    // ─── Runtime state ───────────────────────────────────────────────────────

    private ToolsLocker _locker;
    private IHoverable _lastHoverable;
    private bool _popupOpen;

    /// <summary>Freezes camera panning while the purchase popup is open.</summary>
    protected override bool SuppressCameraMovement => _popupOpen;

    /// <summary>
    /// When the popup is open, Q dismisses only the popup.
    /// When no popup is open, Q closes the entire diegetic view as normal.
    /// </summary>
    protected override void OnExitKeyPressed()
    {
        if (_popupOpen)
            CloseItemPopup();
        else
            Close();
    }

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

    private void SetAllItemsHighlightBlocked(bool blocked)
    {
        foreach (ShopItem item in _shopItems)
            item?.SetHighlightBlocked(blocked);
    }

    /// <summary>
    /// Positions <see cref="_itemZoomCamera"/> to frame <paramref name="item"/> and activates it,
    /// causing Cinemachine to blend from the locker camera into the item close-up.
    /// </summary>
    private void ActivateItemZoomCamera(ShopItem item)
    {
        if (_itemZoomCamera == null) return;

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        Camera rayCam = RaycastCamera;
        if (rayCam == null) return;

        // Place the zoom camera along the current view direction toward the item centre.
        Vector3 dir = (bounds.center - rayCam.transform.position).normalized;
        float fovRad  = _itemZoomCamera.Lens.FieldOfView * Mathf.Deg2Rad;
        float radius   = bounds.extents.magnitude * 1.5f;
        float distance = radius / Mathf.Sin(fovRad * 0.5f);

        _itemZoomCamera.transform.position = bounds.center - dir * distance;
        _itemZoomCamera.transform.LookAt(bounds.center);
        _itemZoomCamera.gameObject.SetActive(true);
    }

    private void DeactivateItemZoomCamera()
    {
        if (_itemZoomCamera != null)
            _itemZoomCamera.gameObject.SetActive(false);
    }

    private void ShowPrompt(ShopItem item)
    {
        _cursorPrompt?.Show($"{item.Name}  <sprite=0>  {item.Price}");
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
        SetAllItemsHighlightBlocked(true);
        ActivateItemZoomCamera(item);
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(CloseItemPopup);
        UIController.Instance.OpenShopItemPurchasePopup(item, () => OnPopupBuyConfirmed(item), CloseItemPopup);
    }

    private void CloseItemPopup()
    {
        _popupOpen = false;
        SetAllItemsHighlightBlocked(false);
        DeactivateItemZoomCamera();
        UIController.Instance.CloseShopItemPurchasePopup();
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(Close);
    }

    private void OnPopupBuyConfirmed(ShopItem item)
    {
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

    /// <summary>
    /// Re-enables all shop items that were hidden by purchases.
    /// Called via ClientRpc at the start of each new day so the locker is fully restocked.
    /// </summary>
    public void RestockItems()
    {
        foreach (ShopItem item in _shopItems)
        {
            if (item == null) continue;
            item.SetAvailable(true);
            item.gameObject.SetActive(true);
        }
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
