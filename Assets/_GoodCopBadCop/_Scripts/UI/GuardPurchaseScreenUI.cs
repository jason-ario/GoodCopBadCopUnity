using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Guard Purchase Screen.
/// Supports two display modes:
/// - Purchase mode: shows price and Yes/No buttons, greying out Yes when the player cannot afford the guard.
/// - Hired mode: shows a confirmation message with a single Okay button.
/// Set the mode before activating the screen's root GameObject.
/// </summary>
public class GuardPurchaseScreenUI : MonoBehaviour
{
    [Header("Purchase Mode")]
    [SerializeField] private GameObject _purchaseContent;
    [SerializeField] private TextMeshProUGUI _priceLabel;
    [SerializeField] private Button _yesButton;

    [Header("Hired Mode")]
    [SerializeField] private GameObject _hiredPanel;

    private int _price;
    private bool _isHiredMode;

    /// <summary>Configures the screen for purchase mode and sets the displayed price. Call before activating the screen.</summary>
    public void SetPurchaseMode(int price)
    {
        _price = price;
        _priceLabel.text = $"{price} coupons";
        _isHiredMode = false;
    }

    /// <summary>Configures the screen for hired mode. Call before activating the screen.</summary>
    public void SetHiredMode()
    {
        _isHiredMode = true;
    }

    private void OnEnable()
    {
        if (_isHiredMode)
        {
            _purchaseContent.SetActive(false);
            _hiredPanel.SetActive(true);
        }
        else
        {
            _purchaseContent.SetActive(true);
            _hiredPanel.SetActive(false);
            RefreshYesButton();

            if (GlobalHostVariables.Instance != null)
                GlobalHostVariables.Instance.money.OnValueChanged += OnMoneyChanged;
        }
    }

    private void OnDisable()
    {
        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.money.OnValueChanged -= OnMoneyChanged;
    }

    private void OnMoneyChanged(int previousValue, int newValue) => RefreshYesButton();

    private void RefreshYesButton()
    {
        bool canAfford = GlobalHostVariables.Instance != null
                         && GlobalHostVariables.Instance.money.Value >= _price;
        _yesButton.interactable = canAfford;
    }
}
