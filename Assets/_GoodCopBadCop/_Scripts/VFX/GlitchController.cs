using System.Collections.Generic;
using FronkonGames.Glitches.Interferences;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the FronkonGames <see cref="InterferencesVolume"/> based on the player's
/// proximity to the nearest anomalous suspect and that suspect's infection score.
///
/// Attach to any persistent scene GameObject. Assign <see cref="_postProcessingVolume"/>
/// in the Inspector (the global Post Processing volume). Player origin is resolved
/// automatically from Camera.main, or you can pin a specific transform.
/// </summary>
public class GlitchController : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The global Post Processing Volume that holds (or will receive) the InterferencesVolume override.")]
    [SerializeField] private Volume _postProcessingVolume;

    // ── Detection ─────────────────────────────────────────────────────────────

    [Header("Detection")]
    [Tooltip("Suspects inside this radius can trigger the effect.")]
    [SerializeField] private float _detectionRadius = 6f;

    [Tooltip("Seconds between suspect scan passes. Scanning every frame is unnecessary.")]
    [SerializeField] private float _scanInterval = 0.2f;

    // ── Smoothing ─────────────────────────────────────────────────────────────

    [Header("Smoothing")]
    [Tooltip("How quickly the effect fades IN when a signal is detected.")]
    [SerializeField] private float _fadeInSpeed = 5f;

    [Tooltip("How quickly the effect fades OUT when the signal drops.")]
    [SerializeField] private float _fadeOutSpeed = 2f;

    [Tooltip("Maps the raw 0-1 signal (score × proximity) to the final effect intensity.")]
    [SerializeField] private AnimationCurve _intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Effect Parameters at Full Intensity ───────────────────────────────────

    [Header("Effect at Full Intensity")]
    [SerializeField, Range(0f, 10f)]  private float _maxOffset             = 2.5f;
    [SerializeField, Range(0f, 2f)]   private float _maxDistortion         = 1.0f;
    [SerializeField, Range(0f, 100f)] private float _maxDistortionSpeed    = 25f;
    [SerializeField, Range(0f, 10f)]  private float _maxDistortionDensity  = 5f;
    [SerializeField, Range(0f, 5f)]   private float _maxDistortionAmplitude = 0.4f;
    [SerializeField, Range(0f, 1f)]   private float _maxScanlines          = 0.9f;
    [SerializeField, Range(0f, 1f)]   private float _maxScanlinesOpacity   = 0.7f;

    // ── Debug ─────────────────────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("Force the effect on without requiring a nearby anomalous suspect.")]
    [SerializeField] private bool _debugForceGlitch;

    [SerializeField, Range(0f, 1f)]
    private float _debugIntensity = 0.5f;

    [Tooltip("Press this key to infect the nearest suspect with _debugInfectionScore at runtime.")]
    [SerializeField] private KeyCode _debugInfectKey = KeyCode.F7;

    [Tooltip("Press this key to clear any debug infection applied to a suspect.")]
    [SerializeField] private KeyCode _debugClearKey = KeyCode.F8;

    [Tooltip("InfectionScore applied to the nearest suspect when the infect hotkey is pressed.")]
    [SerializeField, Range(0, 100)] private int _debugInfectionScore = 80;

    // ── Internals ─────────────────────────────────────────────────────────────

    private InterferencesVolume _interferences;
    private float _targetIntensity;
    private float _smoothedIntensity;
    private float _scanTimer;

    // Cached results from the last scan to avoid garbage from OverlapSphere each frame.
    private readonly List<SuspectCharacter> _candidateSuspects = new();

    // Suspect that was infected via the debug hotkey, held so we can clean them up.
    private SuspectCharacter _debugInfectedSuspect;

    // Baseline values to lerp FROM at t = 0.
    private const float BASE_OFFSET              = 0f;
    private const float BASE_DISTORTION          = 0f;
    private const float BASE_DISTORTION_SPEED    = 10f;
    private const float BASE_DISTORTION_DENSITY  = 2f;
    private const float BASE_DISTORTION_AMPLITUDE = 0f;
    private const float BASE_SCANLINES           = 0f;
    private const float BASE_SCANLINES_OPACITY   = 0f;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (_postProcessingVolume == null)
        {
            Debug.LogError("[GlitchController] Post Processing Volume is not assigned.", this);
            enabled = false;
            return;
        }

        // volume.profile creates a runtime instance (cloned from sharedProfile) on first access,
        // so we never write back to the asset on disk.
        VolumeProfile profile = _postProcessingVolume.profile;

        if (!profile.TryGet(out _interferences))
        {
            // Add the component at runtime if the profile didn't include one.
            _interferences = profile.Add<InterferencesVolume>(true);
            Debug.Log("[GlitchController] InterferencesVolume was absent from the profile — created at runtime.");
        }

        // Mark all parameters we drive as overrides so the blending system picks them up.
        _interferences.intensity.overrideState           = true;
        _interferences.offset.overrideState              = true;
        _interferences.distortion.overrideState          = true;
        _interferences.distortionSpeed.overrideState     = true;
        _interferences.distortionDensity.overrideState   = true;
        _interferences.distortionAmplitude.overrideState = true;
        _interferences.scanlines.overrideState           = true;
        _interferences.scanlinesOpacity.overrideState    = true;

        ApplyParameters(0f);
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(_debugInfectKey)) DebugInfectNearestSuspect();
        if (Input.GetKeyDown(_debugClearKey))  DebugClearInfection();
