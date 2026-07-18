using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A box of matches the player picks up and carries.
///
/// Flow:
///   1. Player picks up the matchbox (standard PickableObject interaction).
///   2. Player presses LMB with nothing targetable in range → <see cref="OnStartUse"/>
///      fires the "LightMatch" animation trigger and starts the server lighting sequence.
///   3. After <see cref="_lightAnimDuration"/> seconds <see cref="IsLit"/> becomes true
///      on all clients — the match tip VFX activates.
///   4. While lit the player can LMB on a <see cref="FirePit"/>. The fire pit validates
///      <see cref="IsLit"/> via <see cref="IIgnitionSource"/>, ignites, and calls
///      <see cref="OnUsedToIgnite"/> which decrements the count and extinguishes the match.
///   5. If the 10-second burn timer expires before use, the match extinguishes automatically.
///
/// Prefab requirements:
///   - NetworkObject, NetworkTransform, NetworkRigidbody
///   - HighlightEffect           (required by Interactable)
///   - ParentConstraint          (required by PickableObject)
///   - Rigidbody + PickableColliderController (required by PickableObject)
///   - Collider on Default layer
///   - "Item Data" field         → MatchBox.asset (PickableItemData)
///   - "_flameVFX"               → child ParticleSystem for the lit-match tip flame
///   - "_crackleAudio"           → (optional) looping AudioSource for burn crackle
/// Must be registered as a Network Prefab in the NetworkManager.
/// The <see cref="FirePit"/>'s "itemsThatCanInteractWith" must include MatchBox.asset.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MatchBox : PickableObject, IIgnitionSource
{
    /// <summary>Maximum matches a full box holds.</summary>
    public const int MaxMatches = 10;

    [Header("Match Box — VFX / Audio")]
    [Tooltip("Child ParticleSystem that plays on the match tip while the match is lit.")]
    [SerializeField] private ParticleSystem _flameVFX;

    [Tooltip("Optional looping AudioSource played while the match burns.")]
    [SerializeField] private AudioSource _crackleAudio;

    [Header("Match Box — Timing")]
    [Tooltip("Seconds after LMB press before the match is considered lit (covers strike animation).")]
    [SerializeField] private float _lightAnimDuration = 1.5f;

    [Tooltip("Seconds the lit match burns before automatically extinguishing.")]
    [SerializeField] private float _burnDuration = 10f;

    // ── Networked state ────────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _matchCount = new(
        MaxMatches,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// True while the lighting animation is playing. Prevents re-triggering during the
    /// strike window.
    /// </summary>
    private readonly NetworkVariable<bool> _isLighting = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// True once the strike animation has completed and the match is actively burning.
    /// Read by <see cref="FirePit"/> server-side to validate ignition attempts.
    /// </summary>
    private readonly NetworkVariable<bool> _isLitVar = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current match count.</summary>
    public int MatchCount => _matchCount.Value;

    // IIgnitionSource
    /// <inheritdoc/>
    public bool IsLit => _isLitVar.Value;

    private Coroutine _lightingCoroutine;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        UpdateInteractText();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _matchCount.OnValueChanged += OnMatchCountChanged;
        _isLitVar.OnValueChanged   += OnIsLitChanged;

        UpdateInteractText();
        if (_isLitVar.Value) StartFlameVisuals();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _matchCount.OnValueChanged -= OnMatchCountChanged;
        _isLitVar.OnValueChanged   -= OnIsLitChanged;

        if (_lightingCoroutine != null)
        {
            StopCoroutine(_lightingCoroutine);
            _lightingCoroutine = null;
        }
    }

    // ── IIgnitionSource ────────────────────────────────────────────────────────

    /// <summary>
    /// Called server-side by <see cref="FirePit"/> immediately after a successful
    /// ignition. Decrements the match count and extinguishes the burning match.
    /// </summary>
    public void OnUsedToIgnite()
    {
        if (!IsServer) return;

        if (_lightingCoroutine != null)
        {
            StopCoroutine(_lightingCoroutine);
            _lightingCoroutine = null;
        }

        _isLitVar.Value   = false;
        _isLighting.Value = false;

        if (_matchCount.Value > 0)
            _matchCount.Value--;
    }

    // ── PickableObject overrides ───────────────────────────────────────────────

    /// <summary>
    /// Called on the owner when the player presses LMB. If a compatible interactable
    /// (fire pit) was targeted this same frame, <c>InteractWithItem</c> has already run
    /// and set <c>_isLitVar</c> false on the server — but the client hasn't received the
    /// update yet, so <c>IsLit</c> is still true here, which correctly suppresses
    /// starting a new lighting cycle after a successful ignition.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        if (!IsOwner) return;

        // Suppress if match is already burning, animation in progress, or box is empty.
        if (IsLit || _isLighting.Value || _matchCount.Value <= 0) return;

        // Broadcast strike animation to all clients via the networked trigger.
        playerPickupController?.PlayerAnimationController.SetAnimTrigger("LightMatch");

        LightMatchServerRpc();
    }

    public override void OnStopUse()
    {
        base.OnStopUse();
        // Burn continues after LMB release — nothing to do.
    }

    // Body-side events are no-ops. Flame VFX is driven by the _isLitVar NetworkVariable
    // which propagates to all clients and controls the world object's VFX directly.
    public override void OnBodyStartUse() { }
    public override void OnBodyStopUse()  { }

    // ── Server RPCs ────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void LightMatchServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsLit || _isLighting.Value || _matchCount.Value <= 0) return;

        if (_lightingCoroutine != null)
            StopCoroutine(_lightingCoroutine);

        _lightingCoroutine = StartCoroutine(LightingCoroutine());
    }

    private IEnumerator LightingCoroutine()
    {
        _isLighting.Value = true;

        yield return new WaitForSeconds(_lightAnimDuration);

        _isLighting.Value = false;
        _isLitVar.Value   = true;

        // Burn timer — extinguishes automatically if not used on a fire pit.
        yield return new WaitForSeconds(_burnDuration);

        _isLitVar.Value    = false;
        _lightingCoroutine = null;
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnIsLitChanged(bool previous, bool current)
    {
        if (current) StartFlameVisuals();
        else         StopFlameVisuals();
    }

    private void OnMatchCountChanged(int previous, int current)
        => UpdateInteractText();

    // ── Visual helpers ─────────────────────────────────────────────────────────

    private void StartFlameVisuals()
    {
        if (_flameVFX != null && !_flameVFX.isPlaying)
            _flameVFX.Play();

        if (_crackleAudio != null && !_crackleAudio.isPlaying)
            _crackleAudio.Play();
    }

    private void StopFlameVisuals()
    {
        if (_flameVFX != null)
            _flameVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (_crackleAudio != null)
            _crackleAudio.Stop();
    }

    private void UpdateInteractText()
        => interactText = $"Matchbox ({_matchCount.Value}/{MaxMatches})";
}
