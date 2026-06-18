using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A piece of graffiti on a checkpoint wall that is gradually scrubbed off by a <see cref="Mop"/>.
///
/// Scrubbing begins when a <see cref="Mop"/> detects this collider via its overlap sphere while
/// the owner holds LMB. Progress is accumulated server-side and replicated via
/// <see cref="_scrubProgress"/>, which all clients use to fade the graffiti renderer.
/// When progress reaches 1 the server notifies <see cref="GraffitiThreat"/> and despawns.
///
/// Multiple players can scrub simultaneously — each active mop increases the scrub rate.
/// Progress persists if scrubbing is interrupted and resumes from where it left off.
///
/// Prefab requirements:
///   - NetworkObject
///   - Collider (any layer detectable by the Mop's <c>_graffitiLayerMask</c>)
///   - <see cref="_graffitiRenderer"/>: Renderer using a URP material with Transparent surface type
///   - Optional: ParticleSystem child assigned to <see cref="_scrubParticles"/>
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GraffitiInteractable : NetworkBehaviour
{
    [Header("Scrub Settings")]
    [Tooltip("Seconds required to fully scrub this graffiti when a single mop is active.")]
    [SerializeField] private float _scrubDuration = 3f;

    [Header("Visual")]
    [Tooltip("Renderer whose material alpha is faded as scrub progress increases. " +
             "Assign a material using a URP Lit or Unlit shader with Transparent surface type.")]
    [SerializeField] private Renderer _graffitiRenderer;

    [Header("VFX")]
    [Tooltip("Optional particle system played while scrubbing is active.")]
    [SerializeField] private ParticleSystem _scrubParticles;

    [Header("Audio")]
    [SerializeField] private AudioClip _scrubSound;
    [SerializeField] [Range(0f, 1f)] private float _scrubSoundVolume = 1f;

    // ── Networked state ────────────────────────────────────────────────────────

    /// <summary>
    /// Scrub progress in [0, 1]. Written by the server; all clients drive the renderer fade from it.
    /// </summary>
    private NetworkVariable<float> _scrubProgress = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Server-only state ──────────────────────────────────────────────────────

    private int _activeScrubbers;
    private Coroutine _progressCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_graffitiRenderer == null)
            Debug.LogWarning($"[GraffitiInteractable] No renderer assigned on '{name}'. Visual fade will not work.", this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _scrubProgress.OnValueChanged += OnScrubProgressChanged;
        // Apply initial visual in case of a late-joining client.
        ApplyScrubVisual(_scrubProgress.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubProgress.OnValueChanged -= OnScrubProgressChanged;
    }

    // ── Scrub control (called from Mop) ───────────────────────────────────────

    /// <summary>
    /// Registers one more active mop. Starts the server-side progress coroutine if not already
    /// running and broadcasts the scrub effect to all clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void StartScrubServerRpc()
    {
        _activeScrubbers++;

        if (_progressCoroutine == null)
        {
            PlayScrubEffectClientRpc(transform.position);
            _progressCoroutine = StartCoroutine(ProgressRoutine());
        }
    }

    /// <summary>
    /// Deregisters one active mop. Stops the progress coroutine when no mops remain active.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void StopScrubServerRpc()
    {
        _activeScrubbers = Mathf.Max(0, _activeScrubbers - 1);

        if (_activeScrubbers == 0 && _progressCoroutine != null)
        {
            StopCoroutine(_progressCoroutine);
            _progressCoroutine = null;
            StopScrubEffectClientRpc();
        }
    }

    // ── Progress coroutine (server only) ──────────────────────────────────────

    private IEnumerator ProgressRoutine()
    {
        while (_scrubProgress.Value < 1f)
        {
            // Progress rate scales with the number of active mops.
            float rate = _activeScrubbers / _scrubDuration;
            _scrubProgress.Value = Mathf.Clamp01(_scrubProgress.Value + rate * Time.deltaTime);
            yield return null;
        }

        _progressCoroutine = null;
        GraffitiThreat.Instance?.OnGraffitiScrubbed();
        NetworkObject.Despawn(destroy: true);
    }

    // ── Visual ─────────────────────────────────────────────────────────────────

    private void OnScrubProgressChanged(float previous, float current)
    {
        ApplyScrubVisual(current);
    }

    /// <summary>
    /// Sets the graffiti renderer's material alpha to <c>1 - progress</c>.
    /// The material must be set to Transparent surface type in URP for alpha to take effect.
    /// </summary>
    private void ApplyScrubVisual(float progress)
    {
        if (_graffitiRenderer == null) return;

        // Use a MaterialPropertyBlock to avoid creating a new material instance per change.
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        _graffitiRenderer.GetPropertyBlock(block);
        Color c = _graffitiRenderer.sharedMaterial != null
            ? _graffitiRenderer.sharedMaterial.color
            : Color.white;
        c.a = 1f - progress;
        block.SetColor("_BaseColor", c);
        _graffitiRenderer.SetPropertyBlock(block);
    }

    // ── Effects ────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayScrubEffectClientRpc(Vector3 position)
    {
        _scrubParticles?.Play();

        if (SFXController.Instance != null && _scrubSound != null)
            SFXController.Instance.PlayAtPosition(_scrubSound, position, _scrubSoundVolume);
    }

    [ClientRpc]
    private void StopScrubEffectClientRpc()
    {
        _scrubParticles?.Stop();
    }
}
