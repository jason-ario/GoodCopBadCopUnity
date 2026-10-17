/// <summary>
/// Melee shovel. All swing, hit, audio and durability behaviour lives in <see cref="MeleeWeaponPickable"/>.
/// Kept as its own type because scene/day code references it directly (e.g. <c>Day_01</c> breach shovels).
/// </summary>
public class Shovel : MeleeWeaponPickable
{
}
