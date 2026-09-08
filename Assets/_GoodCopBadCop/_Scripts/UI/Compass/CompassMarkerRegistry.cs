using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Purely local, purely visual registry of world Transforms that should show a directional pip on
/// the bottom-of-screen <see cref="CompassController"/>, grouped by <see cref="CompassMarkerCategory"/>.
///
/// Deliberately separate from <see cref="TutorialMarkerManager"/> (which also spawns a floating
/// world-space arrow above its targets): junk/graffiti/fences/blood are already called out in-world
/// via outline highlights or decal visuals, so registering them here only adds a compass pip —
/// it does not spawn an extra floating arrow over every piece of trash on the map.
///
/// Not networked — every client independently registers/unregisters based on state it already
/// observes locally (replicated NetworkVariables, local highlight state, etc.), so nothing here
/// needs to be server-authoritative.
///
/// Wiring: category owners register/unregister themselves across their own lifecycle —
/// <see cref="JunkPickupHighlightService"/> (Junk), <c>GraffitiInteractable</c> (Graffiti/Blood),
/// and <c>PerimiterFence</c> (Fence). No scene setup required.
/// </summary>
public static class CompassMarkerRegistry
{
    private static readonly Dictionary<Transform, CompassMarkerCategory> _markers = new();

    /// <summary>Live view of every currently-registered target and its category. Do not mutate.</summary>
    public static IReadOnlyDictionary<Transform, CompassMarkerCategory> Markers => _markers;

    /// <summary>
    /// Registers (or re-categorizes) <paramref name="target"/> so it shows a compass pip.
    /// Safe to call repeatedly, including every frame — idempotent when the category is unchanged.
    /// </summary>
    public static void Register(Transform target, CompassMarkerCategory category)
    {
        if (target == null) return;
        _markers[target] = category;
    }

    /// <summary>Removes <paramref name="target"/>'s compass pip, if any. Safe to call with a target that isn't registered.</summary>
    public static void Unregister(Transform target)
    {
        if (target == null) return;
        _markers.Remove(target);
    }

    /// <summary>Clears every registered marker. Call on scene teardown so nothing lingers across a scene reload.</summary>
    public static void Clear() => _markers.Clear();
}
