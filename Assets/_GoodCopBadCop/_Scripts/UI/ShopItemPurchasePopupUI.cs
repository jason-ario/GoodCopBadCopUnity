using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Drives the Shop Item Purchase Popup shown when the player clicks a diegetic shop item.
/// Displays "Buy [item name]" as the title, the coupon price, a 3-D item preview, and a Buy button.
/// Greys out the Buy button when the player cannot afford the item.
/// Configure via <see cref="Setup"/> before activating the root GameObject.
///
/// Controller support: a sibling <see cref="GamepadMenuNavigator"/> handles selecting/highlighting
/// the Buy and No buttons and drives stick/d-pad navigation plus Submit (A) through Unity's
/// EventSystem; Cancel (B) is handled globally by <see cref="UIController"/>'s back-button check,
/// since the opener always wires the back button to the same cancel callback. This class only
/// manages the free cursor's default visibility so it doesn't compete with the controller highlight.
/// </summary>
public class ShopItemPurchasePopupUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _priceLabel;
    [SerializeField] private TextMeshProUGUI _descriptionLabel;
    [SerializeField] private Button _buyButton;
    [SerializeField] private Button _noButton;
    [SerializeField] private ItemPreviewSpawner _previewSpawner;

    [Header("Audio")]
    [SerializeField] private AudioClip _purchaseSound;

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
    /// <param name="titleOverride">
    /// When non-null and non-empty, replaces the default "Buy {item.Name}" title.
    /// Pass the item name directly to skip the "Buy " prefix for action-style interactables.
    /// </param>
    public void Setup(ShopItem item, Action onBuy, Action onCancel, string titleOverride = null)
    {
        _onBuy = onBuy;
        _onCancel = onCancel;
        _price = item.Price;
        _titleLabel.text = string.IsNullOrEmpty(titleOverride) ? $"Buy {item.Name}" : titleOverride;
        _priceLabel.text = $"<sprite=0>  {item.Price}";
        // Preview is handled by the in-world Cinemachine zoom camera.

        // Description comes from the item's PickableItemData (when assigned) — action-style
        // shop interactables with no pickableItemData (e.g. WorldPurchaseActionInteractable)
        // simply hide the label rather than show blank space.
        string description = item.pickableItemData != null ? item.pickableItemData.Description : null;
        if (_descriptionLabel != null)
        {
            _descriptionLabel.gameObject.SetActive(!string.IsNullOrEmpty(description));
            _descriptionLabel.text = description;
        }
    }

    /// <summary>Called by the Buy button's OnClick event.</summary>
    public void OnBuyClicked()
    {
        if (_purchaseSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_purchaseSound);

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

        // Default to a hidden cursor when a gamepad is connected so the GamepadMenuNavigator's
        // button highlight (sibling component on this GameObject) is the only visible selection
        // indicator, mirroring the exam notebook / dialogue choice controller behaviour. The
        // opener (e.g. WorldShopItemInteractable) always calls ShowCursor() just before this
        // popup activates, so this runs after and wins.
        if (Gamepad.current != null && UIController.Instance != null)
            UIController.Instance.HideCursor();
    }

    private void OnDisable()
    {
        if (GlobalHostVariables.Instance != null)
            GlobalHostVariables.Instance.money.OnValueChanged -= OnMoneyChanged;
    }

    private void Update()
    {
        // Mouse/keyboard activity always wins: restore the free cursor so mouse clicks on the
        // Buy/No buttons work as normal and the controller highlight stops competing with it.
        bool mkActivity = (Mouse.current != null &&
                            (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                             Mouse.current.leftButton.wasPressedThisFrame)) ||
                           (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (mkActivity)
        {
            UIController.Instance?.ShowCursor();
        }
        else if (Gamepad.current != null)
        {
            bool navInput = Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.1f
                             || Gamepad.current.dpad.up.wasPressedThisFrame
                             || Gamepad.current.dpad.down.wasPressedThisFrame
                             || Gamepad.current.buttonSouth.wasPressedThisFrame;
            if (navInput)
                UIController.Instance?.HideCursor();
        }
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
