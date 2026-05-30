using UnityEngine;

/// <summary>
/// Base ScriptableObject defining a custom shop purchase action.
/// Derive from this to implement alternative purchase behaviors beyond the default prefab-spawn flow.
/// Assign to <see cref="ShopItem.CustomPurchaseAction"/> to override that default.
/// </summary>
public abstract class ShopPurchaseAction : ScriptableObject
{
    /// <summary>Whether the player must have empty hands before this purchase can proceed.</summary>
    public abstract bool RequiresEmptyHands { get; }

    /// <summary>
    /// Executes the purchase action on the purchasing client.
    /// Implementations are responsible for routing server-side work via ServerRpcs on <paramref name="pickup"/>.
    /// </summary>
    /// <param name="pickup">The local player's pickup controller.</param>
    /// <param name="price">The coupon cost to deduct.</param>
    public abstract void Execute(PlayerPickupController pickup, int price);
}
