/// <summary>
/// Color identity for circuit-breaker fuses used in the Day 3 power-station puzzle.
/// Each <see cref="FusePickup"/> carries one color, and the matching <see cref="FuseSlot"/>
/// on the fuse box will only accept a fuse of the same color.
/// </summary>
public enum FuseColor
{
    Red   = 0,
    Green = 1,
    Blue  = 2,
}
