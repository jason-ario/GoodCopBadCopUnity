using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Shop Item Purchase Popup shown when the player clicks a diegetic shop item.
/// Displays "Buy [item name]" as the title, the coupon price, and a Buy button.
/// Greys out the Buy button when the player cannot afford the item.
/// Configure via <see cref="Setup"/> before activating the root GameObject.
/// </summary>
public class ShopItemPurchasePopupUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _priceLabel;
    [SerializeField] private Button _buyButton;

    private Action _onBuy;
    private int _price;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the popup for a specific shop item. Call before activating the root GameObject.
    /// </summary>
    /// <param name="itemName">Name of the item to display in the title.</param>
    /// <param name="price">Coupon price; used for affordability check and display.</param>
    /// <param name="onBuy">Callback invoked when the player confirms the purchase.</param>
    public void Setup(string itemName, int price, Action onBuy)
    {
        _onBuy = onBuy;
        _price = price;
        _titleLabel.text = $"Buy {itemName}";
        _priceLabel.text = $"<sprite=0>{price}";
    }

    /// <summary>Called by the Buy button's OnClick event.</summary>
    public void OnBuyClicked()
    {
        _onBuy?.Invoke();
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        RefreshBuyButton();
        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.money.OnValueChanged += OnMoneyChanged;
    }

    private void OnDisable()
    {
        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.money.OnValueChanged -= OnMoneyChanged;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void OnMoneyChanged(int previousValue, int newValue) => RefreshBuyButton();

    private void RefreshBuyButton()
    {
        if (_buyButton == null) return;
        bool canAfford = GlobalHostVariables.Instance != null
                         && GlobalHostVariables.Instance.money.Value >= _price;
        _buyButton.interactable = canAfford;
    }
}
