using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Shop Item Purchase Popup shown when the player clicks a diegetic shop item.
/// Displays "Buy [item name]" as the title, the coupon price, a 3-D item preview, and a Buy button.
/// Greys out the Buy button when the player cannot afford the item.
/// Configure via <see cref="Setup"/> before activating the root GameObject.
/// </summary>
public class ShopItemPurchasePopupUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _priceLabel;
    [SerializeField] private Button _buyButton;
    [SerializeField] private Button _noButton;
    [SerializeField] private ItemPreviewSpawner _previewSpawner;

    private Action _onBuy;
    private Action _onCancel;
    private int _price;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the popup for a specific shop item. Call before activating the root GameObject.
    /// Spawns a live 3-D preview of <paramref name="item"/> in the preview camera.
    /// </summary>
    /// <param name="item">The shop item to display and purchase.</param>
    /// <param name="onBuy">Callback invoked when the player confirms the purchase.</param>
    /// <param name="onCancel">Callback invoked when the player presses the No button.</param>
    public void Setup(ShopItem item, Action onBuy, Action onCancel)
    {
        _onBuy = onBuy;
        _onCancel = onCancel;
        _price = item.Price;
        _titleLabel.text = $"Buy {item.Name}";
        _priceLabel.text = $"<sprite=0>  {item.Price}";
        _previewSpawner?.SpawnAndFrame(item);
    }

    /// <summary>Called by the Buy button's OnClick event.</summary>
    public void OnBuyClicked()
    {
        _onBuy?.Invoke();
    }

    /// <summary>Called by the No button's OnClick event.</summary>
    public void OnCancelClicked()
    {
        _onCancel?.Invoke();
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

        _previewSpawner?.Clear();
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
