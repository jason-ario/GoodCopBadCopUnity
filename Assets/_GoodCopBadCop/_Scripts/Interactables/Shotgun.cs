using System;
using System.Collections;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A pump-action shotgun with networked ammo tracking.
///
/// LMB (while held) fires a pellet blast, playing muzzle VFX, camera impulse, recoil, and a
/// "Shoot" animation trigger. Firing is blocked when <see cref="_roundsRemaining"/> reaches
/// zero — a dry-fire click sound plays instead.
///
/// E while holding a <see cref="ShotgunAmmo"/> box refills shells to <see cref="MaxRounds"/>
/// and consumes (despawns) the box.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Shotgun : PickableObject, IAmmoProvider, IInventoryReloadable
{
    /// <summary>Maximum number of shells the shotgun can hold.</summary>
    public const int MaxRounds = 15;

    [SerializeField] private ParticleSystem shootVFX;
    [SerializeField] private CinemachineImpulseSource _cinemachineImpulseSource;
    [SerializeField] private GameObject muzzleFlashLight;
    [SerializeField] private float lightOnTime = .2f;

    [Header("Shotgun — Combat")]
    [Tooltip("Damage dealt to a fellow player per pellet that connects.")]
    [SerializeField] private float _playerPelletDamage = 8f;

    [Tooltip("Damage dealt to a mutant per pellet that connects.")]
    [SerializeField] private float _mutantPelletDamage = 10f;

    [Tooltip("Number of pellets fired per shot, spread across the cone.")]
    [SerializeField] [Min(1)] private int _pelletCount = 8;

    [Tooltip("Half-angle (in degrees) of the pellet spread cone.")]
    [SerializeField] [Range(0f, 45f)] private float _spreadAngle = 10f;

    [Tooltip("Maximum hitscan range in metres — kept short since this is a close-range weapon.")]
    [SerializeField] private float _bulletRange = 8f;

    [Header("Shotgun — Audio")]
    [Tooltip("Dry-fire click played locally when the shotgun is empty.")]
    [SerializeField] private AudioClip _emptySound;

    [Tooltip("Sound played on every client when the shotgun is reloaded.")]
    [SerializeField] private AudioClip _reloadSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _roundsRemaining = new(
        MaxRounds,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current number of shells loaded in the shotgun.</summary>
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
        => interactText = $"Shotgun ({rounds}/{MaxRounds})";

    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_roundsRemaining.Value <= 0)
        {
            if (_emptySound != null)
                SFXController.Instance.PlayAtPosition(_emptySound, transform.position);
            return;
        }

        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        playerPickupController.PlayerAnimationController.SetAnimTrigger("Shoot");
        _cinemachineImpulseSource.GenerateImpulse();
        StartCoroutine(LightOnOff());
        var movement = playerPickupController.GetComponent<PlayerMovementController>();
        if (movement != null)
        {
            movement.ApplyRecoil();
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            // Client-authoritative hitscan: the pellet cone is traced here, against the world as
            // this player sees it, and the resolved per-target pellet counts are reported to the
            // server. The server used to re-trace the cone from the reported origin — a different
            // random spread against already-moved targets — so a blast that clearly connected
            // locally could deal little or nothing. See FireServerRpc.
            ResolveBlast(cam.transform.position, cam.transform.forward,
                out NetworkObjectReference[] mutantRefs, out int[] mutantPellets,
                out NetworkObjectReference[] playerRefs, out int[] playerPellets,
                out bool hitGlass);

            FireServerRpc(cam.transform.forward, mutantRefs, mutantPellets, playerRefs, playerPellets, hitGlass);
        }
    }

    /// <summary>
    /// Traces the pellet cone locally on the shooter's machine and accumulates how many pellets
    /// landed on each mutant and each fellow player, plus whether the booth glass was struck.
    /// Runs on the shooter only — the spread the player sees is the spread that is reported.
    /// </summary>
    private void ResolveBlast(Vector3 rayOrigin, Vector3 rayDirection,
        out NetworkObjectReference[] mutantRefs, out int[] mutantPellets,
        out NetworkObjectReference[] playerRefs, out int[] playerPellets,
        out bool hitGlass)
    {
        Dictionary<NetworkObject, int> mutantHits = new();
        Dictionary<NetworkObject, int> playerHits = new();
        hitGlass = false;

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        for (int i = 0; i < _pelletCount; i++)
        {
            Vector3 pelletDirection = RandomConeDirection(rayDirection, _spreadAngle);

            if (!Physics.Raycast(rayOrigin, pelletDirection, out RaycastHit hit, _bulletRange))
                continue;

            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null && enemy.NetworkObject != null)
            {
                mutantHits.TryGetValue(enemy.NetworkObject, out int mCount);
                mutantHits[enemy.NetworkObject] = mCount + 1;
                continue;
            }

            Transform root = hit.collider.transform.root;
            if (root.CompareTag("Player"))
            {
                NetworkObject playerNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (playerNetObj == null || playerNetObj.OwnerClientId == localClientId)
                    continue;

                if (hit.collider.GetComponentInParent<PlayerHealth>() != null)
                {
                    playerHits.TryGetValue(playerNetObj, out int pCount);
                    playerHits[playerNetObj] = pCount + 1;
                }
                continue;
            }

            BreakableGlassController glassHit = hit.collider.GetComponentInParent<BreakableGlassController>();
            if (glassHit != null && !glassHit.IsSmashed)
                hitGlass = true;
        }

        ToArrays(mutantHits, out mutantRefs, out mutantPellets);
        ToArrays(playerHits, out playerRefs, out playerPellets);
    }

    private static void ToArrays(Dictionary<NetworkObject, int> hits,
        out NetworkObjectReference[] refs, out int[] counts)
    {
        refs   = new NetworkObjectReference[hits.Count];
        counts = new int[hits.Count];

        int index = 0;
        foreach (var kvp in hits)
        {
            refs[index]   = new NetworkObjectReference(kvp.Key);
            counts[index] = kvp.Value;
            index++;
        }
    }

    public void ShootFX()
    {
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        _cinemachineImpulseSource.GenerateImpulse();
        StartCoroutine(LightOnOff());
    }

    IEnumerator LightOnOff()
    {
        muzzleFlashLight.SetActive(true);
        yield return new WaitForSeconds(lightOnTime);
        muzzleFlashLight.SetActive(false);
    }

    public override void OnBodyStartUse()
    {
        //playerPickupController.GetComponent<RagdollController>().ActivateRagdollWithForce(-playerPickupController.transform.forward * 100);
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        StartCoroutine(LightOnOff());

    }

    // ── Combat ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side: validates the shooter, spends a round, then applies the per-target pellet counts
    /// the CLIENT resolved in <see cref="ResolveBlast"/>. Damage from multiple pellets hitting the
    /// same mutant/player is accumulated into one call; the glass registers at most one hit per
    /// blast, mirroring <see cref="MutantSuspectBehaviour"/>'s hit pattern.
    /// The server does not re-trace the cone — it trusts what the shooter's own machine hit, so a
    /// blast that visibly connected always lands. Damage/health/death stay server-owned, and the
    /// pellet counts are clamped so a bad report can't inflate damage.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayDirection,
        NetworkObjectReference[] mutantRefs, int[] mutantPellets,
        NetworkObjectReference[] playerRefs, int[] playerPellets,
        bool hitGlass, ServerRpcParams rpcParams = default)
    {
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterClientId, out var client))
            return;

        // Only the client actually holding this shotgun may fire.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null) return;
        if (!ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj) || heldNetObj != NetworkObject) return;

        if (_roundsRemaining.Value <= 0) return;

        _roundsRemaining.Value--;

        if (mutantRefs != null && mutantPellets != null)
        {
            int count = Mathf.Min(mutantRefs.Length, mutantPellets.Length);
            for (int i = 0; i < count; i++)
            {
                if (!mutantRefs[i].TryGet(out NetworkObject targetObj) || targetObj == null) continue;

                MutantEnemy enemy = targetObj.GetComponent<MutantEnemy>() ?? targetObj.GetComponentInChildren<MutantEnemy>();
                if (enemy == null) continue;

                int pellets = Mathf.Clamp(mutantPellets[i], 0, _pelletCount);
                if (pellets <= 0) continue;

                enemy.TakeDamage(_mutantPelletDamage * pellets, enemy.transform.position);
            }
        }

        if (playerRefs != null && playerPellets != null)
        {
            int count = Mathf.Min(playerRefs.Length, playerPellets.Length);
            for (int i = 0; i < count; i++)
            {
                if (!playerRefs[i].TryGet(out NetworkObject targetObj) || targetObj == null) continue;

                // Never let a blast hurt the shooter, whatever the client claims.
                if (targetObj.OwnerClientId == shooterClientId) continue;

                PlayerHealth playerHealth = targetObj.GetComponent<PlayerHealth>() ?? targetObj.GetComponentInChildren<PlayerHealth>();
                if (playerHealth == null) continue;

                int pellets = Mathf.Clamp(playerPellets[i], 0, _pelletCount);
                if (pellets <= 0) continue;

                playerHealth.TakeDamage(_playerPelletDamage * pellets, EffectKeys.FriendlyGunshotDamage);
            }
        }

        if (hitGlass)
        {
            BreakableGlassController glass = BreakableGlassController.Instance;
            if (glass != null && !glass.IsSmashed)
            {
                int newHits = glass.RegisterHit();
                if (glass.IsSmashed)
                    ShotgunSmashGlassClientRpc();
                else
                    ShotgunUpdateGlassClientRpc(newHits);
            }
        }
    }

    /// <summary>
    /// Returns a random direction within a cone of half-angle <paramref name="maxAngleDegrees"/>
    /// around <paramref name="forward"/>, used to spread the shotgun's pellets.
    /// </summary>
    private static Vector3 RandomConeDirection(Vector3 forward, float maxAngleDegrees)
    {
        float angle = UnityEngine.Random.Range(0f, maxAngleDegrees) * Mathf.Deg2Rad;
        float rotation = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;

        float x = Mathf.Sin(angle) * Mathf.Cos(rotation);
        float y = Mathf.Sin(angle) * Mathf.Sin(rotation);
        float z = Mathf.Cos(angle);

        Quaternion lookRot = Quaternion.LookRotation(forward);
        return lookRot * new Vector3(x, y, z);
    }

    /// <summary>
    /// Received by all clients when a shotgun blast lands an intermediate hit on the glass.
    /// Mirrors UpdateGlassClientRpc on Pistol/MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void ShotgunUpdateGlassClientRpc(int hitCount)
    {
        BreakableGlassController.Instance?.OnHitByMutant(hitCount);
    }

    /// <summary>
    /// Received by all clients when a shotgun blast fully smashes the glass.
    /// Mirrors SmashGlassClientRpc on Pistol/MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void ShotgunSmashGlassClientRpc()
    {
        BreakableGlassController.Instance?.ApplySmash();
    }

    // ── Reloading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting the shotgun.
    /// Delegates to <see cref="TryReload"/> so both E and LMB share the same path.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
        => TryReload(player);

    /// <summary>
    /// Called when the player LMB-clicks the shotgun while holding a compatible item
    /// (i.e. <see cref="ShotgunAmmo"/> is listed in <c>itemsThatCanInteractWith</c>).
    /// Delegates to <see cref="TryReload"/> so both E and LMB share the same path.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        TryReload(playerInteractionController);
    }

    /// <summary>
    /// Validates that the player is holding a <see cref="ShotgunAmmo"/> box and the shotgun
    /// has room for more shells, then sends <see cref="ReloadServerRpc"/>.
    /// </summary>
    private void TryReload(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject is not ShotgunAmmo) return;
        if (_roundsRemaining.Value >= MaxRounds) return;

        ReloadServerRpc();
    }

    /// <summary>
    /// Validates server-side that the requesting player is holding a <see cref="ShotgunAmmo"/>
    /// box and the shotgun is not already full. On success, transfers only the shells needed
    /// to reach <see cref="MaxRounds"/> from the box. If the box reaches zero it is despawned
    /// via <see cref="ConsumeAmmoClientRpc"/>; otherwise it stays equipped with the updated count.
    /// RequireOwnership = false so any client can reload the shotgun regardless of who holds it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[Shotgun] ReloadServerRpc: client {clientId} not found.");
            return;
        }

        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null || !ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj)
            || heldNetObj.GetComponent<ShotgunAmmo>() is not ShotgunAmmo ammo)
        {
            Debug.LogWarning($"[Shotgun] ReloadServerRpc: client {clientId} is not holding ShotgunAmmo.");
            return;
        }

        if (_roundsRemaining.Value >= MaxRounds) return;

        int needed = MaxRounds - _roundsRemaining.Value;
        int transferred = ammo.ConsumeRounds(needed);
        _roundsRemaining.Value += transferred;

        // Only despawn the box when it has been fully emptied.
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
    /// Calls <see cref="PlayerPickupController.DestroyEquippedItem"/> which unequips the box
    /// from all arm containers, releases the holder, and despawns the NetworkObject.
    /// </summary>
    [ClientRpc]
    private void ConsumeAmmoClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        if (ppc == null)
        {
            Debug.LogWarning("[Shotgun] ConsumeAmmoClientRpc: could not find local PlayerPickupController.");
            return;
        }

        ppc.DestroyEquippedItem();
    }

    // ── Reloading from inventory (KeyCode.R) ────────────────────────────────────

    /// <summary>Returns true if <paramref name="candidate"/> is a <see cref="ShotgunAmmo"/> box.</summary>
    public bool IsCompatibleAmmo(PickableObject candidate) => candidate is ShotgunAmmo;

    /// <summary>
    /// Called by <see cref="PlayerInventory"/> when the local player presses R while this shotgun
    /// is equipped and a <see cref="ShotgunAmmo"/> box sits in the other inventory slot (not held).
    /// </summary>
    public void ReloadFromInventory(PickableObject ammoItem)
    {
        if (ammoItem is not ShotgunAmmo ammo) return;
        if (_roundsRemaining.Value >= MaxRounds) return;
        if (!ammo.TryGetComponent(out NetworkObject ammoNetObj)) return;

        ReloadFromInventoryServerRpc(new NetworkObjectReference(ammoNetObj));
    }

    /// <summary>
    /// Validates server-side that the requesting client owns both this shotgun (currently holds it)
    /// and the referenced <see cref="ShotgunAmmo"/> box (it sits somewhere in their inventory),
    /// then transfers shells exactly like <see cref="ReloadServerRpc"/> — except the box is never
    /// brought to hand. If the box empties, <see cref="ConsumeInventoryAmmoClientRpc"/> tells the
    /// owning client to clear its inventory slot and despawn the box.
    /// RequireOwnership = false so any client can request this for their own shotgun/ammo.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadFromInventoryServerRpc(NetworkObjectReference ammoRef, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[Shotgun] ReloadFromInventoryServerRpc: client {clientId} not found.");
            return;
        }

        // Only the client actually holding this shotgun may reload it.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null || !ppc.HeldObjectRef.TryGet(out NetworkObject heldWeaponObj) || heldWeaponObj != NetworkObject)
        {
            Debug.LogWarning($"[Shotgun] ReloadFromInventoryServerRpc: client {clientId} is not holding this shotgun.");
            return;
        }

        if (!ammoRef.TryGet(out NetworkObject ammoNetObj) || ammoNetObj.GetComponent<ShotgunAmmo>() is not ShotgunAmmo ammo)
        {
            Debug.LogWarning($"[Shotgun] ReloadFromInventoryServerRpc: client {clientId}'s ammo reference is invalid.");
            return;
        }

        // The box must actually belong to the requesting client (i.e. sit in their inventory).
        if (ammoNetObj.OwnerClientId != clientId) return;
        if (_roundsRemaining.Value >= MaxRounds) return;

        int needed = MaxRounds - _roundsRemaining.Value;
        int transferred = ammo.ConsumeRounds(needed);
        _roundsRemaining.Value += transferred;

        // Only despawn the box when it has been fully emptied.
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
    /// box from their <see cref="PlayerInventory"/> slot (so the UI clears immediately), then
    /// asks the server to despawn it — mirroring <see cref="ConsumeAmmoClientRpc"/>'s ordering
    /// for the held-box path.
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
