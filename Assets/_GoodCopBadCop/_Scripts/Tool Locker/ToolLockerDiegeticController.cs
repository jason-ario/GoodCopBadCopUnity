using System;
using System.Collections.Generic;
using TMPro;
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
    [Tooltip("Screen-space TextMeshPro label shown when hovering a purchasable item. Optional.")]
    [SerializeField] private TextMeshProUGUI _interactPrompt;

    // ─── Runtime state ───────────────────────────────────────────────────────

    private ToolsLocker _locker;

    /// <summary>Cached delegates so we can unsubscribe cleanly on close.</summary>
    private readonly Dictionary<ShopItem, (Action hovered, Action unhovered, Action clicked)> _subs = new();

    // ─── Constants ───────────────────────────────────────────────────────────

    private const string HoldingObjectMessage = "Put down what you're holding first!";
    private const string PurchaseSuccessMessage = "Item purchased!";
    private const string NotEnoughMoneyMessage = "Not enough coupons!";

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

        if (_interactPrompt != null)
        {
            _interactPrompt.gameObject.SetActive(true);
            _interactPrompt.text = string.Empty;
        }

        foreach (ShopItem item in _shopItems)
        {
            if (item == null) continue;

            ShopItem captured = item;
            Action hovered   = () => ShowPrompt(captured);
            Action unhovered = ClearPrompt;
            Action clicked   = () => TryPurchase(captured);

            item.Hovered   += hovered;
            item.Unhovered += unhovered;
            item.Clicked   += clicked;

            _subs[item] = (hovered, unhovered, clicked);
        }
    }

    protected override void OnClosed()
    {
        foreach (var (item, subs) in _subs)
        {
            item.Hovered   -= subs.hovered;
            item.Unhovered -= subs.unhovered;
            item.Clicked   -= subs.clicked;
        }
        _subs.Clear();

        if (_interactPrompt != null)
            _interactPrompt.gameObject.SetActive(false);

        if (_locker != null)
        {
            _locker.NotifyPlayerClosedServerRpc();
            _locker = null;
        }

        if (_lockerCollider != null)
            _lockerCollider.enabled = true;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void ShowPrompt(ShopItem item)
    {
        if (_interactPrompt == null) return;
        _interactPrompt.text = $"{item.Name}  <sprite=0>{item.Price}  [Click to buy]";
    }

    private void ClearPrompt()
    {
        if (_interactPrompt != null)
            _interactPrompt.text = string.Empty;
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
            Close();
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
