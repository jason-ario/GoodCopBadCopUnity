using UnityEngine;

/// <summary>
/// A hammer the player can pick up and swing to:
///   - Repair broken PerimiterFence segments (owner-side OverlapSphere, fence ServerRpc).
///   - Damage mutant enemies (via MeleeWeaponHitbox).
///
/// Swing timing, animation lock, hit scan, audio and durability come from <see cref="MeleeWeaponPickable"/>;
/// this class only adds the fence repair at the hit frame.
///
/// Prefab setup:
///   - Root: NetworkObject + HammerPickable + MeleeWeaponDurability (optional) + ParentConstraint
///   - Child "AttackPoint": MeleeWeaponHitbox (NetworkBehaviour)
///   - Assign a PickableItemData ScriptableObject.
/// </summary>
public class HammerPickable : MeleeWeaponPickable
{
    [Header("Fence Repair")]
    [Tooltip("Radius of the owner-side OverlapSphere used to detect nearby fence segments.")]
    [SerializeField] private float _fenceHitRadius = 1.5f;

    [Tooltip("Layer mask for the fence OverlapSphere. Restrict to your fence layer for best performance.")]
    [SerializeField] private LayerMask _fenceLayerMask = ~0;

    protected override void PerformSwingHit()
    {
        Vector3 origin = playerPickupController != null && playerPickupController.holdPoint != null
            ? playerPickupController.holdPoint.position
            : transform.position;

        TryHitFence(origin);
        base.PerformSwingHit();
    }

    /// <summary>
    /// Owner-side OverlapSphere that finds the nearest PerimiterFence and sends a single repair
    /// hit via ServerRpc. Does not interact with enemies.
    ///
    /// Two deliberate details:
    ///   - It picks the <em>nearest</em> segment rather than whichever collider the physics query
    ///     happened to return first, so a swing always lands on the fence the player is standing at.
    ///   - It never refuses to send the RPC based on this client's own view of the fence's health.
    ///     The server re-validates in <c>HitWithHammerServerRpc</c> and ignores hits on intact
    ///     segments, so sending unconditionally is free and authoritative.
    /// </summary>
    private void TryHitFence(Vector3 origin)
    {
        Collider[] hits = Physics.OverlapSphere(origin, _fenceHitRadius, _fenceLayerMask);

        PerimiterFence nearest = null;
        PerimiterFence nearestBroken = null;
        float nearestSqr = float.MaxValue;
        float nearestBrokenSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            PerimiterFence fence = col.GetComponent<PerimiterFence>()
                                ?? col.GetComponentInParent<PerimiterFence>();

            if (fence == null || !fence.IsSpawned) continue;

            float sqr = (col.ClosestPoint(origin) - origin).sqrMagnitude;

            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = fence;
            }

            if (fence.IsBroken && sqr < nearestBrokenSqr)
            {
                nearestBrokenSqr = sqr;
                nearestBroken = fence;
            }
        }

        // Prefer a segment this client already knows is damaged; otherwise let the server decide.
        PerimiterFence target = nearestBroken ?? nearest;
        target?.HitWithHammerServerRpc();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform origin = playerPickupController != null && playerPickupController.holdPoint != null
            ? playerPickupController.holdPoint
            : transform;

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawSphere(origin.position, _fenceHitRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(origin.position, _fenceHitRadius);
    }
#endif
}
