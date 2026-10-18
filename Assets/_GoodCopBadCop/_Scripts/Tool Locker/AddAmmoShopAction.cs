using UnityEngine;

/// <summary>
/// Shop action that adds ammunition directly to the buyer's <see cref="PlayerAmmoReserve"/>
/// instead of spawning an <see cref="AmmoPickup"/> item into their hands.
/// Assign this ScriptableObject to a <see cref="ShopItem.CustomPurchaseAction"/>.
///
/// The purchase is refused (no coupons spent) when the reserve for <see cref="ammoType"/> is full.
/// If only part of <see cref="amount"/> fits, the purchase still goes through and the reserve is
/// topped up to its carry limit.
/// </summary>
[CreateAssetMenu(fileName = "AddAmmoShopAction", menuName = "Good Cop Bad Cop/Shop/Actions/Add Ammo")]
public class AddAmmoShopAction : ShopPurchaseAction
{
    private const string ReserveFullMessage = "Ammo reserve full!";

    [Tooltip("Which ammo reserve this purchase feeds.")]
    public AmmoType ammoType = AmmoType.Pistol;

    [Min(1)]
    [Tooltip("Amount of ammo added to the reserve per purchase.")]
    public int amount = 30;

    [Tooltip("Close the shop after buying. Off by default so players can buy several in a row.")]
    [SerializeField] private bool _closeShopOnPurchase = false;

    /// <summary>Ammo goes straight to the reserve, so hands don't need to be empty.</summary>
    public override bool RequiresEmptyHands => false;

    public override bool CloseShopOnPurchase => _closeShopOnPurchase;

    /// <summary>Refuses the purchase locally when the local player's reserve has no room.</summary>
    public override bool CanExecute(PlayerPickupController pickup, out string blockedMessage)
    {
        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        if (reserve != null && reserve.GetFreeSpace(ammoType) <= 0)
        {
            reserve.RaiseFullLocal(ammoType);
            blockedMessage = ReserveFullMessage;
            return false;
        }

        blockedMessage = null;
        return true;
    }

    /// <summary>Routes the request through the networked pickup controller.</summary>
    public override void Execute(PlayerPickupController pickup, int price)
        => pickup.PurchaseAmmo(ammoType, amount, price);
}
