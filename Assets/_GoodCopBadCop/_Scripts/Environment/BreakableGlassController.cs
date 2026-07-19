using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the breakable booth window glass.
///
/// Tracks cumulative hit damage across mutant visits, updates a procedural crack overlay via
/// MaterialPropertyBlock, and handles the full transition to the broken state when smashed through.
///
/// Architecture mirrors ShutterController:
/// – This MonoBehaviour is a singleton; no NetworkBehaviour is needed here.
/// – The server calls <see cref="RegisterHit"/> to advance health (only once per RPC cycle).
/// – All clients update visuals via <see cref="OnHitByMutant"/> or <see cref="ApplySmash"/>,
///   which are called inside ClientRpcs on <see cref="MutantSuspectBehaviour"/>.
/// </summary>
public class BreakableGlassController : MonoBehaviour
{
    public static BreakableGlassController Instance { get; private set; }

    // ── Serialized fields ──────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The intact glass mesh child (MeshFilter + MeshRenderer + MeshCollider).")]
    [SerializeField] private GameObject _normalGlass;

    [Tooltip("The pre-shattered glass pieces child. Kept inactive until fully smashed.")]
    [SerializeField] private GameObject _brokenGlass;

    [Tooltip("Prefab used to re-instantiate the broken glass after a repair purchase. " +
             "Create this prefab from the Broken Glass scene child — ensure its root is inactive so " +
             "AddForceOnAwake fires only when the glass actually shatters.")]
    [SerializeField] private GameObject _brokenGlassPrefab;

    [Tooltip("The purchase interactable shown when the glass is smashed. " +
             "Should start inactive in the scene; activated automatically when the glass breaks.")]
    [SerializeField] private WorldPurchaseActionInteractable _repairInteractable;

    [Tooltip("Material using the GoodCopBadCop/GlassCrackOverlay shader. " +
             "Instantiated as a crack overlay at runtime on top of the normal glass mesh.")]
    [SerializeField] private Material _crackMaterial;

    [Header("Health")]
    [Tooltip("Total hits required to smash through the glass. Default 4 gives three " +
             "intermediate damage stages plus the final smash.")]
    [SerializeField] [Min(1)] private int _maxHits = 4;

    [Header("Hit Feedback")]
    [Tooltip("Sound played each time the mutant lands an intermediate hit on the glass.")]
    [SerializeField] private AudioClip _hitClip;

    [Tooltip("Sound played when the glass fully shatters.")]
    [SerializeField] private AudioClip _smashClip;

    [Tooltip("Volume scale for both hit and smash clips.")]
    [SerializeField] [Range(0f, 2f)] private float _hitVolume = 1f;

    [Tooltip("The glass Transform to shake on each hit. Leave empty to use _normalGlass.")]
    [SerializeField] private Transform _shakeTarget;

    [Tooltip("Duration of the shake per hit in seconds.")]
    [SerializeField] private float _shakeDuration = 0.20f;

    [Tooltip("Maximum positional offset during the shake.")]
    [SerializeField] private float _shakeStrength = 0.03f;

    [Tooltip("Oscillation count during the shake.")]
    [SerializeField] private int _shakeVibrato = 14;

    [Header("Repair Feedback")]
    [Tooltip("Sound played on all clients when the glass is successfully repaired.")]
    [SerializeField] private AudioClip _repairClip;

    [Tooltip("Volume scale for the repair sound.")]
    [SerializeField] [Range(0f, 2f)] private float _repairVolume = 1f;

    [Tooltip("Particle system played on all clients when the glass is repaired. " +
             "Assign the in-scene RepairParticles child of this GameObject.")]
    [SerializeField] private ParticleSystem _repairParticles;

    [Header("Broken Glass Despawn")]
    [Tooltip("Seconds after shattering before the broken glass pieces are destroyed.")]
    [SerializeField] private float _brokenGlassDespawnDelay = 3f;

    // ── Private state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Server-authoritative hit counter. Only incremented by <see cref="RegisterHit"/>
    /// which is called exclusively on the server inside MutantSuspectBehaviour.
    /// </summary>
    private int _hits;
    private bool _saveRestoreComplete;

