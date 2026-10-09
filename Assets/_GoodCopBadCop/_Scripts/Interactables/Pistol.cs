using System;
using System.Collections;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A semi-automatic pistol with networked ammo tracking.
///
/// LMB (while held) fires one round, playing muzzle VFX, camera impulse, recoil, and a
/// "Shoot" animation trigger. Firing is blocked when <see cref="_roundsRemaining"/> reaches
/// zero — a dry-fire click sound plays instead.
///
/// R (while held) reloads the magazine from the holder's <see cref="PlayerAmmoReserve"/>.
/// Ammo enters the reserve by left-clicking a held <see cref="PistolAmmo"/> clip.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - CinemachineImpulseSource on the root
///   - Collider on the Interactable layer
///   - "Item Data" field → Pistol.asset
///   - "_shootVFX" → child ParticleSystem at the muzzle point
///   - "_muzzleFlashLight" → child Light GameObject, starts inactive
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Pistol : PickableObject, IAmmoProvider, IInventoryReloadable
{
    /// <summary>Maximum number of rounds the pistol can hold.</summary>
    public const int MaxRounds = 30;

    [Header("Pistol — VFX")]
    [Tooltip("Particle system at the muzzle point, played on every shot.")]
    [SerializeField] private ParticleSystem _shootVFX;

    [Tooltip("Cinemachine impulse source used to shake the camera on fire.")]
    [SerializeField] private CinemachineImpulseSource _cinemachineImpulseSource;

    [Tooltip("Light child at the muzzle, briefly activated on fire.")]
    [SerializeField] private GameObject _muzzleFlashLight;

    [Tooltip("Duration the muzzle flash light stays on, in seconds.")]
    [SerializeField] private float _lightOnTime = 0.08f;

    [Tooltip("Cosmetic bullet prefab spawned locally on every shot. Requires a BulletVisual component.")]
    [SerializeField] private GameObject _bulletVisualPrefab;

    [Header("Pistol — Combat")]
    [Tooltip("Damage dealt to a MutantEnemy per bullet.")]
    [SerializeField] private float _damage = 25f;

    [Tooltip("Maximum hitscan range in metres.")]
    [SerializeField] private float _bulletRange = 150f;

    /// <summary>Hitscan range in metres. Read by the reticle to show the enemy-in-range color.</summary>
    public float BulletRange => _bulletRange;

    [Header("Pistol — Audio")]
    [Tooltip("Gunshot sound played on every client when a round is fired.")]
    [SerializeField] private AudioClip _shootSound;

    [Tooltip("Dry-fire click played locally when the magazine is empty.")]
    [SerializeField] private AudioClip _emptySound;

    [Tooltip("Sound played on every client when the pistol is reloaded.")]
    [SerializeField] private AudioClip _reloadSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _roundsRemaining = new(
        MaxRounds,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current number of rounds loaded in the pistol.</summary>
    public int RoundsRemaining => _roundsRemaining.Value;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _roundsRemaining.Value;
    public float MaxAmmo => MaxRounds;
    public event Action OnAmmoChanged;

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = _roundsRemaining.Value;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _roundsRemaining.Value = Mathf.Clamp(Mathf.RoundToInt(data.ResourceAmount), 0, MaxRounds);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        // The source VFX prefab has stopAction = Destroy, which would destroy the muzzle flash
        // GameObject after its first shot. Override it to None so the system can be replayed.
        if (_shootVFX != null)
        {
            ParticleSystem.MainModule main = _shootVFX.main;
            main.stopAction = ParticleSystemStopAction.None;
        }

        UpdateInteractText(MaxRounds);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _roundsRemaining.OnValueChanged += OnRoundsChanged;

        // Server initialises the authoritative count so late-joining clients replicate correctly.
        if (IsServer)
            _roundsRemaining.Value = MaxRounds;

        // Sync text immediately — OnValueChanged won't fire when value equals the NetworkVariable default.
        UpdateInteractText(_roundsRemaining.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _roundsRemaining.OnValueChanged -= OnRoundsChanged;
    }

    private void OnRoundsChanged(int previous, int current)
    {
        UpdateInteractText(current);
        OnAmmoChanged?.Invoke();
    }

    private void UpdateInteractText(int rounds)
        => interactText = $"Pistol ({rounds}/{MaxRounds})";

    // ── Firing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the owner's client when LMB fires.
    /// Plays VFX, camera impulse, and recoil immediately (no network round-trip), then asks
    /// the server to decrement the counter and relay VFX to all other clients.
    /// Plays a dry-fire click when the magazine is empty.
    /// </summary>
    /// <summary>LMB fires the pistol.</summary>
    public override string GetHeldUseVerb() => "Shoot";

    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_roundsRemaining.Value <= 0)
        {
            if (_emptySound != null)
                SFXController.Instance.PlayAtPosition(_emptySound, transform.position);
            return;
        }

        Camera cam = Camera.main;
        PlayShootFX(cam.transform.forward);
        playerPickupController.PlayerAnimationController.SetAnimTrigger("Shoot");
        _cinemachineImpulseSource?.GenerateImpulse();

        PlayerMovementController movement = playerPickupController.GetComponent<PlayerMovementController>();
        movement?.ApplyRecoil();

        // Client-authoritative hitscan: the raycast runs here, against the world exactly as this
        // player sees it down their own sights. The server used to re-raycast from the reported
        // origin, which meant a shot at a moving mutant was tested against a position the target had
        // already left — the shot looked like a hit locally and did nothing. See FireServerRpc.
        FireHit hit = ResolveShot(cam.transform.position, cam.transform.forward);

        // Cosmetic prop reaction plays instantly for the shooter; the server relays it to others.
        if (hit.Kind == ShotKind.Prop)
            HittableProp.TryHitAt(hit.Point, cam.transform.forward);

        FireServerRpc(cam.transform.forward, (byte)hit.Kind, hit.TargetRef, hit.Point);
    }

    /// <summary>What a locally-resolved shot connected with. Sent to the server as a byte.</summary>
    private enum ShotKind : byte
    {
        None    = 0,
        Mutant  = 1,
        Player  = 2,
        Glass   = 3,
        Prop    = 4,
        Suspect = 5,
    }

    private readonly struct FireHit
    {
        public readonly ShotKind Kind;
        public readonly NetworkObjectReference TargetRef;
        public readonly Vector3 Point;

        public FireHit(ShotKind kind, NetworkObjectReference targetRef, Vector3 point)
        {
            Kind      = kind;
            TargetRef = targetRef;
            Point     = point;
        }
    }

    /// <summary>
    /// Runs the hitscan locally on the shooter's machine and classifies what it struck, in the same
    /// priority order the server used to: mutant > fellow player > breakable glass.
    /// </summary>
    private FireHit ResolveShot(Vector3 rayOrigin, Vector3 rayDirection)
    {
        // Uses RaycastAll (sorted by distance) instead of a plain Raycast so trigger colliders can
        // be inspected without letting them silently block the shot. A resurrected corpse (see
        // CorpseResurrectionController) exposes its ragdoll limb colliders as trigger hitboxes so
        // individual limbs/body parts can be hit — but this project also uses trigger colliders
        // everywhere for non-physical logic volumes (interaction zones, click detectors, task
        // areas, etc.), and those must stay completely transparent to bullets, exactly like before.
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, _bulletRange, Physics.AllLayers, QueryTriggerInteraction.Collide);
        if (hits.Length == 0)
            return new FireHit(ShotKind.None, default, rayOrigin);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            // IsActive matters here: every Player prefab carries a dormant MutantEnemy (see
            // CorpseResurrectionController) so it can later resurrect into a chasing mutant while
            // still alive/uninfected. Without the IsActive check, a shot at a perfectly living
            // player would always be misclassified as a mutant hit and silently swallowed by
            // MutantEnemy's own dormancy guard, never reaching PlayerHealth at all.
            // CanBeDamagedByPlayers = IsActive, plus dormant lineup mutants attacking the booth window.
            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null && enemy.CanBeDamagedByPlayers)
            {
                if (enemy.NetworkObject != null)
                {
                    // If the ray first grazed a broad body capsule, use the LimbHitbox behind it so
                    // the hit point lands on the actual limb (drives LimbHitReactor reactions).
                    int limbIndex = CombatHitUtility.PreferLimbAlongRay(hits, i, enemy);
                    return new FireHit(ShotKind.Mutant, new NetworkObjectReference(enemy.NetworkObject), hits[limbIndex].point);
                }
                continue;
            }

            // Living subjects / world NPCs (Soldier, Vlad, guards). Shots are harmless: they only
            // trigger a flinch on the server (see SuspectCharacter.PlayHarmlessHitReaction).
            SuspectCharacter suspect = hit.collider.GetComponentInParent<SuspectCharacter>();
            if (suspect != null && !suspect.IsDead && suspect.NetworkObject != null)
                return new FireHit(ShotKind.Suspect, new NetworkObjectReference(suspect.NetworkObject), hit.point);

            // An irrelevant trigger (interaction zone, click detector, task area, etc.) — bullets
            // must pass straight through it, exactly as if QueryTriggerInteraction.Collide had
            // never been requested.
            if (hit.collider.isTrigger)
                continue;

            // Fences and gates are open mesh — shots pass through them, matching melee weapons.
            if (CombatHitUtility.IsBulletPassthrough(hit.collider))
                continue;

            if (hit.collider.transform.root.CompareTag("Player"))
            {
                NetworkObject playerNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                PlayerHealth playerHealth  = hit.collider.GetComponentInParent<PlayerHealth>();

                // Skip the shooter's own body so hitting yourself never registers damage.
                if (playerNetObj != null && playerHealth != null && playerNetObj.OwnerClientId != localClientId)
                    return new FireHit(ShotKind.Player, new NetworkObjectReference(playerNetObj), hit.point);

                return new FireHit(ShotKind.None, default, hit.point);
            }

            BreakableGlassController glass = hit.collider.GetComponentInParent<BreakableGlassController>();
            if (glass != null && !glass.IsSmashed)
            {
                // A closed shutter on the shooter's side shields the pane: the shot stops dead.
                if (glass.IsShieldedFrom(rayOrigin))
                    return new FireHit(ShotKind.None, default, hit.point);
                return new FireHit(ShotKind.Glass, default, hit.point);
            }

            if (hit.collider.GetComponentInParent<HittableProp>() != null)
                return new FireHit(ShotKind.Prop, default, hit.point);

            // Solid geometry that isn't anything special — this blocks the shot.
            return new FireHit(ShotKind.None, default, hit.point);
        }

        return new FireHit(ShotKind.None, default, rayOrigin);
    }

    /// <summary>
    /// The body slot Pistol (inactive, pre-placed in the body arm container) receives this
    /// call from PlayerPickupController. VFX for other clients is handled by
    /// <see cref="PlayShootFXClientRpc"/> inside <see cref="FireServerRpc"/>, so this is
    /// intentionally a no-op.
    /// </summary>
    public override void OnBodyStartUse() { }

    private void PlayShootFX(Vector3 direction)
    {
        if (_shootVFX != null)
        {
            _shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _shootVFX.Play();
        }

        if (_shootSound != null)
            SFXController.Instance.PlayAtPosition(_shootSound, transform.position);

        if (_muzzleFlashLight != null)
            StartCoroutine(MuzzleFlashCoroutine());

        if (_bulletVisualPrefab != null)
        {
            Vector3 muzzlePos = _shootVFX != null ? _shootVFX.transform.position : transform.position;
            BulletVisual bullet = Instantiate(_bulletVisualPrefab).GetComponent<BulletVisual>();
            bullet?.Initialize(muzzlePos, direction);
        }
    }

    private IEnumerator MuzzleFlashCoroutine()
    {
        _muzzleFlashLight.SetActive(true);
        yield return new WaitForSeconds(_lightOnTime);
        _muzzleFlashLight.SetActive(false);
    }

    // ── Fire Server RPC ───────────────────────────────────────────────────────

    /// <summary>
    /// Server-side: validates the sender is the current holder and has rounds remaining,
    /// decrements <see cref="_roundsRemaining"/>, applies the hit the CLIENT resolved (see
    /// <see cref="ResolveShot"/>), then relays shoot VFX to all other clients.
    /// The server no longer re-raycasts: it trusts what the shooter's own machine says it hit, so a
    /// shot that visibly connected can never be thrown away because the target had already moved on
    /// the server. Damage/health/death remain server-owned, and the sanity bound below rejects
    /// impossible reports.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayDirection, byte shotKind, NetworkObjectReference targetRef,
        Vector3 hitPoint, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        // Only the client actually holding this pistol may fire.
        // NOTE: PlayerPickupController.HeldObject is a plain field that is only ever set on the
        // owning client's own machine, so it reads as null here on the server for any
        // non-host client — checking it would silently drop every remote client's shots
        // (no damage, no ammo consumed, no VFX relayed). HeldObjectRef is backed by a
        // NetworkVariable and is therefore reliably replicated to the server.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null) return;
        if (!ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj) || heldNetObj != NetworkObject) return;

        if (_roundsRemaining.Value <= 0) return;

        _roundsRemaining.Value--;

        ApplyShot((ShotKind)shotKind, targetRef, hitPoint, rayDirection, clientId);

        // Relay VFX to every other connected client; shooter already played it locally.
        List<ulong> others = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != clientId)
                others.Add(id);
        }

        if (others.Count > 0)
        {
            PlayShootFXClientRpc(rayDirection, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = others }
            });
        }
    }

    /// <summary>
    /// Applies the consequence of a client-resolved shot. Server only.
    /// </summary>
    private void ApplyShot(ShotKind kind, NetworkObjectReference targetRef, Vector3 hitPoint,
        Vector3 rayDirection, ulong shooterClientId)
    {
        if (kind == ShotKind.None) return;

        if (kind == ShotKind.Glass)
        {
            BreakableGlassController glass = BreakableGlassController.Instance;
            if (glass == null || glass.IsSmashed) return;

            int newHits = glass.RegisterHit();
            Debug.Log($"[Pistol] Client {shooterClientId} shot breakable glass at {hitPoint}. Hits={newHits}");

            if (glass.IsSmashed) PistolSmashGlassClientRpc();
            else                 PistolUpdateGlassClientRpc(newHits);
            return;
        }

        if (kind == ShotKind.Prop)
        {
            // Cosmetic only — replay on every client except the shooter (who already played it).
            List<ulong> propTargets = new List<ulong>();
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                if (id != shooterClientId) propTargets.Add(id);

            if (propTargets.Count > 0)
                PropHitClientRpc(hitPoint, rayDirection, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = propTargets }
                });
            return;
        }

        if (!targetRef.TryGet(out NetworkObject targetObj) || targetObj == null)
            return; // Target despawned between the shot and this message.

        // Sanity bound, NOT a hit test — rejects a report that could only come from a bug.
        // Measured to the target's nearest collider, not its pivot, so big enemies (Ocho) keep limb hits.
        if (CombatHitUtility.DistanceToTarget(targetObj, hitPoint) > MaxReportedHitDistance)
        {
            Debug.LogWarning($"[Pistol] Discarding shot report from client {shooterClientId} — reported hit point is far from '{targetObj.name}'.");
            return;
        }

        if (kind == ShotKind.Mutant)
        {
            MutantEnemy enemy = targetObj.GetComponent<MutantEnemy>() ?? targetObj.GetComponentInChildren<MutantEnemy>();
            enemy?.TakeDamage(_damage, hitPoint, knockbackDirection: rayDirection);
            return;
        }

        if (kind == ShotKind.Suspect)
        {
            SuspectCharacter suspect = targetObj.GetComponent<SuspectCharacter>() ?? targetObj.GetComponentInChildren<SuspectCharacter>();
            suspect?.PlayHarmlessHitReaction(hitPoint);
            return;
        }

        if (kind == ShotKind.Player)
        {
            // Never let a shot hurt the shooter, whatever the client claims.
            if (targetObj.OwnerClientId == shooterClientId) return;

            PlayerHealth playerHealth = targetObj.GetComponent<PlayerHealth>() ?? targetObj.GetComponentInChildren<PlayerHealth>();
            playerHealth?.TakeDamage(_damage, EffectKeys.FriendlyGunshotDamage, hitPoint);
        }
    }

    /// <summary>
    /// Sanity limit (metres) between the hit point a client reported and the target it claims to have
    /// hit. Generous on purpose — it exists to reject impossible reports, never to re-litigate a
    /// legitimate shot.
    /// </summary>
    private const float MaxReportedHitDistance = 6f;

    /// <summary>
    /// Received by all clients when a pistol shot lands an intermediate hit on the glass.
    /// Mirrors UpdateGlassClientRpc on MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void PistolUpdateGlassClientRpc(int hitCount)
    {
        BreakableGlassController.Instance?.OnHitByMutant(hitCount);
    }

    /// <summary>
    /// Received by all clients when a pistol shot fully smashes the glass.
    /// Mirrors SmashGlassClientRpc on MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void PistolSmashGlassClientRpc()
    {
        BreakableGlassController.Instance?.ApplySmash();
    }

    /// <summary>Replays a cosmetic <see cref="HittableProp"/> reaction on non-shooting clients.</summary>
    [ClientRpc]
    private void PropHitClientRpc(Vector3 hitPoint, Vector3 direction, ClientRpcParams clientRpcParams = default)
    {
        HittableProp.TryHitAt(hitPoint, direction);
    }

    /// <summary>
    /// Received by all clients except the shooter. Plays VFX on the world pickup Pistol
    /// (the active, networked object) so the effect always originates from the correct instance.
    /// </summary>
    [ClientRpc]
    private void PlayShootFXClientRpc(Vector3 direction, ClientRpcParams clientRpcParams = default)
    {
        // No camera impulse here: impulses are world-space, so replaying it on observers shook
        // their camera for someone else's shot. The shooter generates it locally in OnStartUse.
        PlayShootFX(direction);
    }

    // ── Reloading (KeyCode.R, from PlayerAmmoReserve) ──────────────────────────

    public AmmoType ReserveAmmoType => AmmoType.Pistol;

    public bool NeedsReload => _roundsRemaining.Value < MaxRounds;

    /// <summary>
    /// Called by <see cref="PlayerInventory"/> when the local player presses R with this pistol
    /// equipped. Skips the round-trip when the magazine is full or the local reserve is empty.
    /// </summary>
    public void RequestReloadFromReserve()
    {
        if (_roundsRemaining.Value >= MaxRounds) return;

        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        if (reserve != null && reserve.Get(ReserveAmmoType) <= 0) return;

        ReloadFromReserveServerRpc();
    }

    /// <summary>
    /// Server: validates the sender is holding this pistol, then moves only the rounds needed to
    /// reach <see cref="MaxRounds"/> from their <see cref="PlayerAmmoReserve"/> into the magazine.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadFromReserveServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!PlayerAmmoReserve.TryGetForHolder(clientId, NetworkObject, out PlayerAmmoReserve reserve))
        {
            Debug.LogWarning($"[Pistol] ReloadFromReserveServerRpc: client {clientId} is not holding this pistol.");
            return;
        }

        int needed = MaxRounds - _roundsRemaining.Value;
        if (needed <= 0) return;

        int taken = reserve.Take(ReserveAmmoType, needed);
        if (taken <= 0) return;

        _roundsRemaining.Value += taken;
        PlayReloadSoundClientRpc();
    }

    /// <summary>Plays the reload sound at the pistol on every client after a successful reload.</summary>
    [ClientRpc]
    private void PlayReloadSoundClientRpc()
    {
        if (_reloadSound != null)
            SFXController.Instance.PlayAtPosition(_reloadSound, transform.position);
    }
}
