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

        // Cache every collider in the entire weapon NetworkObject hierarchy so they can be
        // skipped during hit scans. Using GetComponentInParent<NetworkObject> ensures we
        // collect colliders from the root weapon GameObject (e.g. the shovel BoxCollider)
        // and all of its children, not just from this component's own subtree.
        NetworkObject weaponRoot = GetComponentInParent<NetworkObject>();
        Component searchRoot = weaponRoot != null ? (Component)weaponRoot : this;
        foreach (Collider col in searchRoot.GetComponentsInChildren<Collider>(true))
            _ownColliders.Add(col);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the local client (or server). Forwards the overlap check to the server
    /// using the current world position of the attack point. When already on the server
    /// (host player), the scan runs immediately without a network round-trip.
    /// </summary>
    public void PerformHitScan(float damage)
    {
        if (IsServer)
        {
            // Host player: skip the RPC and run directly, targeting the host's own client ID.
            PerformHitScanInternal(transform.position, damage, OwnerClientId);
        }
        else
        {
            PerformHitScanServerRpc(transform.position, damage);
        }
    }

    // ── Server ─────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void PerformHitScanServerRpc(Vector3 attackOrigin, float damage, ServerRpcParams rpcParams = default)
    {
        PerformHitScanInternal(attackOrigin, damage, rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// Runs the OverlapSphere hit scan on the server. Called either directly for host players
    /// or via <see cref="PerformHitScanServerRpc"/> for remote clients.
    /// </summary>
    private void PerformHitScanInternal(Vector3 attackOrigin, float damage, ulong senderClientId)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            attackOrigin,
            hitRadius,
            OverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        Debug.Log($"[MeleeWeaponHitbox] OverlapSphere at {attackOrigin} radius={hitRadius} — {hitCount} colliders. Sender={senderClientId}", this);

        ClientRpcParams ownerParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { senderClientId }
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

            // Walk up from the hit collider to find a MutantEnemy — no tag dependency.
            MutantEnemy enemy = col.GetComponentInParent<MutantEnemy>();
            if (enemy == null)
                continue;

            enemy.TakeDamage(damage, col.ClosestPoint(attackOrigin));
            Debug.Log($"[MeleeWeaponHitbox] Hit enemy '{enemy.name}' via '{col.name}' for {damage} damage.", this);

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