    private MeshRenderer _crackRenderer;
    private MaterialPropertyBlock _mpb;
    private AudioSource _audioSource;
    private Tween _shakeTween;
    private Coroutine _despawnCoroutine;

    // Cached spawn data so the broken glass can be re-instantiated at the original transform.
    private Transform  _brokenGlassParent;
    private Vector3    _brokenGlassLocalPos;
    private Quaternion _brokenGlassLocalRot;

    private static readonly int CrackProgressId = Shader.PropertyToID("_CrackProgress");

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>True when the glass has received enough hits to be fully smashed.</summary>
    public bool IsSmashed => _hits >= _maxHits;

    /// <summary>
    /// True on all clients while the intact glass pane is visible.
    /// Updated via <see cref="ApplySmash"/> so it is reliable on every client,
    /// unlike <see cref="IsSmashed"/> which is server-side only.
    /// Used by <see cref="ScriptedDialogueRunner"/> to suppress the face-cam when the
    /// glass would obscure the close-up view.
    /// </summary>
    public bool IsWindowVisible => _normalGlass != null && _normalGlass.activeSelf;

    /// <summary>Current server-side hit count.</summary>
    public int CurrentHits => _hits;

    /// <summary>Maximum hits before the glass shatters.</summary>
    public int MaxHits => _maxHits;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        _audioSource = GetComponent<AudioSource>();
        _mpb = new MaterialPropertyBlock();

        if (_normalGlass != null)
            BuildCrackOverlay();

        // Cache broken glass spawn data so it can be re-instantiated after despawn.
        if (_brokenGlass != null)
        {
            _brokenGlassParent   = _brokenGlass.transform.parent;
            _brokenGlassLocalPos = _brokenGlass.transform.localPosition;
            _brokenGlassLocalRot = _brokenGlass.transform.localRotation;
        }

