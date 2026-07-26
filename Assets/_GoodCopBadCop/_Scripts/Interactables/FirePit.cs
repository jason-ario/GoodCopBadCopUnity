using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A world-placed fire pit that can be ignited by an ignition source such as a
/// <see cref="Match"/> or the <see cref="Flamethrower"/>.
///
/// Once lit, the fire burns for <see cref="_burnDuration"/> seconds and then fades
/// out over <see cref="_fadeOutDuration"/> seconds before extinguishing.
/// The pit can be re-lit after it goes out, or while already burning (resets the timer).
///
/// If <see cref="_burnIndefinitely"/> is enabled, the timer is skipped entirely and the
/// fire stays lit until <see cref="Extinguish"/> is called explicitly — used for narrative
/// fires (e.g. the fire barrel) that must keep burning through an entire shift and only go
/// out as part of a scripted day transition, after the End of Shift Report has been shown.
///
/// Prefab requirements:
///   - NetworkObject
///   - HighlightEffect       (required by Interactable)
///   - Collider on the Interactable layer
///   - "_fireVFX"            → child ParticleSystem that plays the fire effect
///   - "_fireLight"          → (optional) child Light for a warm glow
///   - "_fireAudioSource"    → (optional) looping AudioSource for crackling sound
///   - "itemsThatCanInteractWith" → include MatchStick.asset so the highlight and
///     interaction routing work correctly
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FirePit : Interactable, IIgnitable
{
    [Header("Fire Pit — VFX")]
    [Tooltip("Child ParticleSystem that plays when the fire is lit.")]
    [SerializeField] private ParticleSystem _fireVFX;

    [Tooltip("Optional child Light that glows while the fire is burning.")]
    [SerializeField] private Light _fireLight;

    [Header("Fire Pit — Duration")]
    [Tooltip("If true, the fire ignores _burnDuration and stays lit indefinitely once ignited. " +
             "It will only go out when Extinguish() is called explicitly (e.g. a scripted day " +
             "transition after the End of Shift Report has been shown). Use this for narrative " +
             "fires that must burn through an entire shift regardless of real-world duration.")]
    [SerializeField] private bool _burnIndefinitely = false;

    [Tooltip("Total burn time in seconds before the fire naturally extinguishes (~3 minutes default). Ignored if _burnIndefinitely is true.")]
    [SerializeField] private float _burnDuration = 180f;

    [Tooltip("Seconds at the end of the burn during which the flame fades out. Ignored if _burnIndefinitely is true.")]
    [SerializeField] private float _fadeOutDuration = 10f;

    [Header("Fire Pit — Audio")]
    [Tooltip("Looping AudioSource for crackling fire sound.")]
    [SerializeField] private AudioSource _fireAudioSource;

    // ── Networked state ────────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _isLit = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True while the fire pit is burning.</summary>
    public bool IsLit => _isLit.Value;

    // ── Local state ────────────────────────────────────────────────────────────

    private Coroutine _burnCoroutine;
    private Coroutine _fadeCoroutine;
    private float _baseLightIntensity;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = "Fire Pit";

        if (_fireLight != null)
            _baseLightIntensity = _fireLight.intensity;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isLit.OnValueChanged += OnIsLitChanged;

        // Sync visual state for late-joining clients.
        if (_isLit.Value)
            StartFireVisuals();
        else
            StopFireVisuals(immediate: true);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isLit.OnValueChanged -= OnIsLitChanged;
    }

    // ── IIgnitable ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Lights the fire pit, or resets the burn timer if already lit.
    /// </summary>
    public void Ignite()
    {
        if (!IsServer) return;

        _isLit.Value = true;

        if (_burnCoroutine != null)
        {
            StopCoroutine(_burnCoroutine);
            _burnCoroutine = null;
        }

        // Indefinite fires (e.g. the narrative fire barrel) skip the timer entirely and
        // rely solely on an explicit Extinguish() call — typically fired from a scripted
        // day transition once the End of Shift Report has finished playing.
        if (!_burnIndefinitely)
            _burnCoroutine = StartCoroutine(BurnCoroutine());
    }

    /// <summary>
    /// Server-only. Immediately puts the fire out, bypassing the burn timer and fade-out.
    /// Use for scripted day transitions (e.g. the fire should be out by the next day).
    /// </summary>
    public void Extinguish()
    {
        if (!IsServer) return;

        if (_burnCoroutine != null)
        {
            StopCoroutine(_burnCoroutine);
            _burnCoroutine = null;
        }

        _isLit.Value = false;
    }

    // ── Interactable overrides ─────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerInteractionController"/> when the local player
    /// uses a compatible item (e.g. a <see cref="Match"/>) on this fire pit.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        base.InteractWithItem(player, item);
        IgniteWithItemServerRpc();
    }

    // ── Server RPCs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side: validates the sender holds a recognised ignition item, ignites the
    /// fire pit, then instructs the sender's client to consume (destroy) the item.
    /// RequireOwnership = false so any client can ignite regardless of NetworkObject ownership.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void IgniteWithItemServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var client))
        {
            Debug.LogWarning($"[FirePit] IgniteWithItemServerRpc: sender {senderId} not found.");
            return;
        }

        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null) return;

        PickableObject held = ppc.HeldObject;
        if (held == null) return;

        // ── Path A: IIgnitionSource items (e.g. MatchBox) ─────────────────────
        // These manage their own lit state and match consumption; no item destruction.
        if (held is IIgnitionSource ignitionSource)
        {
            if (!ignitionSource.IsLit)
            {
                Debug.Log("[FirePit] IgniteWithItemServerRpc: ignition source is not yet lit.");
                return;
            }

            Ignite();
            ignitionSource.OnUsedToIgnite();
            return;
        }

        // ── Path B: Legacy single-use items (e.g. standalone MatchStick) ──────
        // Validated against itemsThatCanInteractWith; consumed (despawned) after use.
        bool validIgnitionItem = false;
        foreach (PickableItemData data in itemsThatCanInteractWith)
        {
            if (held.ItemData == data)
            {
                validIgnitionItem = true;
                break;
            }
        }

        if (!validIgnitionItem)
        {
            Debug.LogWarning($"[FirePit] IgniteWithItemServerRpc: held item '{held.name}' is not a valid ignition item.");
            return;
        }

        Ignite();

        // Consume the single-use item on the sender's client.
        ConsumeItemClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
        });
    }

    /// <summary>
    /// Received only by the player who used the ignition item. Despawns the equipped item.
    /// </summary>
    [ClientRpc]
    private void ConsumeItemClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        if (ppc == null)
        {
            Debug.LogWarning("[FirePit] ConsumeItemClientRpc: could not find local PlayerPickupController.");
            return;
        }

        ppc.DestroyEquippedItem();
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void OnIsLitChanged(bool previous, bool current)
    {
        if (current)
            StartFireVisuals();
        else
            StopFireVisuals(immediate: false);
    }

    // ── Burn lifecycle (server only) ───────────────────────────────────────────

    private IEnumerator BurnCoroutine()
    {
        float activeDuration = Mathf.Max(0f, _burnDuration - _fadeOutDuration);

        yield return new WaitForSeconds(activeDuration);

        // Begin the visual fade-out on all clients (including host).
        if (_fadeOutDuration > 0f)
            BeginFadeOutClientRpc(_fadeOutDuration);

        yield return new WaitForSeconds(_fadeOutDuration);

        _isLit.Value = false;
        _burnCoroutine = null;
    }

    // ── Client RPCs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Tells all clients to start fading out fire visuals over <paramref name="duration"/> seconds.
    /// </summary>
    [ClientRpc]
    private void BeginFadeOutClientRpc(float duration)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeOutVisuals(duration));
    }

    // ── Visual helpers ─────────────────────────────────────────────────────────

    private void StartFireVisuals()
    {
        // Cancel any in-progress fade so it does not fight the restored intensity.
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (_fireVFX != null)
        {
            var emission = _fireVFX.emission;
            emission.enabled = true;
            if (!_fireVFX.isPlaying)
                _fireVFX.Play();
        }

        if (_fireLight != null)
        {
            _fireLight.intensity = _baseLightIntensity;
            _fireLight.enabled = true;
        }

        if (_fireAudioSource != null && !_fireAudioSource.isPlaying)
            _fireAudioSource.Play();
    }

    private void StopFireVisuals(bool immediate)
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (_fireVFX != null)
            _fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (_fireLight != null)
        {
            if (immediate)
                _fireLight.intensity = 0f;
            _fireLight.enabled = false;
        }

        if (_fireAudioSource != null)
            _fireAudioSource.Stop();
    }

    private IEnumerator FadeOutVisuals(float duration)
    {
        // Stop new particles but let existing ones die naturally.
        if (_fireVFX != null)
            _fireVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (_fireAudioSource != null)
            _fireAudioSource.Stop();

        if (_fireLight == null || duration <= 0f)
            yield break;

        float startIntensity = _fireLight.intensity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            _fireLight.intensity = Mathf.Lerp(startIntensity, 0f, elapsed / duration);
        }

        _fireLight.intensity = 0f;
        _fadeCoroutine = null;
    }
}