#endif

        if (_debugForceGlitch)
        {
            _targetIntensity = _debugIntensity;
        }
        else
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                _targetIntensity = SampleNearbyAnomalies();
            }
        }

        // Asymmetric smoothing: fast attack, slow release.
        float speed = (_smoothedIntensity < _targetIntensity) ? _fadeInSpeed : _fadeOutSpeed;
        _smoothedIntensity = Mathf.Lerp(_smoothedIntensity, _targetIntensity, Time.deltaTime * speed);

        ApplyParameters(_smoothedIntensity);
    }

    // ── Scanning ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates all active SuspectCharacters, keeps those inside the detection radius
    /// that have at least one active anomaly, and returns the strongest combined signal.
    /// Signal = (infectionScore / FULLY_MUTATED_THRESHOLD) × (1 - normalisedDistance).
    /// </summary>
    private float SampleNearbyAnomalies()
    {
        Transform origin = ResolvePlayerTransform();
        if (origin == null) return 0f;

        _candidateSuspects.Clear();

        // FindObjectsByType is called every _scanInterval seconds (default 0.2 s),
        // not every frame, so the cost is acceptable for a small suspect count.
        var allSuspects = FindObjectsByType<SuspectCharacter>(FindObjectsSortMode.None);
        foreach (var suspect in allSuspects)
        {
            float dist = Vector3.Distance(origin.position, suspect.transform.position);
            if (dist > _detectionRadius) continue;

            var anomalies = suspect.AnomalyController;
            if (anomalies == null || anomalies.activeAnomalies.Count == 0) continue;

            _candidateSuspects.Add(suspect);
        }

        if (_candidateSuspects.Count == 0) return 0f;

        float maxSignal = 0f;
        foreach (var suspect in _candidateSuspects)
        {
            float scoreNorm = Mathf.Clamp01(
                (float)suspect.InfectionScore / AnomalyController.FULLY_MUTATED_THRESHOLD);

            float dist      = Vector3.Distance(origin.position, suspect.transform.position);
            float proximity = 1f - Mathf.Clamp01(dist / _detectionRadius);

            float signal = scoreNorm * proximity;
            if (signal > maxSignal) maxSignal = signal;
        }

        return _intensityCurve.Evaluate(maxSignal);
    }

    // ── Parameter Application ─────────────────────────────────────────────────

    /// <summary>
    /// Writes all Interferences parameters based on a normalised intensity [0, 1].
    /// </summary>
    private void ApplyParameters(float t)
    {
        _interferences.intensity.value           = t;
        _interferences.offset.value              = Mathf.Lerp(BASE_OFFSET,               _maxOffset,              t);
        _interferences.distortion.value          = Mathf.Lerp(BASE_DISTORTION,           _maxDistortion,          t);
        _interferences.distortionSpeed.value     = Mathf.Lerp(BASE_DISTORTION_SPEED,     _maxDistortionSpeed,     t);
        _interferences.distortionDensity.value   = Mathf.Lerp(BASE_DISTORTION_DENSITY,   _maxDistortionDensity,   t);
        _interferences.distortionAmplitude.value = Mathf.Lerp(BASE_DISTORTION_AMPLITUDE, _maxDistortionAmplitude, t);
        _interferences.scanlines.value           = Mathf.Lerp(BASE_SCANLINES,            _maxScanlines,           t);
        _interferences.scanlinesOpacity.value    = Mathf.Lerp(BASE_SCANLINES_OPACITY,    _maxScanlinesOpacity,    t);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Transform ResolvePlayerTransform()
    {
        return PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;
    }

    // ── Debug Hotkeys (Editor only) ───────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>
    /// Finds the nearest SuspectCharacter, stamps <see cref="_debugInfectionScore"/> on it,
    /// and calls <see cref="AnomalyController.InitializeByInfectionScore"/> so the normal
    /// scanning path picks up the signal without any override shortcuts.
    /// </summary>
    private void DebugInfectNearestSuspect()
    {
        Transform origin = ResolvePlayerTransform();
        if (origin == null)
        {
            Debug.LogWarning("[GlitchController] Cannot infect suspect — player not found.");
            return;
        }

        SuspectCharacter nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var suspect in FindObjectsByType<SuspectCharacter>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(origin.position, suspect.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = suspect;
            }
        }

        if (nearest == null)
        {
            Debug.LogWarning("[GlitchController] No SuspectCharacter found in the scene.");
            return;
        }

        // Clear any previous debug infection first.
        DebugClearInfection();

        _debugInfectedSuspect          = nearest;
        nearest.InfectionScore         = _debugInfectionScore;
        nearest.AnomalyController?.InitializeByInfectionScore(_debugInfectionScore);

        Debug.Log($"[GlitchController] DEBUG — infected '{nearest.name}' " +
                  $"with score {_debugInfectionScore} ({nearestDist:F1} m away).");
    }

    /// <summary>
    /// Clears the infection applied by <see cref="DebugInfectNearestSuspect"/>,
    /// resetting the suspect to a clean state.
    /// </summary>
    private void DebugClearInfection()
    {
        if (_debugInfectedSuspect == null) return;

        _debugInfectedSuspect.InfectionScore = 0;
        _debugInfectedSuspect.AnomalyController?.InitializeClean();

        Debug.Log($"[GlitchController] DEBUG — cleared infection on '{_debugInfectedSuspect.name}'.");
        _debugInfectedSuspect = null;
    }
#endif

    // ── Gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Transform origin = ResolvePlayerTransform();
        if (origin == null) return;

        Gizmos.color = new Color(0f, 0.9f, 1f, 0.25f);
        Gizmos.DrawWireSphere(origin.position, _detectionRadius);
    }
}
