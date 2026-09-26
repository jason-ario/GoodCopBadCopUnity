using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps junk highlighted only while an active task requires it. Gore chunks, mutant corpses,
/// and ordinary trash remain baggable whenever their <see cref="JunkItem.CanBeCollected"/>
/// predicate allows it, but unneeded environmental debris does not advertise itself as an
/// objective.
///
/// Players can still find required junk reliably during clean-up: gore reads as scenery against a
/// dark, prop-heavy 1989 yard, and the hover highlight only fires once the reticle is already on the
/// item. Task-owned junk therefore glows, while unrelated collectible debris remains visually quiet.
///
/// Deliberately NOT conditioned on what the player is holding. The glow means "this is collectible
/// junk", not "you can grab it this instant" — a player who hasn't fetched a trash bag yet is
/// exactly the player who most needs to be shown where the mess is.
///
/// Purely visual and purely local — nothing here is networked or server-authoritative. The highlight
/// is claimed through <see cref="HighlightHold.PickupAffordance"/> so it composes with, and never
/// clobbers, tutorial call-outs claimed via <see cref="HighlightHold.Tutorial"/>.
///
/// Wiring: <see cref="JunkItem"/> registers, refreshes, and unregisters itself across its own
/// lifecycle. No scene setup required.
/// </summary>
public static class JunkPickupHighlightService
{
    private static readonly HashSet<JunkItem> _registered = new HashSet<JunkItem>();

    private static bool _enabled = true;

    /// <summary>
    /// Global on/off for the whole affordance. Single lever for suppressing the glow when it would
    /// be intrusive (cutscenes, scripted beats) or undesirable. Defaults to on.
    /// </summary>
    public static bool Enabled => _enabled;

    /// <summary>
    /// Enables or disables the affordance globally and immediately re-applies it to every registered
    /// item. Releases only this system's highlight hold, so a tutorial call-out on the same object
    /// stays lit.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;

        _enabled = enabled;
        RefreshAll();
    }

    // ── Registration / refresh (called by JunkItem) ────────────────────────────

    /// <summary>
    /// Adds <paramref name="junk"/> to the tracked set and syncs its glow to its current
    /// collectibility. Safe and cheap to call repeatedly — it re-applies the desired state rather
    /// than bailing out on an already-registered item, which is what makes the runtime
    /// "component enabled" path work (a mutant corpse registers while its <see cref="JunkItem"/> is
    /// still disabled, then refreshes from OnEnable once <c>MutantEnemy</c> makes it collectible).
    /// <see cref="Interactable.SetForceHighlight"/> no-ops when the flag state is unchanged, so
    /// redundant calls cost nothing.
    /// </summary>
    public static void Register(JunkItem junk)
    {
        if (junk == null) return;

        _registered.Add(junk);
        Apply(junk);
    }

    /// <summary>
    /// Re-reads <paramref name="junk"/>'s collectibility and updates its glow. Called whenever
    /// something feeding <see cref="JunkItem.CanBeCollected"/> changes.
    /// </summary>
    public static void Refresh(JunkItem junk)
    {
        if (junk == null || !_registered.Contains(junk)) return;

        Apply(junk);
    }

    /// <summary>
    /// Removes <paramref name="junk"/> from the tracked set and releases this system's highlight
    /// hold, leaving any other hold (e.g. a tutorial call-out) untouched.
    /// </summary>
    public static void Unregister(JunkItem junk)
    {
        if (junk == null) return;

        if (!_registered.Remove(junk)) return;

        junk.SetForceHighlight(false, HighlightHold.PickupAffordance);
        CompassMarkerRegistry.Unregister(junk.transform);
    }

    /// <summary>Re-applies the affordance to every tracked item.</summary>
    public static void RefreshAll()
    {
        // Snapshot: SetForceHighlight can run OnStopHighlight overrides that touch the set.
        var snapshot = new List<JunkItem>(_registered);

        foreach (JunkItem junk in snapshot)
        {
            if (junk == null)
            {
                _registered.Remove(junk);
                continue;
            }

            Apply(junk);
        }
    }

    /// <summary>
    /// Drops all tracked items after releasing their holds. Call on scene teardown so nothing is
    /// left glowing and no destroyed items are retained.
    /// </summary>
    public static void Reset()
    {
        var snapshot = new List<JunkItem>(_registered);
        _registered.Clear();

        foreach (JunkItem junk in snapshot)
        {
            if (junk != null)
            {
                junk.SetForceHighlight(false, HighlightHold.PickupAffordance);
                CompassMarkerRegistry.Unregister(junk.transform);
            }
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static void Apply(JunkItem junk)
    {
        bool requiredByTask = junk.IsTaskRequired.Value && junk.CanBeCollected;

        junk.SetForceHighlight(_enabled && requiredByTask, HighlightHold.PickupAffordance);

        // Objective markers follow the same replicated task-ownership state as the persistent
        // highlight. Pickup availability remains independent: unmarked debris is still baggable.
        if (_enabled && requiredByTask)
            CompassMarkerRegistry.Register(junk.transform, CompassMarkerCategory.Junk);
        else
            CompassMarkerRegistry.Unregister(junk.transform);
    }
}
