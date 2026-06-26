using System.Collections;
using System.Collections.Generic;
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
public class Pistol : PickableObject
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

        PlayShootFX();
        playerPickupController.PlayerAnimationController.SetAnimTrigger("Shoot");
        _cinemachineImpulseSource?.GenerateImpulse();

        PlayerMovementController movement = playerPickupController.GetComponent<PlayerMovementController>();
        movement?.ApplyRecoil();

        FireServerRpc();
    }

    /// <summary>
    /// The body slot Pistol (inactive, pre-placed in the body arm container) receives this
    /// call from PlayerPickupController. VFX for other clients is handled by
    /// <see cref="PlayShootFXClientRpc"/> inside <see cref="FireServerRpc"/>, so this is
    /// intentionally a no-op.
    /// </summary>
    public override void OnBodyStartUse() { }

    private void PlayShootFX()
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
    /// decrements <see cref="_roundsRemaining"/>, then relays shoot VFX to all clients
    /// except the shooter (who already played it locally in <see cref="OnStartUse"/>).
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return;

        // Only the client actually holding this pistol may decrement the counter.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc?.HeldObject != this) return;

        if (_roundsRemaining.Value <= 0) return;

        _roundsRemaining.Value--;

        // Relay VFX to every other connected client; shooter already played it locally.
        List<ulong> others = new List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != clientId)
                others.Add(id);
        }

        if (others.Count > 0)
        {
            PlayShootFXClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = others }
            });
        }
    }

    /// <summary>
    /// Received by all clients except the shooter. Plays VFX on the world pickup Pistol
    /// (the active, networked object) so the effect always originates from the correct instance.
    /// </summary>
    [ClientRpc]
    private void PlayShootFXClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayShootFX();
        _cinemachineImpulseSource?.GenerateImpulse();
    }

    // ── Reloading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting the pistol.
    /// If the player is holding a <see cref="PistolAmmo"/> clip and the pistol is not full,
    /// sends a reload request to the server which refills rounds and consumes the clip.
    /// LMB pickup is inherited unchanged from <see cref="PickableObject.Interact"/>.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject is not PistolAmmo) return;
        if (_roundsRemaining.Value >= MaxRounds) return;

        ReloadServerRpc();
    }

    /// <summary>
    /// Validates server-side that the requesting player is holding a <see cref="PistolAmmo"/>
    /// clip and the pistol is not already full. On success, refills the round counter and
    /// sends a targeted <see cref="ConsumeAmmoClientRpc"/> to that player.
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
        if (ppc == null || ppc.HeldObject is not PistolAmmo)
        {
            Debug.LogWarning($"[Pistol] ReloadServerRpc: client {clientId} is not holding PistolAmmo.");
            return;
        }

        if (_roundsRemaining.Value >= MaxRounds) return;

        _roundsRemaining.Value = MaxRounds;

        ConsumeAmmoClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
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
}
