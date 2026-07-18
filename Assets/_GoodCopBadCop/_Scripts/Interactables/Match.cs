using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single-use lit match spawned by <see cref="MatchBox"/> when the player draws one.
///
/// Lifecycle:
///   1. Spawned into the player's hand by <see cref="MatchBox.Interact"/>.
///   2. On pickup, the "LightMatch" animator trigger fires (owner broadcasts via PAC).
///   3. After <see cref="_lightAnimDuration"/> seconds the match is considered lit on
///      all clients (<see cref="IsLit"/> becomes true, flame VFX starts).
///   4. The player can use the lit match on a <see cref="FirePit"/> to ignite it;
///      the fire pit validates <see cref="IsLit"/> before accepting.
///   5. After <see cref="_burnDuration"/> seconds of burning the match extinguishes and
///      is automatically consumed from the player's hand.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect           (required by Interactable)
///   - ParentConstraint          (required by PickableObject)
///   - Rigidbody + NetworkRigidbody + PickableColliderController (required by PickableObject)
///   - Collider on Default layer
///   - "Item Data" field         → MatchStick.asset (PickableItemData)
///   - "_flameVFX"               → child ParticleSystem for the tip flame
///   - "_crackleAudioSource"     → (optional) looping AudioSource for burn crackle
/// Must be registered as a Network Prefab in the NetworkManager.
/// The <see cref="FirePit"/>'s "itemsThatCanInteractWith" must include MatchStick.asset.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MatchStick : PickableObject
{
    [Header("Match Stick — VFX")]
    [Tooltip("Child ParticleSystem that plays while the match is lit.")]
    [SerializeField] private ParticleSystem _flameVFX;

    [Header("Match Stick — Audio")]
    [Tooltip("Optional looping AudioSource for the burn crackle.")]
    [SerializeField] private AudioSource _crackleAudioSource;

    [Header("Match Stick — Timing")]
    [Tooltip("Seconds after pickup before the match is considered lit (covers the strike animation).")]
    [SerializeField] private float _lightAnimDuration = 1.5f;

    [Tooltip("Seconds the lit match burns before automatically extinguishing in the player's hand.")]
    [SerializeField] private float _burnDuration = 10f;

    // ── Networked state ────────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _isLit = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// True once the strike animation has completed and the match is actively burning.
    /// Checked by <see cref="FirePit"/> server-side before allowing ignition.
    /// </summary>
    public bool IsLit => _isLit.Value;

    private Coroutine _lightingCoroutine;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = "Match";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isLit.OnValueChanged += OnIsLitChanged;

        // Sync visual state for late-joining clients.
        if (_isLit.Value)
            StartFlameVisuals();
        else
            StopFlameVisuals();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isLit.OnValueChanged -= OnIsLitChanged;

        // Stop the server coroutine if the match is despawned mid-burn
        // (e.g. consumed at a fire pit before the timer expires).
        if (_lightingCoroutine != null)
        {
            StopCoroutine(_lightingCoroutine);
            _lightingCoroutine = null;
        }
    }

    // ── Pickup lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// Called on the owner client when the match enters the player's hand.
    /// Triggers the strike animation via the networked animator and begins the
    /// server-authoritative lighting sequence.
    /// </summary>
    public override void OnPickedUp()
    {
        base.OnPickedUp();

        // playerPickupController is set in OnEquipped, which ObjectContainer calls
        // before PickUpObject calls OnPickedUp, so it is guaranteed to be non-null here.
        // SetAnimTrigger is owner-only and broadcasts the trigger to all clients via ServerRpc.
        playerPickupController?.PlayerAnimationController.SetAnimTrigger("LightMatch");

        // Begin the server-authoritative lighting sequence.
        // RequireOwnership = false because ownership transfer (RequestOwnershipServerRpc)
        // may still be in flight when this fires.
        BeginLightingServerRpc();
    }

    // ── Server RPCs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Server: waits for the strike animation to finish, marks the match as lit, then
    /// starts the burn timer. Restarting while already running resets both timers.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void BeginLightingServerRpc(ServerRpcParams rpcParams = default)
    {
        if (_lightingCoroutine != null)
            StopCoroutine(_lightingCoroutine);
        _lightingCoroutine = StartCoroutine(LightingCoroutine());
    }

    private IEnumerator LightingCoroutine()
    {
        // Wait for the strike animation to finish before the match is treated as lit.
        yield return new WaitForSeconds(_lightAnimDuration);

        _isLit.Value = true;

        // Burn for the configured duration.
        yield return new WaitForSeconds(_burnDuration);

        _isLit.Value = false;
        _lightingCoroutine = null;

        // Tell the holding client to consume the match so player state is cleaned up
        // properly (arm mask, item index, etc.). The guard inside the ClientRpc ensures
        // this is a no-op if the player has already dropped or used the match.
        BurnOutClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });
    }

    // ── Client RPCs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Received only by the match owner. If the player is still holding this match it
    /// is destroyed via <see cref="PlayerPickupController.DestroyEquippedItem"/> which
    /// correctly clears all arm-mask, item-index, and network state before despawning.
    /// </summary>
    [ClientRpc]
    private void BurnOutClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        if (ppc == null) return;

        // Guard: the player may have already dropped or used the match.
        if (ppc.HeldObject != this) return;

        ppc.DestroyEquippedItem();
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnIsLitChanged(bool previous, bool current)
    {
        if (current)
            StartFlameVisuals();
        else
            StopFlameVisuals();
    }

    // ── Visual helpers ─────────────────────────────────────────────────────────

    private void StartFlameVisuals()
    {
        if (_flameVFX != null && !_flameVFX.isPlaying)
            _flameVFX.Play();

        if (_crackleAudioSource != null && !_crackleAudioSource.isPlaying)
            _crackleAudioSource.Play();
    }

    private void StopFlameVisuals()
    {
        if (_flameVFX != null)
            _flameVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (_crackleAudioSource != null)
            _crackleAudioSource.Stop();
    }
}
