using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Performs an OverlapSphere at the attack point and damages any enemy found within range.
/// Attach to an AttackPoint child of the melee weapon prefab.
/// Call <see cref="PerformHitScan"/> from the weapon owner's client; the server validates
/// and applies damage, then notifies the owner via <see cref="OnHit"/> or
/// <see cref="OnEnvironmentHit"/> depending on what was struck.
/// </summary>
public class MeleeWeaponHitbox : NetworkBehaviour
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [Tooltip("Radius of the OverlapSphere centered on this transform.")]
    [SerializeField] private float hitRadius = 0.8f;

    [Tooltip("Tag used to identify enemy GameObjects. Must match the enemy prefab tag.")]
    [SerializeField] private string enemyTag = "Enemy";

    private const string PlayerTag = "Player";

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the owning client when the overlap successfully finds an enemy.
    /// </summary>
    public event Action OnHit;

    /// <summary>
    /// Fired on the owning client when the sphere overlaps geometry but no enemy.
    /// </summary>
    public event Action OnEnvironmentHit;

    // ── Internal ───────────────────────────────────────────────────────────────

    private static readonly Collider[] OverlapBuffer = new Collider[16];

    /// <summary>Colliders belonging to this weapon's own hierarchy, populated on Start.</summary>
    private readonly HashSet<Collider> _ownColliders = new HashSet<Collider>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Cache every collider in the weapon hierarchy so they can be skipped during hit scans.
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
            _ownColliders.Add(col);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the local client. Forwards the overlap check to the server using
    /// the current world position of the attack point.
    /// </summary>
    public void PerformHitScan(float damage)
    {
        PerformHitScanServerRpc(transform.position, damage);
    }

    // ── Server ─────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void PerformHitScanServerRpc(Vector3 attackOrigin, float damage, ServerRpcParams rpcParams = default)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            attackOrigin,
            hitRadius,
            OverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        Debug.Log($"[MeleeWeaponHitbox] OverlapSphere at {attackOrigin} radius={hitRadius} — {hitCount} colliders.", this);

        ClientRpcParams ownerParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        };

        bool anyNonSelfHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;

            // Skip colliders that belong to the weapon itself.
            if (_ownColliders.Contains(col))
                continue;

            Transform root = col.transform.root;

            // Skip all player colliders.
            if (root.CompareTag(PlayerTag))
                continue;

            anyNonSelfHit = true;

            if (!root.CompareTag(enemyTag))
                continue;

            MutantEnemy enemy = root.GetComponentInChildren<MutantEnemy>();
            if (enemy == null)
                continue;

            enemy.TakeDamage(damage, col.ClosestPoint(attackOrigin));
            Debug.Log($"[MeleeWeaponHitbox] Hit enemy '{root.name}' via '{col.name}' for {damage} damage.", this);

            NotifyHitClientRpc(ownerParams);
            return;
        }

        if (anyNonSelfHit)
            NotifyEnvironmentHitClientRpc(ownerParams);
    }

    // ── Client ─────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void NotifyHitClientRpc(ClientRpcParams clientRpcParams = default)
    {
        OnHit?.Invoke();
    }

    [ClientRpc]
    private void NotifyEnvironmentHitClientRpc(ClientRpcParams clientRpcParams = default)
    {
        OnEnvironmentHit?.Invoke();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
#endif
}
