using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A circuit-breaker fuse that the player can pick up and carry to the fuse box.
///
/// Each fuse has a <see cref="FuseColor"/> that determines which slot on the
/// <see cref="FuseBoxPuzzleController"/> fuse-box panel will accept it.
/// The color is also visually communicated via a colored <see cref="MeshRenderer"/>
/// material set up in the prefab.
///
/// Setup notes:
///   - Attach to a prefab that already has <see cref="PickableObject"/> requirements
///     (NetworkObject, NetworkTransform, HighlightEffect, ParentConstraint).
///   - Assign a <see cref="PickableItemData"/> in the base <see cref="PickableObject"/>
///     itemData field (one ScriptableObject per color recommended).
///   - Set <see cref="_fuseColor"/> to match the material colour used on the mesh.
/// </summary>
public class FusePickup : PickableObject
{
    [Header("Fuse")]
    [Tooltip("The color of this fuse — must match the expectedColor on the target FuseSlot.")]
    [SerializeField] private FuseColor _fuseColor;

    /// <summary>The color identity of this fuse.</summary>
    public FuseColor FuseColor => _fuseColor;

    // ── "Find the fuses" highlight (local, per client) ───────────────────────

    private static readonly HashSet<FusePickup> s_spawned = new();
    private static bool s_findHighlightActive;

    /// <summary>True while this fuse is seated in a <see cref="FuseSlot"/>; seated fuses never glow.</summary>
    private bool _isSeated;

    /// <summary>The "find the fuses" glow stays on while carried, until the fuse is seated.</summary>
    protected override bool KeepHoldHighlightWhileHeld => true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_spawned.Clear();
        s_findHighlightActive = false;
    }

    /// <summary>
    /// Local, every client. Toggles the "find the fuses" highlight on every spawned fuse that
    /// isn't seated in a fuse slot (held fuses keep glowing). Fuses are NOT highlighted on spawn — <c>Day_03</c> enables this
    /// only once the fuse box has been investigated, and disables it once every slot is filled.
    /// Fuses spawned while active pick the state up automatically.
    /// </summary>
    public static void SetFindHighlightActive(bool active)
    {
        if (s_findHighlightActive == active) return;
        s_findHighlightActive = active;

        foreach (FusePickup fuse in s_spawned)
            if (fuse != null) fuse.ApplyFindHighlight();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        s_spawned.Add(this);
        ApplyFindHighlight();
    }

    public override void OnNetworkDespawn()
    {
        s_spawned.Remove(this);
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Local, every client. Called by <see cref="FuseSlot"/> when this fuse is inserted into
    /// (true) or extracted from (false) a slot. Seating clears the "find the fuses" glow;
    /// extracting it while the highlight step is still active restores the glow.
    /// </summary>
    public void SetSeated(bool seated)
    {
        if (_isSeated == seated) return;
        _isSeated = seated;
        ApplyFindHighlight();
    }

    private void ApplyFindHighlight() =>
        SetForceHighlight(s_findHighlightActive && !_isSeated);
}
