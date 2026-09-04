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
/// E while holding a <see cref="PistolAmmo"/> clip refills rounds to <see cref="MaxRounds"/>
/// and consumes (despawns) the clip.
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

        if (current > previous && _reloadSound != null)
            SFXController.Instance.PlayAtPosition(_reloadSound, transform.position);
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
        FireServerRpc(cam.transform.forward, (byte)hit.Kind, hit.TargetRef, hit.Point);
    }

    /// <summary>What a locally-resolved shot connected with. Sent to the server as a byte.</summary>
    private enum ShotKind : byte
    {
        None    = 0,
        Mutant  = 1,
        Player  = 2,
        Glass   = 3,
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
        if (!Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, _bulletRange))
            return new FireHit(ShotKind.None, default, rayOrigin);

        MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
        if (enemy != null && enemy.NetworkObject != null)
            return new FireHit(ShotKind.Mutant, new NetworkObjectReference(enemy.NetworkObject), hit.point);

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
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
            return new FireHit(ShotKind.Glass, default, hit.point);

        return new FireHit(ShotKind.None, default, hit.point);
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

        if (!targetRef.TryGet(out NetworkObject targetObj) || targetObj == null)
            return; // Target despawned between the shot and this message.

        // Sanity bound, NOT a hit test — rejects a report that could only come from a bug.
        if (Vector3.Distance(targetObj.transform.position, hitPoint) > MaxReportedHitDistance)
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

        if (kind == ShotKind.Player)
        {
            // Never let a shot hurt the shooter, whatever the client claims.
            if (targetObj.OwnerClientId == shooterClientId) return;

            PlayerHealth playerHealth = targetObj.GetComponent<PlayerHealth>() ?? targetObj.GetComponentInChildren<PlayerHealth>();
            playerHealth?.TakeDamage(_damage, EffectKeys.FriendlyGunshotDamage);
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

    /// <summary>
    /// Received by all clients except the shooter. Plays VFX on the world pickup Pistol
    /// (the active, networked object) so the effect always originates from the correct instance.
    /// </summary>
    [ClientRpc]
    private void PlayShootFXClientRpc(Vector3 direction, ClientRpcParams clientRpcParams = default)
    {
        PlayShootFX(direction);
        _cinemachineImpulseSource?.GenerateImpulse();
    }

    // ── Reloading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting the pistol.
    /// Delegates to <see cref="TryReload"/> so both E and LMB share the same path.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
        => TryReload(player);

    /// <summary>
    /// Called when the player LMB-clicks the pistol while holding a compatible item
    /// (i.e. <see cref="PistolAmmo"/> is listed in <c>itemsThatCanInteractWith</c>).
    /// Delegates to <see cref="TryReload"/> so both E and LMB share the same path.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        TryReload(playerInteractionController);
    }

    /// <summary>
    /// Validates that the player is holding a <see cref="PistolAmmo"/> clip and the pistol
    /// has room for more rounds, then sends <see cref="ReloadServerRpc"/>.
    /// </summary>
    private void TryReload(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject is not PistolAmmo) return;
        if (_roundsRemaining.Value >= MaxRounds) return;

        ReloadServerRpc();
    }

    /// <summary>
    /// Validates server-side that the requesting player is holding a <see cref="PistolAmmo"/>
    /// clip and the pistol is not already full. On success, transfers only the rounds needed
    /// to reach <see cref="MaxRounds"/> from the clip. If the clip reaches zero it is despawned
    /// via <see cref="ConsumeAmmoClientRpc"/>; otherwise it stays equipped with the updated count.
    /// RequireOwnership = false so any client can reload the pistol regardless of who holds it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[Pistol] ReloadServerRpc: client {clientId} not found.");
            return;
        }

        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null || !ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj)
            || heldNetObj.GetComponent<PistolAmmo>() is not PistolAmmo ammo)
        {
            Debug.LogWarning($"[Pistol] ReloadServerRpc: client {clientId} is not holding PistolAmmo.");
            return;
        }

        if (_roundsRemaining.Value >= MaxRounds) return;

        int needed = MaxRounds - _roundsRemaining.Value;
        int transferred = ammo.ConsumeRounds(needed);
        _roundsRemaining.Value += transferred;

        // Only despawn the clip when it has been fully emptied.
        if (ammo.RoundsInClip <= 0)
        {
            ConsumeAmmoClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }
    }

    /// <summary>
    /// Received only by the player who triggered the reload.
    /// Calls <see cref="PlayerPickupController.DestroyEquippedItem"/> which unequips the clip
    /// from all arm containers, releases the holder, and despawns the NetworkObject.
    /// </summary>
    [ClientRpc]
    private void ConsumeAmmoClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        if (ppc == null)
        {
            Debug.LogWarning("[Pistol] ConsumeAmmoClientRpc: could not find local PlayerPickupController.");
            return;
        }

        ppc.DestroyEquippedItem();
    }

    // ── Reloading from inventory (KeyCode.R) ────────────────────────────────────

    /// <summary>Returns true if <paramref name="candidate"/> is a <see cref="PistolAmmo"/> clip.</summary>
    public bool IsCompatibleAmmo(PickableObject candidate) => candidate is PistolAmmo;

    /// <summary>
    /// Called by <see cref="PlayerInventory"/> when the local player presses R while this pistol
    /// is equipped and a <see cref="PistolAmmo"/> clip sits in the other inventory slot (not held).
    /// </summary>
    public void ReloadFromInventory(PickableObject ammoItem)
    {
        if (ammoItem is not PistolAmmo ammo) return;
        if (_roundsRemaining.Value >= MaxRounds) return;
        if (!ammo.TryGetComponent(out NetworkObject ammoNetObj)) return;

        ReloadFromInventoryServerRpc(new NetworkObjectReference(ammoNetObj));
    }

    /// <summary>
    /// Validates server-side that the requesting client owns both this pistol (currently holds it)
    /// and the referenced <see cref="PistolAmmo"/> clip (it sits somewhere in their inventory),
    /// then transfers rounds exactly like <see cref="ReloadServerRpc"/> — except the clip is never
    /// brought to hand. If the clip empties, <see cref="ConsumeInventoryAmmoClientRpc"/> tells the
    /// owning client to clear its inventory slot and despawn the clip.
    /// RequireOwnership = false so any client can request this for their own pistol/ammo.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadFromInventoryServerRpc(NetworkObjectReference ammoRef, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[Pistol] ReloadFromInventoryServerRpc: client {clientId} not found.");
            return;
        }

        // Only the client actually holding this pistol may reload it.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null || !ppc.HeldObjectRef.TryGet(out NetworkObject heldWeaponObj) || heldWeaponObj != NetworkObject)
        {
            Debug.LogWarning($"[Pistol] ReloadFromInventoryServerRpc: client {clientId} is not holding this pistol.");
            return;
        }

        if (!ammoRef.TryGet(out NetworkObject ammoNetObj) || ammoNetObj.GetComponent<PistolAmmo>() is not PistolAmmo ammo)
        {
            Debug.LogWarning($"[Pistol] ReloadFromInventoryServerRpc: client {clientId}'s ammo reference is invalid.");
            return;
        }

        // The clip must actually belong to the requesting client (i.e. sit in their inventory).
        if (ammoNetObj.OwnerClientId != clientId) return;
        if (_roundsRemaining.Value >= MaxRounds) return;

        int needed = MaxRounds - _roundsRemaining.Value;
        int transferred = ammo.ConsumeRounds(needed);
        _roundsRemaining.Value += transferred;

        // Only despawn the clip when it has been fully emptied.
        if (ammo.RoundsInClip <= 0)
        {
            ConsumeInventoryAmmoClientRpc(ammoRef, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }
    }

    /// <summary>
    /// Received only by the player who triggered the inventory reload. Removes the now-empty
    /// clip from their <see cref="PlayerInventory"/> slot (so the UI clears immediately), then
    /// asks the server to despawn it — mirroring <see cref="ConsumeAmmoClientRpc"/>'s ordering
    /// for the held-clip path.
    /// </summary>
    [ClientRpc]
    private void ConsumeInventoryAmmoClientRpc(NetworkObjectReference ammoRef, ClientRpcParams clientRpcParams = default)
    {
        if (!ammoRef.TryGet(out NetworkObject ammoNetObj)) return;

        PickableObject ammoItem = ammoNetObj.GetComponent<PickableObject>();
        if (ammoItem == null) return;

        PlayerInventory inventory = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerInventory>();
        inventory?.ClearSlotForItem(ammoItem);

        ammoItem.DespawnServerRpc();
    }
}
