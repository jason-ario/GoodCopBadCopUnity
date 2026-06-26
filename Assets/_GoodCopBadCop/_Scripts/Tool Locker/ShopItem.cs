using System;
using HighlightPlus;
using UnityEngine;

[RequireComponent(typeof(HighlightEffect))]
public class ShopItem : MonoBehaviour, IHoverable, IClickable
{
    [SerializeField] private string name;
    public string Name => name; 
    public PickableItemData pickableItemData;

    private HighlightEffect _highlightEffect;
    private bool _highlightBlocked;

    [SerializeField] private int price;
    private int? _priceOverride = null;
    public int Price => _priceOverride ?? price;

    /// <summary>Temporarily overrides this item's displayed and charged price. Clear with <see cref="ClearPriceOverride"/>.</summary>
    public void SetPriceOverride(int overridePrice) => _priceOverride = overridePrice;

    /// <summary>Restores this item's price to its configured value.</summary>
    public void ClearPriceOverride() => _priceOverride = null;

    [SerializeField] private Vector3 rotationOffset; 
    public Vector3 RotationOffset => rotationOffset;

    [Tooltip("Optional custom purchase action. When set, overrides the default prefab-spawn behavior.")]
    [SerializeField] private ShopPurchaseAction customPurchaseAction;
    /// <summary>
    /// When non-null this action is executed on purchase instead of spawning <see cref="pickableItemData"/>.
    /// </summary>
    public ShopPurchaseAction CustomPurchaseAction => customPurchaseAction;

    // -------------------------------------------------------------------------
    // Availability
    // -------------------------------------------------------------------------

    [Tooltip("When false, this item is hidden behind '???' in the shop until explicitly unlocked via gameplay progression.")]
    [SerializeField] private bool _unlockedByDefault = true;

    /// <summary>
    /// Runtime availability override. Null means fall back to <see cref="_unlockedByDefault"/>.
    /// Stored as nullable so the field works correctly on prefab assets where Awake never runs.
    /// </summary>
    private bool? _availabilityOverride;

    /// <summary>
    /// True if this item is visible and purchasable in the shop.
    /// Determined by <see cref="_unlockedByDefault"/> unless overridden at runtime via <see cref="SetAvailable"/>.
    /// </summary>
    public bool IsAvailable => _availabilityOverride ?? _unlockedByDefault;

    /// <summary>
    /// Sets the runtime availability of this item. Use <see cref="MegaphoneDialogueManager.SetShopItemAvailableSynced"/>
    /// to propagate the unlock across all clients and persist it to the save file.
    /// </summary>
    public void SetAvailable(bool available) => _availabilityOverride = available;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Fired when the cursor enters this item. Subscribers can show contextual UI.</summary>
    public event Action Hovered;

    /// <summary>Fired when the cursor leaves this item.</summary>
    public event Action Unhovered;

    /// <summary>Fired when the player clicks this item.</summary>
    public event Action Clicked;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _highlightEffect = GetComponent<HighlightEffect>();
        _highlightEffect.enabled = false;
        _highlightEffect.highlighted = true;
        _highlightEffect.ProfileLoad(_highlightEffect.profile);
    }

    // -------------------------------------------------------------------------
    // IHoverable
    // -------------------------------------------------------------------------

    public void OnHoverEnter()
    {
        if (!IsAvailable || _highlightBlocked) return;
        Highlight(true);
        Hovered?.Invoke();
    }

    public void OnHoverExit()
    {
        Highlight(false);
        Unhovered?.Invoke();
    }

    // -------------------------------------------------------------------------
    // IClickable
    // -------------------------------------------------------------------------

    public void OnClick()
    {
        if (!IsAvailable || _highlightBlocked) return;
        Clicked?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Highlight
    // -------------------------------------------------------------------------

    /// <summary>Enables or disables the highlight effect on this shop item.</summary>
    public void Highlight(bool highlight)
    {
        if (_highlightEffect != null)
            _highlightEffect.enabled = highlight;
    }

    /// <summary>
    /// Prevents this item from being highlighted regardless of hover state.
    /// Immediately disables any active highlight when <paramref name="blocked"/> is true.
    /// </summary>
    public void SetHighlightBlocked(bool blocked)
    {
        _highlightBlocked = blocked;
        if (blocked)
            Highlight(false);
    }
}
