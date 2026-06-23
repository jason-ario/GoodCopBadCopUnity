using TMPro;
using UnityEngine;

/// <summary>
/// Diegetic view for the tool locker. Extends <see cref="DiegeticViewController"/> with
/// shop-specific logic: raycasting for <see cref="ShopItem"/> objects under the cursor,
/// displaying a purchase prompt, and executing the buy flow on click.
/// </summary>
public class ToolLockerDiegeticController : DiegeticViewController
{
    [Header("Shop Items")]
    [Tooltip("All shop items physically placed inside the locker that the player can buy.")]
    [SerializeField] private ShopItem[] _shopItems;

    [Tooltip("Max raycast distance for detecting shop items under the cursor.")]
    [SerializeField] private float _interactDistance = 3f;

    [Tooltip("Layer mask that includes the ShopItems layer.")]
    [SerializeField] private LayerMask _shopItemLayer;

    [Header("UI")]
    [Tooltip("Screen-space TextMeshPro label shown when hovering a purchasable item. Optional.")]
    [SerializeField] private TextMeshProUGUI _interactPrompt;

    // ─── Runtime state ───────────────────────────────────────────────────────

    private ToolsLocker _locker;
    private ShopItem _hoveredItem;

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
        if (_interactPrompt != null)
        {
            _interactPrompt.gameObject.SetActive(true);
            _interactPrompt.text = string.Empty;
        }
    }

    protected override void OnClosed()
    {
        if (_interactPrompt != null)
            _interactPrompt.gameObject.SetActive(false);

        if (_locker != null)
        {
            _locker.NotifyPlayerClosedServerRpc();
            _locker = null;
        }

        _hoveredItem = null;
    }

    protected override void OnUpdate()
    {
        HandleLockerRaycast();

        if (Input.GetMouseButtonDown(0) && _hoveredItem != null)
            TryPurchase(_hoveredItem);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void HandleLockerRaycast()
    {
        if (Player == null || Player.cam == null) return;

        Ray ray = Player.cam.ScreenPointToRay(Input.mousePosition);

        _hoveredItem = null;
        ClearPrompt();

        if (!Physics.Raycast(ray, out RaycastHit hit, _interactDistance, _shopItemLayer))
            return;

        ShopItem item = hit.collider.GetComponentInParent<ShopItem>();
        if (item == null || !item.IsAvailable)
            return;

        _hoveredItem = item;
        ShowPrompt(item);
    }

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
