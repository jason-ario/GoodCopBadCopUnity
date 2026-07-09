using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Performs an OverlapSphere at the melee hit moment to detect and damage players.
/// Attach to an AttackPoint child of the mutant prefab.
/// Call <see cref="PerformHitScan"/> from the server at the animation's melee frame
/// (via a timed delay in MutantEnemy).
/// </summary>
public class MutantAttackHitbox : MonoBehaviour
{

    // Configuration

    [Tooltip("Radius of the overlap sphere used to detect players on attack.")]
    [SerializeField] private float sphereRadius = 0.8f;

    [Tooltip("Tag used to identify player GameObjects. Must match the Player prefab's tag.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Damage dealt per successful melee hit. Overrides MutantEnemyData when set > 0; otherwise MutantEnemy passes the data value.")]
    [SerializeField] private float damageOverride = 0f;


    // Internal

    private MutantEnemy _owner;

    private static readonly Collider[] OverlapBuffer = new Collider[8];

    private void Awake()
    {
        _owner = GetComponentInParent<MutantEnemy>();
    }


    // Public API

    /// <summary>
    /// Runs an OverlapSphere from this transform and damages any player found.
    /// Must only be called on the server.
    /// </summary>
    /// <param name="damageAmount">Damage to apply; ignored when damageOverride > 0.</param>
    public void PerformHitScan(float damageAmount)
    {
        if (_owner != null && !_owner.IsServer)
        {
            Debug.LogWarning("[MutantAttackHitbox] PerformHitScan called on a non-server instance.", this);
            return;
        }

        float damage = damageOverride > 0f ? damageOverride : damageAmount;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            sphereRadius,
            OverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        Debug.Log($"[MutantAttackHitbox] PerformHitScan at {transform.position} radius={sphereRadius} - {hitCount} colliders overlapped.", this);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = OverlapBuffer[i];

            // Walk up to the root to find a tagged player object.
            Transform root = col.transform.root;

            if (!root.CompareTag(playerTag))
                continue;

            PlayerHealth playerHealth = root.GetComponentInChildren<PlayerHealth>();
            if (playerHealth == null || playerHealth.IsDead)
                continue;

            // Do not damage players who entered a cutscene after the attack was committed
            // (guards DelayedHitScan coroutines that were already in-flight when the player
            // entered dialogue mode — the attack animation fires but the hit is suppressed).
            PlayerInstance playerInstance = root.GetComponent<PlayerInstance>();
            if (playerInstance != null && playerInstance.IsInCutscene)
                continue;

            playerHealth.TakeDamage(damage, EffectKeys.MutantMeleeDamage);
            Debug.Log($"[MutantAttackHitbox] Hit player '{root.name}' via collider '{col.name}' for {damage} damage.", this);

            // Only damage once per swing even if multiple colliders on same player.
            break;
        }
    }

    /// <summary>
    /// Performs an OverlapSphere from this transform (identical sphere to <see cref="PerformHitScan"/>)
    /// and applies <paramref name="damage"/> to <paramref name="fenceTarget"/> if any of its
    /// colliders fall inside the sphere. Must only be called on the server.
    /// </summary>
    /// <returns>True when the fence was hit.</returns>
    public bool PerformFenceHitScan(float damage, PerimiterFence fenceTarget)
    {
        if (fenceTarget == null) return false;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            sphereRadius,
            OverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (OverlapBuffer[i].GetComponentInParent<PerimiterFence>() == fenceTarget)
            {
                Vector3 hitPosition = OverlapBuffer[i].ClosestPoint(transform.position);
                fenceTarget.TakeMutantHitServer(damage, hitPosition);
                Debug.Log($"[MutantAttackHitbox] PerformFenceHitScan hit fence '{fenceTarget.name}' for {damage} damage.", this);
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, sphereRadius);
    }
#endif
}
