using System;

/// <summary>
/// Implemented by ammo box/clip pickups (e.g. <see cref="PistolAmmo"/>, <see cref="ShotgunAmmo"/>)
/// so UI (e.g. <see cref="InventorySlotUI"/>) can generically display how many rounds remain
/// inside the box while it sits in the player's inventory.
/// </summary>
public interface IAmmoStock
{
    /// <summary>Current number of rounds remaining in this box/clip.</summary>
    int RoundsInClip { get; }

    /// <summary>Maximum rounds a single box/clip can carry.</summary>
    int MaxRoundsInClip { get; }

    /// <summary>Fired whenever <see cref="RoundsInClip"/> changes.</summary>
    event Action OnRoundsChanged;
}
