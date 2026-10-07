using UnityEngine;

/// <summary>
/// Runtime marker placed by <see cref="LimbHitReactor"/> on every limb collider it manages.
/// Lets weapon hit resolution tell a precise per-limb hitbox apart from an enemy's broad root
/// capsule (see <see cref="CombatHitUtility"/>), and maps the collider back to its limb index.
/// Not meant to be authored by hand — the reactor adds/configures it.
/// </summary>
[DisallowMultipleComponent]
public class LimbHitbox : MonoBehaviour
{
    public LimbHitReactor Reactor { get; private set; }
    public int LimbIndex { get; private set; }
    public Collider Collider { get; private set; }

    internal void Bind(LimbHitReactor reactor, int limbIndex, Collider col)
    {
        Reactor   = reactor;
        LimbIndex = limbIndex;
        Collider  = col;
    }
}
