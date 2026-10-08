using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared helpers for client-resolved weapon hits (Pistol, Shotgun, MeleeWeaponHitbox).
/// </summary>
public static class CombatHitUtility
{
    private static readonly List<Collider> ColliderBuffer = new List<Collider>(64);

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
