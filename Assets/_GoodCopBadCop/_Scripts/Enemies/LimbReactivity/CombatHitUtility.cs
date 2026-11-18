using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared helpers for client-resolved weapon hits (Pistol, Shotgun, MeleeWeaponHitbox).
/// </summary>
public static class CombatHitUtility
{
    private static readonly List<Collider> ColliderBuffer = new List<Collider>(64);

    // ── Weapon physics targets (kickable junk / gore, soccer ball) ───────────

    /// <summary>
    /// Blood hit particle used for gore that has no character of its own to supply one. Registered
    /// by <see cref="PlayerHealth"/> from its serialized hit particle (the same blood every body uses).
    /// </summary>
    public static GameObject SharedBloodHitPrefab { get; set; }

    /// <summary>Spawns <see cref="SharedBloodHitPrefab"/> at <paramref name="point"/> (local only).</summary>
    public static void SpawnSharedBloodHit(Vector3 point)
    {
        if (SharedBloodHitPrefab == null) return;

        GameObject fx = Object.Instantiate(SharedBloodHitPrefab, point, Quaternion.identity);
        if (fx.GetComponentInChildren<AutoDestroy>() == null)
            fx.AddComponent<AutoDestroy>();
    }

    /// <summary>
    /// The loose physics object a weapon hit on <paramref name="collider"/> should push, or null: a
    /// <see cref="SoccerBall"/> or <see cref="KickablePhysicsBody"/> junk (gore, Vlad pieces, trash).
    /// Doubles as a per-object key so multi-pellet blasts push each object once.
    /// </summary>
    public static Component GetWeaponPhysicsTarget(Collider collider)
    {
        if (collider == null || collider.isTrigger) return null;

        SoccerBall ball = collider.GetComponentInParent<SoccerBall>();
        if (ball != null) return ball;

        return collider.GetComponentInParent<KickablePhysicsBody>();
    }

    /// <summary>True when a weapon hit on <paramref name="collider"/> lands on gore that should bleed.</summary>
    public static bool IsGoreTarget(Collider collider)
    {
        KickablePhysicsBody body = collider != null ? collider.GetComponentInParent<KickablePhysicsBody>() : null;
        return body != null && body.IsGore;
    }

    /// <summary>
    /// Runs on the attacking player's machine. Gives a gun/melee hit's small physics push to a
    /// soccer ball or kickable junk piece (gore also bleeds). Purely cosmetic; never damages.
    /// <paramref name="strength"/> is roughly 1 for a pistol shot.
    /// </summary>
    public static void ApplyWeaponImpact(Collider collider, Vector3 point, Vector3 direction, float strength)
    {
        Component target = GetWeaponPhysicsTarget(collider);
        if (target is SoccerBall ball)
            ball.RequestWeaponHit(direction, strength);
        else if (target is KickablePhysicsBody body)
            body.RequestWeaponHit(point, direction, strength);
    }

    /// <summary>
    /// True when weapons should pass through <paramref name="collider"/>: open-mesh perimeter
    /// fences (<see cref="PerimiterFence"/>), fence gates (<see cref="GateController"/>), and
    /// anything tagged with a <see cref="BulletPassthrough"/> marker (decorative chain-link segments).
    /// </summary>
    public static bool IsBulletPassthrough(Collider collider)
    {
        if (collider == null) return false;
        return collider.GetComponentInParent<PerimiterFence>() != null
            || collider.GetComponentInParent<GateController>() != null
            || collider.GetComponentInParent<BulletPassthrough>() != null;
    }

    /// <summary>
    /// Server sanity distance from a reported hit point to the target: the distance to the nearest
    /// enabled collider's bounds, falling back to the root pivot. Measuring from the pivot alone
    /// threw away legitimate hits on big enemies (Ocho's arms and head are several metres from it).
    /// </summary>
    public static float DistanceToTarget(Component target, Vector3 point)
    {
        if (target == null) return float.MaxValue;

        float best = Vector3.Distance(target.transform.position, point);

        ColliderBuffer.Clear();
        target.GetComponentsInChildren(false, ColliderBuffer);
        foreach (Collider c in ColliderBuffer)
        {
            if (c == null || !c.enabled) continue;
            float d = Mathf.Sqrt(c.bounds.SqrDistance(point));
            if (d < best) best = d;
        }

        ColliderBuffer.Clear();
        return best;
    }

    /// <summary>
    /// A ray first touched <paramref name="owner"/> at <paramref name="sortedHits"/>[<paramref name="startIndex"/>].
    /// If that collider is a broad body capsule, looks further along the ray for one of the owner's
    /// <see cref="LimbHitbox"/> colliders and returns its index (or <paramref name="startIndex"/> if the
    /// first hit already is a limb, or no limb lies behind it before solid geometry).
    /// </summary>
    public static int PreferLimbAlongRay(RaycastHit[] sortedHits, int startIndex, Component owner)
    {
        if (sortedHits == null || owner == null || startIndex < 0 || startIndex >= sortedHits.Length)
            return startIndex;

        if (sortedHits[startIndex].collider.TryGetComponent<LimbHitbox>(out _))
            return startIndex;

        Transform ownerTransform = owner.transform;

        for (int j = startIndex + 1; j < sortedHits.Length; j++)
        {
            Collider c = sortedHits[j].collider;
            if (c == null) continue;

            bool ownedByTarget = c.transform.IsChildOf(ownerTransform);

            if (ownedByTarget && c.TryGetComponent<LimbHitbox>(out _))
                return j;

            // Solid world geometry between the capsule surface and the body blocks the lookahead.
            if (!ownedByTarget && !c.isTrigger && !IsBulletPassthrough(c))
                break;
        }

        return startIndex;
    }

    /// <summary>
    /// Among overlap results, returns the <paramref name="owner"/>'s <see cref="LimbHitbox"/> collider
    /// closest to <paramref name="origin"/>, or null if none were overlapped.
    /// </summary>
    public static Collider FindNearestLimbInOverlap(Collider[] buffer, int count, Component owner, Vector3 origin, out Vector3 closestPoint)
    {
        closestPoint = origin;
        if (buffer == null || owner == null) return null;

        Transform ownerTransform = owner.transform;
        Collider best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider c = buffer[i];
            if (c == null || !c.TryGetComponent<LimbHitbox>(out _) || !c.transform.IsChildOf(ownerTransform))
                continue;

            Vector3 p = c.ClosestPoint(origin);
            float sqr = (p - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
                closestPoint = p;
            }
        }

        return best;
    }
}
