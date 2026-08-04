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

        FireServerRpc(cam.transform.position, cam.transform.forward);
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
    /// decrements <see cref="_roundsRemaining"/>, performs a hitscan against enemies,
    /// then relays shoot VFX to all other clients.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayOrigin, Vector3 rayDirection, ServerRpcParams rpcParams = default)
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

        // Hitscan — raycast from the camera position in the aim direction.
        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, _bulletRange))
        {
            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(_damage, hit.point, knockbackDirection: rayDirection);
            }
            else if (TryDamagePlayer(hit.collider, clientId, _damage))
            {
                // Friendly-fire hit landed — handled inside TryDamagePlayer.
            }
            else
            {
                // Check for breakable glass — register the hit server-side and broadcast visuals
                // to all clients via ClientRpc, mirroring MutantSuspectBehaviour's glass pattern.
                BreakableGlassController glass = hit.collider.GetComponentInParent<BreakableGlassController>();
                if (glass != null && !glass.IsSmashed)
                {
                    int newHits = glass.RegisterHit();
                    Debug.Log($"[Pistol] Shot breakable glass at {hit.point}. Hits={newHits}");
                    if (glass.IsSmashed)
                        PistolSmashGlassClientRpc();
                    else
                        PistolUpdateGlassClientRpc(newHits);
                }
            }
        }

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
    /// Checks whether <paramref name="hitCollider"/> belongs to a fellow player and, if so,
    /// applies friendly-fire damage. Skips the shooter's own player (via <see cref="NetworkObject.OwnerClientId"/>)
    /// so hitting yourself never registers damage.
    /// </summary>
    /// <returns><see langword="true"/> if a fellow player was found and damaged.</returns>
    private static bool TryDamagePlayer(Collider hitCollider, ulong shooterClientId, float damage)
    {
        Transform root = hitCollider.transform.root;
        if (!root.CompareTag("Player"))
            return false;

        NetworkObject playerNetObj = hitCollider.GetComponentInParent<NetworkObject>();
        if (playerNetObj != null && playerNetObj.OwnerClientId == shooterClientId)
            return false;

        PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            return false;

        playerHealth.TakeDamage(damage, EffectKeys.FriendlyGunshotDamage);
        return true;
    }

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
