/// <summary>
/// A pistol ammo clip. Hold it and left-click to load its rounds into the player's
/// <see cref="PlayerAmmoReserve"/>; any rounds that don't fit stay in the clip.
/// See <see cref="AmmoPickup"/> for the shared behaviour and prefab requirements
/// ("Item Data" → Pistol Ammo.asset).
/// </summary>
public class PistolAmmo : AmmoPickup
{
    /// <summary>Maximum rounds a single clip can carry.</summary>
    public const int MaxRoundsPerClip = 30;

    public override AmmoType AmmoType => AmmoType.Pistol;
    public override int Capacity => MaxRoundsPerClip;
    protected override string DisplayName => "Pistol Ammo";
}
