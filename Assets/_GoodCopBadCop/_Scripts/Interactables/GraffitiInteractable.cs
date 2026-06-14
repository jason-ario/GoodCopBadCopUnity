using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A piece of graffiti on a checkpoint wall that can be scrubbed off with a <see cref="Mop"/>.
///
/// Spawned at runtime by <see cref="CleanGraffitiTask"/>. When the player left-clicks this
/// object while holding a Mop the scrub sequence starts: a particle effect plays on every
/// client, and after <see cref="_scrubDuration"/> seconds the server despawns the object and
/// notifies <see cref="CleanGraffitiTask.OnGraffitiScrubbed"/>.
///
/// Prefab requirements:
///   - NetworkObject
///   - HighlightEffect  (required by Interactable base)
///   - Collider on the Interactable layer
///   - Mop PickableItemData assigned to <c>itemsThatCanInteractWith</c> in the Inspector
///   - Visual child (MeshRenderer / SpriteRenderer) representing the graffiti art
///   - Optional: ParticleSystem child assigned to <see cref="_scrubParticles"/>
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GraffitiInteractable : Interactable
{
    private const string InteractText = "Scrub Graffiti";

    [Header("Scrub Settings")]
    [Tooltip("Seconds the scrubbing effect plays before the graffiti is despawned.")]
    [SerializeField] private float _scrubDuration = 0.8f;

    [Header("VFX")]
    [Tooltip("Optional particle system played on all clients when scrubbing starts.")]
    [SerializeField] private ParticleSystem _scrubParticles;

    [Header("Audio")]
    [SerializeField] private AudioClip _scrubSound;
    [SerializeField] [Range(0f, 1f)] private float _scrubSoundVolume = 1f;

    // Local guard: prevents a second click reaching the server before the first resolves.
    private bool _beingScrubbed;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractText;
    }

    // ── Interact ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerInteractionController"/> when the player left-clicks this
    /// graffiti while holding a Mop. The Mop <see cref="PickableItemData"/> must be listed
    /// in <c>itemsThatCanInteractWith</c> on the prefab.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        if (_beingScrubbed) return;
        if (GraffitiThreat.Instance == null) return;
        if (item is not Mop) return;

        base.InteractWithItem(player, item);

        _beingScrubbed = true;

        // Immediate local feedback on the clicking client — reduces perceived latency.
        PlayScrubEffectLocally();

        ScrubServerRpc();
    }

    // ── Scrub sequence (server) ───────────────────────────────────────────────

    /// <summary>
    /// Server-authoritative entry point. Broadcasts the scrub effect to all other clients,
    /// waits for the effect to play out, then notifies the task and despawns this object.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ScrubServerRpc()
    {
        StartCoroutine(ScrubSequence());
    }

    private IEnumerator ScrubSequence()
    {
        // Broadcast effect to all clients (originating client already started locally).
        PlayScrubEffectClientRpc(transform.position);

        yield return new WaitForSeconds(_scrubDuration);

        // Notify threat before despawn — once the object is destroyed the coroutine stops.
        GraffitiThreat.Instance?.OnGraffitiScrubbed();

        NetworkObject.Despawn(destroy: true);
    }

    // ── Effect helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the scrub visual and audio on all clients except the one that initiated the scrub,
    /// which already called <see cref="PlayScrubEffectLocally"/> in <see cref="InteractWithItem"/>.
    /// </summary>
    [ClientRpc]
    private void PlayScrubEffectClientRpc(Vector3 position)
    {
        // The originating client already played the effect locally — skip to avoid doubling.
        if (_beingScrubbed) return;

        _beingScrubbed = true;
        PlayScrubEffectLocally();
    }

    private void PlayScrubEffectLocally()
    {
        _scrubParticles?.Play();

        if (SFXController.Instance != null && _scrubSound != null)
            SFXController.Instance.PlayAtPosition(_scrubSound, transform.position, _scrubSoundVolume);
    }
}
