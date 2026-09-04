using GoodCopBadCop.Effects;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Performs an OverlapSphere at the attack point and damages any enemy or fellow player found
/// within range (friendly fire enabled). Attach to an AttackPoint child of the melee weapon prefab.
///
/// HIT DETECTION IS CLIENT-AUTHORITATIVE. <see cref="PerformHitScan"/> runs the overlap on the
/// swinging player's own machine, against the world exactly as that player sees it, and reports the
/// resolved target to the server via <see cref="ReportHitServerRpc"/>. The server then applies the
/// consequences (damage, glass hits) to whatever the client says it struck — it does NOT re-run the
/// geometry test.
///
/// Why: the scan used to run server-side against the position the client *reported* its weapon at.
/// The server's copy of a charging mutant has already moved by the time that message lands, so a
/// remote player's swing that visibly connected on their screen frequently missed, while the host —
/// scanned locally with zero delay — never lost a hit. Damage, health and death stay authoritative
/// on the server, but WHAT was hit is decided where the player actually swung, so a swing that
/// connects on screen always connects, and impact feedback is instant with no round trip.
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

    /// <summary>
    /// Sanity limit (metres) between the hit point the client reported and the target it claims to
    /// have hit, checked server-side. Client-resolved hits are trusted, but not blindly: this
    /// rejects a report that could only come from a bug or tampering, without ever second-guessing a
    /// legitimate swing (it is several times <see cref="hitRadius"/>).
    /// </summary>
    private const float MaxReportedHitDistance = 5f;


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

    /// <summary>What a locally-resolved swing connected with. Sent to the server as a byte.</summary>
    private enum HitKind : byte
    {
        None        = 0,
        Environment = 1,
        Mutant      = 2,
        Suspect     = 3,
        Player      = 4,
        Glass       = 5,
    }

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
    /// Called on the swinging player's machine (client or host). Resolves the hit locally against
    /// what that player sees, plays impact feedback immediately, and reports the result to the
    /// server so it can apply the damage.
    /// </summary>
    public void PerformHitScan(float damage)
    {
        Vector3 attackOrigin = transform.position;

        HitKind kind = ResolveLocalHit(attackOrigin, out NetworkBehaviour target, out Vector3 hitPoint);

        // Immediate local feedback — no round trip, so the swing reads as connecting the instant it
        // does on this player's screen.
        if (kind == HitKind.Environment)
        {
            SpawnHitEffect(_environmentHitEffectPrefab, hitPoint);
            OnEnvironmentHit?.Invoke();
        }
        else if (kind != HitKind.None)
        {
            SpawnHitEffect(_hitEffectPrefab, hitPoint);
            OnHit?.Invoke();
        }

        if (kind == HitKind.None || kind == HitKind.Environment)
            return;

        NetworkObjectReference targetRef = target != null && target.NetworkObject != null
            ? new NetworkObjectReference(target.NetworkObject)
            : default;

        if (IsServer)
            ApplyHit(kind, targetRef, attackOrigin, hitPoint, damage, OwnerClientId);
        else
            ReportHitServerRpc((byte)kind, targetRef, attackOrigin, hitPoint, damage);
    }


    // Local resolution (runs on the swinging player's machine)

    /// <summary>
    /// Runs the OverlapSphere locally and returns the first meaningful thing the swing connected
    /// with, in the original priority order: fellow player > breakable glass > mutant > suspect >
    /// plain geometry.
    /// </summary>
    private HitKind ResolveLocalHit(Vector3 attackOrigin, out NetworkBehaviour target, out Vector3 hitPoint)
    {
        target   = null;
        hitPoint = attackOrigin;

        int hitCount = Physics.OverlapSphereNonAlloc(
            attackOrigin,
            hitRadius,
            OverlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

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
                if (playerNetObj != null && playerNetObj.OwnerClientId == localClientId)
                    continue;

                PlayerHealth playerHealth = col.GetComponentInParent<PlayerHealth>();
                if (playerHealth == null)
                    continue;

                target   = playerHealth;
                hitPoint = col.ClosestPoint(attackOrigin);
                return HitKind.Player;
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

            // Breakable glass is a plain MonoBehaviour singleton, so there is no NetworkObject to
            // send — the server resolves it through BreakableGlassController.Instance, exactly as
            // the existing visual ClientRpcs below already do.
            BreakableGlassController glass = col.GetComponentInParent<BreakableGlassController>();
            if (glass != null && !glass.IsSmashed)
            {
                hitPoint = col.ClosestPoint(attackOrigin);
                return HitKind.Glass;
            }

            // Walk up from the hit collider to find a MutantEnemy or SuspectCharacter - no tag dependency.
            MutantEnemy enemy = col.GetComponentInParent<MutantEnemy>();
            SuspectCharacter suspect = col.GetComponentInParent<SuspectCharacter>();

            if (enemy == null && suspect == null)
                continue;

            if (enemy != null)
            {
                target   = enemy;
                hitPoint = col.ClosestPoint(attackOrigin);
                return HitKind.Mutant;
            }

            if (!suspect.IsDead)
            {
                target   = suspect;
                hitPoint = col.ClosestPoint(attackOrigin);
                return HitKind.Suspect;
            }
        }

        if (anyNonSelfHit)
        {
            hitPoint = firstNonSelfHitPosition;
            return HitKind.Environment;
        }

        return HitKind.None;
    }


    // Server

    /// <summary>
    /// Applies a hit the swinging client already resolved. RequireOwnership = false because
    /// ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportHitServerRpc(byte kind, NetworkObjectReference targetRef, Vector3 attackOrigin,
        Vector3 hitPoint, float damage, ServerRpcParams rpcParams = default)
    {
        ApplyHit((HitKind)kind, targetRef, attackOrigin, hitPoint, damage, rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// Server-side consequence of a client-resolved hit. Trusts WHAT was hit; still owns the damage,
    /// health and death rules, and never lets a swing hurt the player who threw it.
    /// </summary>
    private void ApplyHit(HitKind kind, NetworkObjectReference targetRef, Vector3 attackOrigin,
        Vector3 hitPoint, float damage, ulong senderClientId)
    {
        if (!IsServer) return;

        if (kind == HitKind.Glass)
        {
            BreakableGlassController glass = BreakableGlassController.Instance;
            if (glass == null || glass.IsSmashed) return;

            int newHits = glass.RegisterHit();
            Debug.Log($"[MeleeWeaponHitbox] Client {senderClientId} hit breakable glass at {hitPoint}. Hits={newHits}", this);

            if (glass.IsSmashed) SmashGlassClientRpc();
            else                 UpdateGlassClientRpc(newHits);
            return;
        }

        if (!targetRef.TryGet(out NetworkObject targetObj) || targetObj == null)
        {
            // The target despawned between the client's swing and this message — nothing to hurt.
            return;
        }

        // Sanity bound, NOT a hit test: rejects impossible reports without re-validating the swing.
        float distance = Vector3.Distance(targetObj.transform.position, hitPoint);
        if (distance > MaxReportedHitDistance)
        {
            Debug.LogWarning($"[MeleeWeaponHitbox] Discarding hit report from client {senderClientId} — reported hit point is {distance:F1}m from '{targetObj.name}'.", this);
            return;
        }

        switch (kind)
        {
            case HitKind.Mutant:
            {
                MutantEnemy enemy = FindOn<MutantEnemy>(targetObj);
                if (enemy == null) return;

                // Knockback direction comes from the swing the CLIENT reported, so the shove always
                // matches the angle the player actually struck from.
                Vector3 knockbackDirection = hitPoint - attackOrigin;
                enemy.TakeDamage(damage, hitPoint, knockbackDirection: knockbackDirection);
                Debug.Log($"[MeleeWeaponHitbox] Client {senderClientId} hit enemy '{enemy.name}' for {damage} damage.", this);
                break;
            }

            case HitKind.Suspect:
            {
                SuspectCharacter suspect = FindOn<SuspectCharacter>(targetObj);
                if (suspect == null || suspect.IsDead) return;

                suspect.TakeDamage(damage, hitPoint);
                Debug.Log($"[MeleeWeaponHitbox] Client {senderClientId} hit suspect '{suspect.name}' for {damage} damage.", this);
                break;
            }

            case HitKind.Player:
            {
                // Never let a swing hurt the player who threw it, whatever the client claims.
                if (targetObj.OwnerClientId == senderClientId) return;

                PlayerHealth playerHealth = FindOn<PlayerHealth>(targetObj);
                if (playerHealth == null) return;

                playerHealth.TakeDamage(damage, EffectKeys.FriendlyMeleeDamage);
                Debug.Log($"[MeleeWeaponHitbox] Friendly fire: client {senderClientId} hit player '{targetObj.name}' for {damage} damage.", this);
                break;
            }
        }
    }

    /// <summary>Resolves a component on a reported target, tolerating it living on a child of the NetworkObject.</summary>
    private static T FindOn<T>(NetworkObject netObj) where T : Component
    {
        T component = netObj.GetComponent<T>();
        return component != null ? component : netObj.GetComponentInChildren<T>();
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
