/// <summary>
/// Category of a world-space target shown as a pip on the bottom-of-screen <see cref="CompassController"/>.
/// Each category renders with its own color (see <see cref="CompassController"/>'s category color map)
/// so the player can tell at a glance what kind of objective is off in a given direction.
/// </summary>
public enum CompassMarkerCategory
{
    /// <summary>Hand-scripted tutorial call-outs (<see cref="TutorialMarkerManager"/>'s floating world arrows).</summary>
    Tutorial,

    /// <summary>Collectible junk/trash/gore currently glowing via <see cref="JunkPickupHighlightService"/>.</summary>
    Junk,

    /// <summary>An unscrubbed graffiti piece (<see cref="GraffitiInteractable"/>).</summary>
    Graffiti,

    /// <summary>A perimeter fence segment that needs repair (<see cref="PerimiterFence.IsBroken"/>).</summary>
    Fence,

    /// <summary>An uncleaned blood splatter (also a <see cref="GraffitiInteractable"/>, on a blood decal prefab).</summary>
    Blood,
}
