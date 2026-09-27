/// <summary>
/// A fuel cannister for the <see cref="Flamethrower"/>. Hold it and left-click to pour its fuel
/// into the player's <see cref="PlayerAmmoReserve"/>; any fuel that doesn't fit stays in the
/// cannister. See <see cref="AmmoPickup"/> for the shared behaviour and prefab requirements
/// ("Item Data" → Flamethrower Cannister.asset).
/// </summary>
public class FlamethrowerCannister : AmmoPickup
{
    /// <summary>Fuel units in a full cannister (one full flamethrower tank).</summary>
    public const int FuelPerCannister = (int)Flamethrower.MaxFuel;

    public override AmmoType AmmoType => AmmoType.Fuel;
    public override int Capacity => FuelPerCannister;
    protected override string DisplayName => "Flamethrower Fuel";
}
