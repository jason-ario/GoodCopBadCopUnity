/// <summary>
/// Implemented by weapons (<see cref="Pistol"/>, <see cref="Shotgun"/>, <see cref="Flamethrower"/>)
/// that reload from the holder's <see cref="PlayerAmmoReserve"/>. Driven by
/// <see cref="PlayerInventory"/>'s KeyCode.R handling while the weapon is equipped.
/// </summary>
public interface IInventoryReloadable
{
    /// <summary>The reserve ammo type this weapon consumes.</summary>
    AmmoType ReserveAmmoType { get; }

    /// <summary>
    /// Owner-side. Requests a reload from the local player's reserve. No-ops if the weapon is
    /// already full or the reserve has none of <see cref="ReserveAmmoType"/>.
    /// </summary>
    void RequestReloadFromReserve();
}
