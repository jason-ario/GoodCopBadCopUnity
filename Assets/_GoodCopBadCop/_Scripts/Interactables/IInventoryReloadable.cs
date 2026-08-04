/// <summary>
/// Implemented by weapons (e.g. <see cref="Pistol"/>, <see cref="Shotgun"/>) that can be reloaded
/// from a compatible ammo item sitting in the player's <see cref="PlayerInventory"/> — i.e. without
/// requiring the ammo to be physically held in hand first. Driven by <see cref="PlayerInventory"/>'s
/// KeyCode.R handling.
/// </summary>
public interface IInventoryReloadable
{
    /// <summary>Returns true if <paramref name="candidate"/> is an ammo item this weapon can reload from.</summary>
    bool IsCompatibleAmmo(PickableObject candidate);

    /// <summary>
    /// Requests a reload using <paramref name="ammoItem"/>, which is not currently held in hand
    /// (it lives in the other inventory slot). No-ops if the weapon is already full or the item
    /// is not compatible ammo.
    /// </summary>
    void ReloadFromInventory(PickableObject ammoItem);
}
