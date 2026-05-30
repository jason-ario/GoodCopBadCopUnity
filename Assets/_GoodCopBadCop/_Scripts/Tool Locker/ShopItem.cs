using System;
using UnityEngine;

public class ShopItem : MonoBehaviour
{
    [SerializeField] private string name;
    public string Name => name; 
    public PickableItemData pickableItemData;

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
    
    private void Awake()
    {
        SetLayer();
    }

    void SetLayer()
    {
        int layer = LayerMask.NameToLayer("ShopItems");
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }
}
