using GoodCopBadCop.Effects;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Performs an OverlapSphere at the attack point and damages any enemy or fellow player found
/// within range (friendly fire enabled). Attach to an AttackPoint child of the melee weapon prefab.
/// Call <see cref="PerformHitScan"/> from the weapon owner's client; the server validates
/// and applies damage, then notifies the owner via <see cref="OnHit"/> or
/// <see cref="OnEnvironmentHit"/> depending on what was struck.
/// Optionally assign <see cref="_hitEffectPrefab"/> and <see cref="_environmentHitEffectPrefab"/>
/// to spawn a particle effect at the exact world-space hit position on the owning client.
/// </summary>
public class MeleeWeaponHitbox : NetworkBehaviour
{

    // Configuration

    [Tooltip("Radius of the OverlapSphere centered on this transform.")]
    [SerializeField] private float hitRadius = 0.8f;

    [Header("Hit Effects")]
    [Tooltip("Particle prefab instantiated at the hit position when an enemy or player is struck.")]
    [SerializeField] private ParticleSystem _hitEffectPrefab;

    [Tooltip("Particle prefab instantiated at the hit position when geometry (non-enemy) is struck.")]
    [SerializeField] private ParticleSystem _environmentHitEffectPrefab;

    private const string PlayerTag = "Player";


    // Events

    /// <summary>
    /// Fired on the owning client when the overlap successfully finds an enemy.
    /// </summary>
    public event Action OnHit;

    /// <summary>
    /// Fired on the owning client when the sphere overlaps geometry but no enemy.
    /// </summary>
    public event Action OnEnvironmentHit;


    // Internal

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


    // Public API

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


    // Server

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

        Debug.Log($"[MeleeWeaponHitbox] OverlapSphere at {attackOrigin} radius={hitRadius} - {hitCount} colliders. Sender={senderClientId}", this);

        ClientRpcParams ownerParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { senderClientId }
            }
        };

        bool anyNonSelfHit = false;
        Vector3 firstNonSelfHitPosition = attackOrigin;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;

            // Skip colliders that belong to the weapon itself.
            if (_ownColliders.Contains(col))
                continue;

            Transform root = col.transform.root;

            // Check for a fellow player hit (friendly fire).
            if (root.CompareTag(PlayerTag))
            {
                // Skip the player who is swinging this weapon.
                NetworkObject playerNetObj = col.GetComponentInParent<NetworkObject>();
                if (playerNetObj != null && playerNetObj.OwnerClientId == senderClientId)
                    continue;

                PlayerHealth playerHealth = col.GetComponentInParent<PlayerHealth>();
                if (playerHealth == null)
                    continue;

                anyNonSelfHit = true;
                playerHealth.TakeDamage(damage, EffectKeys.FriendlyMeleeDamage);
                Debug.Log($"[MeleeWeaponHitbox] Friendly fire: hit player '{root.name}' for {damage} damage.", this);
                NotifyHitClientRpc(col.ClosestPoint(attackOrigin), ownerParams);
                return;
            }

            // Track the closest surface point of the first non-self, non-weapon, non-trigger
            // collider so environment hits have a meaningful spawn position. Trigger colliders
            // are excluded here because this project uses them extensively for non-physical logic
            // volumes (interaction zones, click detectors, task areas, etc.) — those aren't real
            // geometry and shouldn't register as a "clang" when the swing merely passes near one.
            // Colliders that ARE meaningful hits despite being triggers (glass, enemies, suspects)
            // are still handled explicitly below regardless of this skip.
            if (!anyNonSelfHit && !col.isTrigger)
            {
                anyNonSelfHit = true;
                firstNonSelfHitPosition = col.ClosestPoint(attackOrigin);
            }

            // Check for breakable glass — registers the hit server-side then broadcasts visuals
            // to all clients via ClientRpc, mirroring MutantSuspectBehaviour's glass attack pattern.
            BreakableGlassController glass = col.GetComponentInParent<BreakableGlassController>();
            if (glass != null && !glass.IsSmashed)
            {
                int newHits = glass.RegisterHit();
                Vector3 glassHitPos = col.ClosestPoint(attackOrigin);
                Debug.Log($"[MeleeWeaponHitbox] Hit breakable glass at {glassHitPos}. Hits={newHits}", this);
                if (glass.IsSmashed)
                    SmashGlassClientRpc();
                else
                    UpdateGlassClientRpc(newHits);
                NotifyHitClientRpc(glassHitPos, ownerParams);
                return;
            }

            // Walk up from the hit collider to find a MutantEnemy or SuspectCharacter - no tag dependency.
            MutantEnemy enemy = col.GetComponentInParent<MutantEnemy>();
            SuspectCharacter suspect = col.GetComponentInParent<SuspectCharacter>();

            if (enemy == null && suspect == null)
                continue;

            if (enemy != null)
            {
                Vector3 enemyHitPosition = col.ClosestPoint(attackOrigin);
                Vector3 knockbackDirection = enemyHitPosition - attackOrigin;
                enemy.TakeDamage(damage, enemyHitPosition, knockbackDirection: knockbackDirection);
                Debug.Log($"[MeleeWeaponHitbox] Hit enemy '{enemy.name}' via '{col.name}' for {damage} damage.", this);
                NotifyHitClientRpc(enemyHitPosition, ownerParams);
                return;
            }

            if (!suspect.IsDead)
            {
                Vector3 suspectHitPosition = col.ClosestPoint(attackOrigin);
                suspect.TakeDamage(damage, suspectHitPosition);
                Debug.Log($"[MeleeWeaponHitbox] Hit suspect '{suspect.name}' via '{col.name}' for {damage} damage.", this);
                NotifyHitClientRpc(suspectHitPosition, ownerParams);
                return;
            }
        }

        if (anyNonSelfHit)
            NotifyEnvironmentHitClientRpc(firstNonSelfHitPosition, ownerParams);
    }


    // Client

    /// <summary>
    /// Received by all clients when the player lands an intermediate melee hit on the glass.
    /// Mirrors UpdateGlassClientRpc on MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void UpdateGlassClientRpc(int hitCount)
    {
        BreakableGlassController.Instance?.OnHitByMutant(hitCount);
    }

    /// <summary>
    /// Received by all clients when the player's melee strike fully smashes the glass.
    /// Mirrors SmashGlassClientRpc on MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void SmashGlassClientRpc()
    {
        BreakableGlassController.Instance?.ApplySmash();
    }

    [ClientRpc]
    private void NotifyHitClientRpc(Vector3 hitPosition, ClientRpcParams clientRpcParams = default)
    {
        SpawnHitEffect(_hitEffectPrefab, hitPosition);
        OnHit?.Invoke();
    }

    [ClientRpc]
    private void NotifyEnvironmentHitClientRpc(Vector3 hitPosition, ClientRpcParams clientRpcParams = default)
    {
        SpawnHitEffect(_environmentHitEffectPrefab, hitPosition);
        OnEnvironmentHit?.Invoke();
    }


    // Effects

    /// <summary>
    /// Instantiates <paramref name="prefab"/> at <paramref name="position"/> and destroys it
    /// automatically once the particle system has finished playing.
    /// </summary>
    private static void SpawnHitEffect(ParticleSystem prefab, Vector3 position)
    {
        if (prefab == null) return;

        ParticleSystem instance = Instantiate(prefab, position, Quaternion.identity);

        // Determine lifetime from the main module so we never leak instances.
        ParticleSystem.MainModule main = instance.main;
        float lifetime = main.duration + main.startLifetime.constantMax;
        Destroy(instance.gameObject, Mathf.Max(lifetime, 0.1f));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
#endif
}
