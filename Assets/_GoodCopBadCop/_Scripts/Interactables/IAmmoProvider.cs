using System;

/// <summary>
/// Implemented by any holdable weapon that consumes a finite resource (bullets, fuel, etc.)
/// so the HUD Ammo Counter can display and track it generically.
/// </summary>
public interface IAmmoProvider
{
    /// <summary>Current resource remaining (rounds, fuel, etc.).</summary>
    float CurrentAmmo { get; }

    /// <summary>Maximum resource capacity.</summary>
    float MaxAmmo { get; }

    /// <summary>Fired on all clients whenever <see cref="CurrentAmmo"/> changes.</summary>
    event Action OnAmmoChanged;
}
