/// <summary>
/// A box of shotgun shells. Hold it and left-click to load its shells into the player's
/// <see cref="PlayerAmmoReserve"/>; any shells that don't fit stay in the box.
/// See <see cref="AmmoPickup"/> for the shared behaviour and prefab requirements
/// ("Item Data" → Shotgun Ammo.asset).
/// </summary>
public class ShotgunAmmo : AmmoPickup
{
    /// <summary>Maximum shells a single box can carry.</summary>
    public const int MaxRoundsPerClip = 15;

    public override AmmoType AmmoType => AmmoType.Shotgun;
    public override int Capacity => MaxRoundsPerClip;
    protected override string DisplayName => "Shotgun Ammo";
}
