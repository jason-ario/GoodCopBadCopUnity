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

    /// <summary>Radius of the swing OverlapSphere (centred on this transform).</summary>
    public float HitRadius => hitRadius;

    [Header("Hit Effects")]
    [Tooltip("Particle prefab instantiated at the hit position when an enemy or player is struck.")]
    [SerializeField] private ParticleSystem _hitEffectPrefab;

    [Tooltip("Particle prefab instantiated at the hit position when geometry (non-enemy) is struck.")]
    [SerializeField] private ParticleSystem _environmentHitEffectPrefab;

    [Tooltip("Where hit effects are spawned, e.g. the weapon's head/tip. Falls back to this transform if unassigned.")]
    [SerializeField] private Transform _hitEffectSpawnPoint;

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

    /// <summary>
    /// Fired on every NON-swinging client when another player's swing connects. Args: whether it
    /// was a geometry/prop hit (true) or a live target hit (false), and the world-space impact
    /// point. Lets the weapon play its impact sound as a spatial world sound for other players.
    /// </summary>
    public event Action<bool, Vector3> OnRemoteImpact;


    // Internal

    // Sized for busy scenes: every trigger volume nearby (interaction zones, task areas) plus an
    // enemy's limb hitboxes all land here. At 16 the target could be cut off and the swing missed.
    private static readonly Collider[] OverlapBuffer = new Collider[96];

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
        Prop        = 6,
        Shutter     = 7,
        Physics     = 8, // local only — gore / junk / soccer ball push, never sent to the server
    }

    /// <summary>Physics push strength of a melee hit on gore / junk / the soccer ball (pistol round = 1).</summary>
    private const float WeaponImpactStrength = 1.4f;

    /// <summary>Collider of the loose physics object the last <see cref="ResolveLocalHit"/> chose (HitKind.Physics).</summary>
    private Collider _physicsHitCollider;

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
        // does on this player's screen. Spawned at the authored effect point (typically the
        // weapon's head/tip) rather than the struck surface's closest point, so the effect always
        // reads as coming from the weapon making contact, not from the target's geometry.
        Vector3 effectOrigin = _hitEffectSpawnPoint != null ? _hitEffectSpawnPoint.position : attackOrigin;

        // Gore reads as a fleshy hit; a soccer ball or trash reads as hitting an object.
        bool physicsIsGore = kind == HitKind.Physics && CombatHitUtility.IsGoreTarget(_physicsHitCollider);
        bool isEnvironment = kind == HitKind.Environment || kind == HitKind.Prop || kind == HitKind.Shutter
                             || (kind == HitKind.Physics && !physicsIsGore);

        if (isEnvironment)
        {
            SpawnHitEffect(_environmentHitEffectPrefab, effectOrigin);
            OnEnvironmentHit?.Invoke();
        }
        else if (kind != HitKind.None)
        {
            SpawnHitEffect(_hitEffectPrefab, effectOrigin);
            OnHit?.Invoke();
        }

        // Relay the impact so every other client hears/sees it as a world event at the weapon tip.
        if (kind != HitKind.None)
        {
            if (IsServer) RelayImpact(isEnvironment, effectOrigin, NetworkManager.Singleton.LocalClientId);
            else          ReportImpactServerRpc(isEnvironment, effectOrigin);
        }

        if (kind == HitKind.Shutter)
        {
            // Cosmetic only: the shutter bang, played here instantly and relayed to everyone else.
            ShutterController.Instance?.OnHitByPlayerWeapon();

            if (IsServer) RelayShutterHit(NetworkManager.Singleton.LocalClientId);
            else          ReportShutterHitServerRpc();
            return;
        }

        if (kind == HitKind.Physics)
        {
            // Small push (gore also bleeds), networked by the object itself.
            CombatHitUtility.ApplyWeaponImpact(_physicsHitCollider, hitPoint, hitPoint - GetSwingerPosition(attackOrigin), WeaponImpactStrength);
            _physicsHitCollider = null;
            return;
        }

        if (kind == HitKind.Prop)
        {
            // Cosmetic prop reaction: play instantly here, then relay so other clients see it too.
            Vector3 swingDirection = hitPoint - attackOrigin;
            HittableProp.TryHitAt(hitPoint, swingDirection);

            if (IsServer) RelayPropHit(hitPoint, swingDirection, NetworkManager.Singleton.LocalClientId);
            else          ReportPropHitServerRpc(hitPoint, swingDirection);
            return;
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

        // Line-of-sight origin: the swinging player's eye. Every candidate below must be visible
        // from here, so the swing sphere can't reach through walls, intact glass or a closed shutter.
        Camera eyeCamera = Camera.main;
        bool checkLineOfSight = eyeCamera != null;
        Vector3 eye = checkLineOfSight ? eyeCamera.transform.position : attackOrigin;

        bool anyNonSelfHit = false;
        Vector3 firstNonSelfHitPosition = attackOrigin;
        bool propHit = false;
        Vector3 propHitPosition = attackOrigin;
        bool shutterHit = false;
        Vector3 shutterHitPosition = attackOrigin;
        _physicsHitCollider = null;
        Vector3 physicsHitPosition = attackOrigin;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null)
                continue;

            // Skip colliders that belong to the weapon itself.
            if (_ownColliders.Contains(col))
                continue;

            Transform root = col.transform.root;

            // Walk up from the hit collider to find a MutantEnemy or SuspectCharacter - no tag
            // dependency. This MUST be checked before the "Player" tag below: a resurrected
            // corpse (see CorpseResurrectionController) keeps its original "Player" tag and its
            // (now-dormant-turned-active) PlayerHealth component forever, so if the tag check ran
            // first every swing would be misclassified as friendly-fire on an already-dead
            // PlayerHealth (which silently no-ops all further damage) and the resurrected mutant
            // would never take damage or even flinch. Checking MutantEnemy/SuspectCharacter first
            // ensures an active mutant is always damaged as a mutant, tag notwithstanding.
            // CanBeDamagedByPlayers also admits dormant lineup mutants attacking the booth window.
            MutantEnemy enemy = col.GetComponentInParent<MutantEnemy>();
            if (enemy != null && enemy.CanBeDamagedByPlayers)
            {
                // Prefer the nearest LimbHitbox the swing overlapped over a broad body capsule, so
                // the hit point sits on the limb that was actually struck (drives LimbHitReactor).
                Collider limb = CombatHitUtility.FindNearestLimbInOverlap(OverlapBuffer, hitCount, enemy, attackOrigin, out Vector3 limbPoint);
                Vector3 bodyPoint = col.ClosestPoint(attackOrigin);

                if (limb != null && (!checkLineOfSight || HasLineOfSight(eye, limbPoint, limb, enemy)))
                    hitPoint = limbPoint;
                else if (!checkLineOfSight || HasLineOfSight(eye, bodyPoint, col, enemy))
                    hitPoint = bodyPoint;
                else
                    continue; // Behind a wall from the swinger's view; other colliders may still connect.

                target = enemy;
                return HitKind.Mutant;
            }

            // Dead bodies are reported too (death state is server-only); the server turns a hit on a
            // corpse into a cosmetic blood spray.
            SuspectCharacter suspect = col.GetComponentInParent<SuspectCharacter>();
            if (suspect != null)
            {
                Vector3 suspectPoint = SafeClosestPoint(col, attackOrigin);
                if (checkLineOfSight && !TryFindVisibleBodyPoint(eye, attackOrigin, col, suspect, ref suspectPoint))
                    continue;

                target   = suspect;
                hitPoint = suspectPoint;
                return HitKind.Suspect;
            }

            // Check for a fellow player hit (friendly fire).
            if (root.CompareTag(PlayerTag))
            {
                PlayerHealth playerHealth = col.GetComponentInParent<PlayerHealth>();
                if (playerHealth == null)
                    continue;

                // Skip the player who is swinging this weapon. A dead body is always hittable
                // (cosmetic blood only) — after a revive the old corpse is server-owned.
                NetworkObject playerNetObj = col.GetComponentInParent<NetworkObject>();
                if (playerNetObj != null && playerNetObj.OwnerClientId == localClientId && !playerHealth.IsDead)
                    continue;

                Vector3 playerPoint = col.ClosestPoint(attackOrigin);
                if (checkLineOfSight && !HasLineOfSight(eye, playerPoint, col, playerHealth))
                    continue;

                target   = playerHealth;
                hitPoint = playerPoint;
                return HitKind.Player;
            }

            // Loose physics objects (gore, junk, soccer ball): outrank plain geometry and props but
            // not live targets or glass, so they are only chosen after the loop.
            Component physicsTarget = CombatHitUtility.GetWeaponPhysicsTarget(col);
            if (physicsTarget != null)
            {
                if (_physicsHitCollider == null)
                {
                    Vector3 physicsPoint = SafeClosestPoint(col, attackOrigin);
                    if (!checkLineOfSight || HasLineOfSight(eye, physicsPoint, col, physicsTarget))
                    {
                        _physicsHitCollider = col;
                        physicsHitPosition  = physicsPoint;
                    }
                }
                continue;
            }

            // Everything below is only worth considering if the swinger can actually see it. The
            // glass pane is built from several colliders, so its own siblings mustn't block it.
            Vector3 surfacePoint = SafeClosestPoint(col, attackOrigin);
            BreakableGlassController glass = col.GetComponentInParent<BreakableGlassController>();
            if (checkLineOfSight && !HasLineOfSight(eye, surfacePoint, col, glass))
                continue;

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
                firstNonSelfHitPosition = surfacePoint;
            }

            // Cosmetic hittable props (signs etc.) outrank plain geometry but not live targets,
            // so they are only chosen after the loop if nothing more important was found.
            if (!propHit && !col.isTrigger && col.GetComponentInParent<HittableProp>() != null)
            {
                propHit = true;
                propHitPosition = surfacePoint;
            }

            // Breakable glass is a plain MonoBehaviour singleton, so there is no NetworkObject to
            // send — the server resolves it through BreakableGlassController.Instance, exactly as
            // the existing visual ClientRpcs below already do.
            if (glass != null && !glass.IsSmashed)
            {
                // A closed shutter on the swinger's side shields the pane: the swing lands on the shutter.
                if (glass.IsShieldedFrom(GetSwingerPosition(attackOrigin)))
                {
                    if (!shutterHit)
                    {
                        shutterHit = true;
                        shutterHitPosition = surfacePoint;
                    }
                    continue;
                }

                hitPoint = surfacePoint;
                return HitKind.Glass;
            }
        }

        if (_physicsHitCollider != null)
        {
            hitPoint = physicsHitPosition;
            return HitKind.Physics;
        }

        if (propHit)
        {
            hitPoint = propHitPosition;
            return HitKind.Prop;
        }

        // The shutter has no collider, so a swing reaching into the window opening (e.g. through
        // smashed glass) is tested geometrically: eye -> the far edge of the swing sphere.
        if (!shutterHit && checkLineOfSight)
        {
            BreakableGlassController booth = BreakableGlassController.Instance;
            Vector3 toTip = attackOrigin - eye;
            Vector3 reach = attackOrigin + (toTip.sqrMagnitude > 0.0001f ? toTip.normalized * hitRadius : Vector3.zero);
            if (booth != null && booth.TryGetShutterCrossing(eye, reach, out Vector3 crossing))
            {
                shutterHit = true;
                shutterHitPosition = crossing;
            }
        }

        if (shutterHit)
        {
            hitPoint = shutterHitPosition;
            return HitKind.Shutter;
        }

        if (anyNonSelfHit)
        {
            hitPoint = firstNonSelfHitPosition;
            return HitKind.Environment;
        }

        return HitKind.None;
    }


    /// <summary>
    /// Where the swinging player stands, used to tell which side of the booth pane the swing comes
    /// from. The weapon tip can poke through the thin pane, so the player's body is the reliable
    /// reference. Falls back to the weapon position when no local player object exists.
    /// </summary>
    private static Vector3 GetSwingerPosition(Vector3 fallback)
    {
        NetworkObject player = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null
            ? NetworkManager.Singleton.LocalClient.PlayerObject
            : null;
        return player != null ? player.transform.position : fallback;
    }

    /// <summary>Stop the line-of-sight ray this far short of the target point so the target's own surface never counts.</summary>
    private const float LineOfSightSkin = 0.05f;

    /// <summary>
    /// <see cref="Collider.ClosestPoint"/> only supports primitive and convex mesh colliders; for
    /// non-convex meshes fall back to the swing origin instead of warning.
    /// </summary>
    private static Vector3 SafeClosestPoint(Collider col, Vector3 position)
    {
        if (col is MeshCollider mesh && !mesh.convex) return position;
        return col.ClosestPoint(position);
    }

    private static readonly RaycastHit[] LineOfSightBuffer = new RaycastHit[32];

    /// <summary>
    /// Line-of-sight test against a body that tolerates furniture. A seated suspect (or one behind
    /// the booth counter) has a tall capsule whose point closest to the weapon often sits below the
    /// desk top, so the eye ray to it is blocked even though the torso is plainly visible. Tries
    /// the closest point first, then points on the body's centre line (swing height, centre, chest).
    /// All samples lie on the target itself, so walls, glass and shutters still block the swing.
    /// On success <paramref name="point"/> is the first visible sample.
    /// </summary>
    private bool TryFindVisibleBodyPoint(Vector3 eye, Vector3 attackOrigin, Collider col, Component owner, ref Vector3 point)
    {
        if (HasLineOfSight(eye, point, col, owner))
            return true;

        Bounds b = col.bounds;
        float swingHeight = Mathf.Clamp(attackOrigin.y, b.min.y, b.max.y);

        Vector3 atSwingHeight = new Vector3(b.center.x, swingHeight, b.center.z);
        if (HasLineOfSight(eye, atSwingHeight, col, owner)) { point = atSwingHeight; return true; }

        if (HasLineOfSight(eye, b.center, col, owner)) { point = b.center; return true; }

        Vector3 chest = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, 0.75f), b.center.z);
        if (HasLineOfSight(eye, chest, col, owner)) { point = chest; return true; }

        return false;
    }

    /// <summary>
    /// True when nothing solid sits between the swinger's <paramref name="eye"/> and
    /// <paramref name="point"/> on the candidate. Blockers are static/kinematic world geometry,
    /// intact booth glass, and the closed rolling shutter (which has no collider, so it is tested
    /// geometrically via <see cref="BreakableGlassController.IsLineBlockedByShutter"/>).
    /// </summary>
    private bool HasLineOfSight(Vector3 eye, Vector3 point, Collider targetCollider, Component targetOwner)
    {
        // The shutter-line test is for reaching THROUGH the window (things beyond it). When the target
        // is the pane itself, skip it: the pane's colliders extend past its centre plane toward the
        // shutter, so a swing from inside would always "cross" the plane and be wrongly blocked.
        // Which side the closed shutter shields is decided by BreakableGlassController.IsShieldedFrom.
        BreakableGlassController booth = BreakableGlassController.Instance;
        if (booth != null && !ReferenceEquals(targetOwner, booth) && booth.IsLineBlockedByShutter(eye, point))
            return false;

        Vector3 delta = point - eye;
        float distance = delta.magnitude - LineOfSightSkin;
        if (distance <= 0f)
            return true;

        int count = Physics.RaycastNonAlloc(eye, delta / delta.magnitude, LineOfSightBuffer, distance,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (BlocksLineOfSight(LineOfSightBuffer[i].collider, targetCollider, targetOwner))
                return false;
        }
        return true;
    }

    /// <summary>Whether <paramref name="col"/> counts as a wall for melee line of sight.</summary>
    private bool BlocksLineOfSight(Collider col, Collider targetCollider, Component targetOwner)
    {
        if (col == null || col == targetCollider || _ownColliders.Contains(col))
            return false;

        if (targetOwner != null && col.transform.IsChildOf(targetOwner.transform))
            return false;

        // Bodies (players, including the swinger and resurrected corpses, mutants, suspects) aren't walls.
        if (col.transform.root.CompareTag(PlayerTag)
            || col.GetComponentInParent<MutantEnemy>() != null
            || col.GetComponentInParent<SuspectCharacter>() != null)
            return false;

        // Loose physics objects (dropped items, documents) shouldn't eat a swing.
        Rigidbody body = col.attachedRigidbody;
        if (body != null && !body.isKinematic)
            return false;

        // Open mesh fences and cosmetic props don't block, matching the gun rules.
        if (CombatHitUtility.IsBulletPassthrough(col) || col.GetComponentInParent<HittableProp>() != null)
            return false;

        // Shards of a smashed window are debris, not a pane.
        BreakableGlassController glass = col.GetComponentInParent<BreakableGlassController>();
        if (glass != null && glass.IsSmashed)
            return false;

        return true;
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
        // Measured to the target's nearest collider, not its pivot, so big enemies (Ocho) keep limb hits.
        float distance = CombatHitUtility.DistanceToTarget(targetObj, hitPoint);
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

                if (enemy.IsDead)
                {
                    enemy.PlayCorpseHitEffect(hitPoint);
                    break;
                }

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
                if (suspect == null) return;

                if (suspect.IsDead)
                {
                    suspect.PlayCorpseHitEffect(hitPoint);
                    break;
                }

                suspect.TakeDamage(damage, hitPoint);
                Debug.Log($"[MeleeWeaponHitbox] Client {senderClientId} hit suspect '{suspect.name}' for {damage} damage.", this);
                break;
            }

            case HitKind.Player:
            {
                PlayerHealth playerHealth = FindOn<PlayerHealth>(targetObj);
                if (playerHealth == null) return;

                // Corpse hits are cosmetic only, so they skip the self-hit guard below.
                if (playerHealth.IsDead)
                {
                    playerHealth.PlayCorpseHitEffect(hitPoint);
                    break;
                }

                // Never let a swing hurt the player who threw it, whatever the client claims.
                if (targetObj.OwnerClientId == senderClientId) return;

                playerHealth.TakeDamage(damage, EffectKeys.FriendlyMeleeDamage, hitPoint);
                Debug.Log($"[MeleeWeaponHitbox] Friendly fire: client {senderClientId} hit player '{targetObj.name}' for {damage} damage.", this);
                break;
            }
        }
    }

    /// <summary>Server-side relay of a cosmetic <see cref="HittableProp"/> hit to every other client.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportPropHitServerRpc(Vector3 hitPoint, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        RelayPropHit(hitPoint, direction, rpcParams.Receive.SenderClientId);
    }

    private void RelayPropHit(Vector3 hitPoint, Vector3 direction, ulong senderClientId)
    {
        if (!IsServer) return;

        List<ulong> targets = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            if (id != senderClientId) targets.Add(id);

        if (targets.Count == 0) return;

        PropHitClientRpc(hitPoint, direction, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets }
        });
    }

    /// <summary>Server-side relay of a swing's impact feedback (sound + particles) to every other client.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportImpactServerRpc(bool isEnvironment, Vector3 point, ServerRpcParams rpcParams = default)
    {
        RelayImpact(isEnvironment, point, rpcParams.Receive.SenderClientId);
    }

    private void RelayImpact(bool isEnvironment, Vector3 point, ulong senderClientId)
    {
        if (!IsServer) return;

        List<ulong> targets = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            if (id != senderClientId) targets.Add(id);

        if (targets.Count == 0) return;

        ImpactClientRpc(isEnvironment, point, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets }
        });
    }

    /// <summary>Server-side relay of a swing hitting the closed shutter to every other client.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportShutterHitServerRpc(ServerRpcParams rpcParams = default)
    {
        RelayShutterHit(rpcParams.Receive.SenderClientId);
    }

    private void RelayShutterHit(ulong senderClientId)
    {
        if (!IsServer) return;

        List<ulong> targets = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            if (id != senderClientId) targets.Add(id);

        if (targets.Count == 0) return;

        ShutterHitClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets }
        });
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

    /// <summary>Replays a cosmetic <see cref="HittableProp"/> reaction on non-swinging clients.</summary>
    [ClientRpc]
    private void PropHitClientRpc(Vector3 hitPoint, Vector3 direction, ClientRpcParams clientRpcParams = default)
    {
        HittableProp.TryHitAt(hitPoint, direction);
    }

    /// <summary>Plays the shutter bang on non-swinging clients when a swing hits the closed shutter.</summary>
    [ClientRpc]
    private void ShutterHitClientRpc(ClientRpcParams clientRpcParams = default)
    {
        ShutterController.Instance?.OnHitByPlayerWeapon();
    }

    /// <summary>Replays a swing's impact particles and world sound on non-swinging clients.</summary>
    [ClientRpc]
    private void ImpactClientRpc(bool isEnvironment, Vector3 point, ClientRpcParams clientRpcParams = default)
    {
        SpawnHitEffect(isEnvironment ? _environmentHitEffectPrefab : _hitEffectPrefab, point);
        OnRemoteImpact?.Invoke(isEnvironment, point);
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
