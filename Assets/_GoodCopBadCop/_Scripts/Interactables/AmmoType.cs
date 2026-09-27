/// <summary>
/// Kinds of ammunition a player can carry in their <see cref="PlayerAmmoReserve"/>.
/// Each <see cref="AmmoPickup"/> feeds exactly one type, and each <see cref="IInventoryReloadable"/>
/// weapon reloads from exactly one type.
/// </summary>
public enum AmmoType : byte
{
    Pistol  = 0,
    Shotgun = 1,
    Fuel    = 2,
}
