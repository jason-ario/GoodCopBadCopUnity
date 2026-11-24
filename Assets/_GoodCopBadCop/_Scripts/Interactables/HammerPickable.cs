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

    [Tooltip("Where the fence OverlapSphere is centred along the hand → hammer-head line. " +
             "0 = hold point (hand, shortest reach), 1 = swing hitbox (head, same reach as the impact sparks).")]
    [Range(0f, 1f)]
    [SerializeField] private float _fenceReach = 0.5f;

    [Tooltip("Layer mask for the fence OverlapSphere. Restrict to your fence layer for best performance.")]
    [SerializeField] private LayerMask _fenceLayerMask = ~0;

    protected override void PerformSwingHit()
    {
        TryHitFence();
        base.PerformSwingHit();
    }

    private Vector3 HoldOrigin => playerPickupController != null && playerPickupController.holdPoint != null
        ? playerPickupController.holdPoint.position
        : transform.position;

    /// <summary>Centre of the fence scan: <see cref="_fenceReach"/> of the way from the hand to the hammer head.</summary>
    private Vector3 FenceOrigin => Hitbox != null
        ? Vector3.Lerp(HoldOrigin, Hitbox.transform.position, _fenceReach)
        : HoldOrigin;

    /// <summary>
    /// Owner-side OverlapSphere that finds the nearest PerimiterFence and sends a single repair
    /// hit via ServerRpc. Does not interact with enemies.
    ///
    /// Three deliberate details:
    ///   - The scan is centred between the hold point (hand) and the swing hitbox (head), tuned by
    ///     <see cref="_fenceReach"/>. Hand-only reach was too short (sparks on the fence but no
    ///     repair unless hugging it); head reach with the hitbox radius was too long. The hold
    ///     point is still scanned as a fallback for very close swings.
    ///   - It picks the <em>nearest</em> segment rather than whichever collider the physics query
    ///     happened to return first, so a swing always lands on the fence the player is standing at.
    ///   - It never refuses to send the RPC based on this client's own view of the fence's health.
    ///     The server re-validates in <c>HitWithHammerServerRpc</c> and ignores hits on intact
    ///     segments, so sending unconditionally is free and authoritative.
    /// </summary>
    private void TryHitFence()
    {
        PerimiterFence nearest = null;
        PerimiterFence nearestBroken = null;
        float nearestSqr = float.MaxValue;
        float nearestBrokenSqr = float.MaxValue;

        CollectFences(FenceOrigin, _fenceHitRadius,
            ref nearest, ref nearestSqr, ref nearestBroken, ref nearestBrokenSqr);

        CollectFences(HoldOrigin, _fenceHitRadius,
            ref nearest, ref nearestSqr, ref nearestBroken, ref nearestBrokenSqr);

        // Prefer a segment this client already knows is damaged; otherwise let the server decide.
        PerimiterFence target = nearestBroken ?? nearest;
        target?.HitWithHammerServerRpc();
    }

    private void CollectFences(Vector3 origin, float radius,
        ref PerimiterFence nearest, ref float nearestSqr,
        ref PerimiterFence nearestBroken, ref float nearestBrokenSqr)
    {
        Collider[] hits = Physics.OverlapSphere(origin, radius, _fenceLayerMask, QueryTriggerInteraction.Collide);

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
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(HoldOrigin, _fenceHitRadius);

        Gizmos.color = new Color(1f, 0.85f, 0f, 1f);
        Gizmos.DrawWireSphere(FenceOrigin, _fenceHitRadius);
    }
#endif
}
