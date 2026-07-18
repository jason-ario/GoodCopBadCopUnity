using DG.Tweening;
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

    // ── Private state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Server-authoritative hit counter. Only incremented by <see cref="RegisterHit"/>
    /// which is called exclusively on the server inside MutantSuspectBehaviour.
    /// </summary>
    private int _hits;

    private MeshRenderer _crackRenderer;
    private MaterialPropertyBlock _mpb;
    private AudioSource _audioSource;
    private Tween _shakeTween;

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

        // Start fully transparent / undamaged.
        RefreshCrackOverlay(0);
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
    /// hides the intact glass (and its collider), activates the broken shards,
    /// and plays the smash sound.
    /// Called on all clients via ClientRpc on the final blow.
    /// </summary>
    public void ApplySmash()
    {
        if (_audioSource != null && _smashClip != null)
            _audioSource.PlayOneShot(_smashClip, _hitVolume);

        if (_normalGlass != null)
            _normalGlass.SetActive(false);

        if (_brokenGlass != null)
            _brokenGlass.SetActive(true);
    }

    /// <summary>
    /// Resets the glass to full health and restores undamaged visuals.
    /// Call at the start of each new session/day (analogous to ShutterController.ResetShutter).
    /// </summary>
    public void ResetGlass()
    {
        _hits = 0;

        if (_normalGlass != null)
            _normalGlass.SetActive(true);

        if (_brokenGlass != null)
            _brokenGlass.SetActive(false);

        RefreshCrackOverlay(0);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

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