        // Start fully transparent / undamaged.
        RefreshCrackOverlay(0);
    }

    /// <summary>
    /// Restores glass and repair interactable state from the save file after one frame,
    /// giving the network manager time to fully initialise before we broadcast.
    /// </summary>
    private System.Collections.IEnumerator Start()
    {
        yield return null;
        if (!_saveRestoreComplete)
        {
            _saveRestoreComplete = true;
            RestoreGlassStateFromSave();
        }
    }

    /// <summary>
    /// Explicitly re-runs the save-state restore. Safe to call from outside if the internal
    /// Start() coroutine was interrupted (e.g. the GameObject was deactivated during the main menu).
    /// No-ops if the restore has already completed.
    /// </summary>
    public void RefreshFromSave()
    {
        if (_saveRestoreComplete) return;
        _saveRestoreComplete = true;
        RestoreGlassStateFromSave();
    }

    private void BuildCrackOverlay()
    {
        if (_crackMaterial == null) return;

        var overlay = new GameObject("CrackOverlay");
        overlay.transform.SetParent(_normalGlass.transform, false);
        overlay.transform.localPosition = Vector3.zero;
        overlay.transform.localRotation = Quaternion.identity;
        overlay.transform.localScale    = Vector3.one;

        // Share the same mesh as the normal glass.
        var sourceMf = _normalGlass.GetComponent<MeshFilter>();
        if (sourceMf != null)
        {
            var mf = overlay.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMf.sharedMesh;
        }

        _crackRenderer = overlay.AddComponent<MeshRenderer>();
        _crackRenderer.sharedMaterial            = _crackMaterial;
        _crackRenderer.shadowCastingMode          = ShadowCastingMode.Off;
        _crackRenderer.receiveShadows             = false;
        _crackRenderer.lightProbeUsage            = LightProbeUsage.Off;
        _crackRenderer.reflectionProbeUsage       = ReflectionProbeUsage.Off;
        _crackRenderer.allowOcclusionWhenDynamic  = false;
    }

    // ── Public server-side API ─────────────────────────────────────────────────

    /// <summary>
    /// Registers one hit on the server. Returns the new total hit count.
    /// Must only be called on the server; follow up immediately with a ClientRpc
    /// to sync visuals on all clients.
    /// </summary>
    public int RegisterHit()
    {
        if (IsSmashed) return _hits;
        _hits = Mathf.Min(_hits + 1, _maxHits);
        return _hits;
    }

    /// <summary>
    /// Debug helper — immediately applies the fully smashed state locally.
    /// Useful for cheat console testing. Sets the server-side hit counter so
    /// <see cref="IsSmashed"/> is also consistent.
    /// </summary>
    public void ForceSmash()
    {
        _hits = _maxHits;
        ApplySmash();
    }

    // ── Public client-side (visual) API ───────────────────────────────────────

    /// <summary>
    /// Updates the crack overlay to the given hit count and plays hit feedback.
    /// Called on all clients via ClientRpc after each intermediate hit.
    /// </summary>
    public void OnHitByMutant(int hitCount)
    {
        RefreshCrackOverlay(hitCount);
        PlayHitFeedback();
    }

    /// <summary>
    /// Transitions to the fully smashed state on all clients:
    /// hides the intact glass, activates the broken shards, plays the smash sound,
    /// shows the repair interactable, and schedules the broken pieces for destruction.
    /// Called on all clients via ClientRpc on the final blow.
    /// </summary>
    public void ApplySmash()
    {
        if (_audioSource != null && _smashClip != null)
            _audioSource.PlayOneShot(_smashClip, _hitVolume);

        if (_normalGlass != null)
            _normalGlass.SetActive(false);

        if (_brokenGlass != null)
        {
            _brokenGlass.SetActive(true);

            // Destroy the shards after a short delay — runs on all clients since ApplySmash
            // is already called in a ClientRpc context.
            if (_despawnCoroutine != null) StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = StartCoroutine(DespawnBrokenGlassCoroutine());
        }

        // Show the repair option — safe to call directly since ApplySmash already runs on all clients.
        _repairInteractable?.SetAvailable(true);

        // The repair interactable's GameObject starts inactive so NGO never auto-spawns its
        // NetworkObject.  We must spawn it explicitly from the server so that the purchase
        // ServerRpc / ClientRpc path can route correctly.
        if (_repairInteractable != null)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsServer || nm.IsHost))
            {
                var repairNetObj = _repairInteractable.NetworkObject;
                if (repairNetObj != null && !repairNetObj.IsSpawned)
                    repairNetObj.Spawn(true);   // true = destroyWithScene
            }
        }

        // Persist the smashed state (server/host only — SaveDataManager.Save() guards writes).
        SaveDataManager.Instance?.SetGlassSmashed(true);
    }

    /// <summary>
    /// Resets the glass to full health on all clients:
    /// cancels any pending despawn, destroys leftover broken shards, re-enables the intact pane,
    /// and pre-instantiates a fresh (inactive) broken glass ready for the next smash.
    /// </summary>
    public void ResetGlass()
    {
        _hits = 0;

        // Cancel any in-flight despawn so we can clean up and respawn cleanly.
        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }

        // Destroy remaining broken glass pieces if they are still present.
        if (_brokenGlass != null)
        {
            Destroy(_brokenGlass);
            _brokenGlass = null;
        }

        // Restore the intact pane, clear crack overlay, and pop it in with a punch scale.
        if (_normalGlass != null)
        {
            _normalGlass.SetActive(true);
            _normalGlass.transform.DOKill();
            _normalGlass.transform.DOPunchScale(Vector3.one * 0.12f, 0.35f, 6, 0.5f);
        }

        RefreshCrackOverlay(0);

        // Play repair feedback on all clients (ResetGlass is called inside ExecutePurchaseClientRpc).
        if (_audioSource != null && _repairClip != null)
            _audioSource.PlayOneShot(_repairClip, _repairVolume);

        _repairParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _repairParticles?.Play();

        // Pre-instantiate a fresh, inactive broken glass so it is ready for the next smash.
        RespawnBrokenGlass();

        // Hide the repair interactable — safe to call directly since ResetGlass already runs on all clients.
        _repairInteractable?.SetAvailable(false);

        // Persist the restored state (server/host only).
        SaveDataManager.Instance?.SetGlassSmashed(false);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the saved glass state and re-applies it at session start.
    /// Glass visuals are restored locally on all peers; the repair interactable's
    /// visibility is then broadcast from the server so every client stays in sync.
    /// The broken glass pieces are intentionally left inactive on load — they were
    /// despawned in the previous session and will reappear only on the next smash.
    /// </summary>
    private void RestoreGlassStateFromSave()
    {
        if (SaveDataManager.Instance == null || !SaveDataManager.Instance.IsGlassSmashed) return;

        // Glass was smashed and not yet repaired — hide the intact pane.
        _hits = _maxHits;
        if (_normalGlass != null) _normalGlass.SetActive(false);
        // Broken glass pieces were already despawned; leave _brokenGlass inactive.

        if (_repairInteractable == null) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            // Offline / editor — set directly on the local instance.
            _repairInteractable.SetAvailable(true);
        }
        else if (nm.IsServer || nm.IsHost)
        {
            // The repair interactable starts inactive so NGO never auto-spawns its NetworkObject.
            // Activate and spawn it manually before broadcasting so SetAvailableServerRpc can route.
            var repairNetObj = _repairInteractable.NetworkObject;
            if (repairNetObj != null && !repairNetObj.IsSpawned)
            {
                _repairInteractable.gameObject.SetActive(true);
                repairNetObj.Spawn(true);   // true = destroyWithScene
            }
            // Host broadcasts availability to all connected clients.
            _repairInteractable.SetAvailableServerRpc(true);
        }
        // Clients will receive the availability change via the ClientRpc triggered above.
    }

    /// <summary>
    /// Waits for <see cref="_brokenGlassDespawnDelay"/> seconds then destroys the broken glass pieces.
    /// </summary>
    private System.Collections.IEnumerator DespawnBrokenGlassCoroutine()
    {
        yield return new WaitForSeconds(_brokenGlassDespawnDelay);

        if (_brokenGlass != null)
        {
            Destroy(_brokenGlass);
            _brokenGlass = null;
        }

        _despawnCoroutine = null;
    }

    /// <summary>
    /// Instantiates a fresh broken glass object as an inactive child, ready for the next smash.
    /// Logs a warning and skips silently if <see cref="_brokenGlassPrefab"/> is not assigned.
    /// </summary>
    private void RespawnBrokenGlass()
    {
        if (_brokenGlassPrefab == null)
        {
            Debug.LogWarning("[BreakableGlassController] _brokenGlassPrefab is not assigned — " +
                             "broken glass will not respawn after repair. " +
                             "Assign the Broken Glass prefab in the Inspector.");
            return;
        }

        _brokenGlass = Instantiate(_brokenGlassPrefab, _brokenGlassParent);
        _brokenGlass.transform.SetLocalPositionAndRotation(_brokenGlassLocalPos, _brokenGlassLocalRot);

        // Keep it inactive until the next smash triggers ApplySmash().
        _brokenGlass.SetActive(false);
    }

    /// <summary>Sets the _CrackProgress material property to reflect the given hit count.</summary>
    private void RefreshCrackOverlay(int hitCount)
    {
        if (_crackRenderer == null) return;

        float progress = _maxHits > 0 ? (float)hitCount / _maxHits : 0f;
        _mpb.SetFloat(CrackProgressId, progress);
        _crackRenderer.SetPropertyBlock(_mpb);
    }

    private void PlayHitFeedback()
    {
        if (_audioSource != null && _hitClip != null)
            _audioSource.PlayOneShot(_hitClip, _hitVolume);

        var target = _shakeTarget != null
            ? _shakeTarget
            : (_normalGlass != null ? _normalGlass.transform : null);

        if (target != null)
        {
            _shakeTween?.Kill(complete: true);
            _shakeTween = target.DOShakePosition(_shakeDuration, _shakeStrength, _shakeVibrato);
        }
    }
}
