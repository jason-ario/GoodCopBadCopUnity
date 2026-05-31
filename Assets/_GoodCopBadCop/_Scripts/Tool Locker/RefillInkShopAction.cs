using UnityEngine;

/// <summary>
/// Shop action that refills ink uses for a specific stamp type via <see cref="StampInkManager"/>.
/// Assign this ScriptableObject to a <see cref="ShopItem.CustomPurchaseAction"/> to replace
/// the default prefab-spawn behavior with an ink-refill on purchase.
/// </summary>
[CreateAssetMenu(fileName = "RefillInkShopAction", menuName = "Good Cop Bad Cop/Shop/Actions/Refill Ink")]
public class RefillInkShopAction : ShopPurchaseAction
{
    [Tooltip("The stamp type whose ink will be refilled.")]
    public StampContainer.StampType stampType;

    [Min(1)]
    [Tooltip("Number of uses to restore on purchase.")]
    public int amount = 1;

    /// <summary>Ink refills do not require the player to have empty hands.</summary>
    public override bool RequiresEmptyHands => false;

    /// <summary>Ink refills keep the locker open so the player can purchase additional refills.</summary>
    public override bool CloseShopOnPurchase => false;

    /// <summary>Routes the refill request through the networked pickup controller.</summary>
    public override void Execute(PlayerPickupController pickup, int price)
        => pickup.PurchaseRefillInk(stampType, amount, price);
}
