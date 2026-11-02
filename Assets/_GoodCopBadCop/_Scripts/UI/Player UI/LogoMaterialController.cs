using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the main menu logo's burn-in and random glitch bursts.
///
/// The burn restarts from fully burned (invisible) every time the logo is enabled, i.e. every
/// time the Home Screen opens. It is driven from Update with a capped step (so a scene-reload
/// hitch can't skip it) and the target value is re-asserted every frame afterwards, so the logo
/// always ends at _targetBurnProgress while active. If another system swaps the Graphic's
/// material or overwrites _BurnProgress, it is corrected and a one-time warning is logged.
///
/// Writes go to a runtime copy of <see cref="_logoMaterial"/> so the shared asset is never
/// modified (previously an interrupted burn left the asset saved at _BurnProgress = 1).
/// </summary>
public class LogoMaterialController : MonoBehaviour
{
    private static readonly int BurnProgressId   = Shader.PropertyToID("_BurnProgress");
    private static readonly int GlitchStrengthId = Shader.PropertyToID("_GlitchStrength");

    [Header("Burn")]
    [SerializeField] private Material _logoMaterial;
    [SerializeField] private float _delay = 1f;
    [SerializeField] private float _duration = 2f;
    [SerializeField] private float _targetBurnProgress = 0.23f;
    [SerializeField] private AudioClip _burnSound;

    [Header("Glitch")]
    [Tooltip("When off, the logo never glitches (no shader glitch, no glitch audio). Used by the Loading screen.")]
    [SerializeField] private bool _enableGlitch = true;
    [SerializeField] private AudioSource _glitchAudioSource;
    [SerializeField] private float _glitchIntervalMin = 3f;
    [SerializeField] private float _glitchIntervalMax = 8f;
    [SerializeField] private float _glitchDurationMin = 0.15f;
    [SerializeField] private float _glitchDurationMax = 1.5f;
    [SerializeField, Range(0f, 1f)] private float _glitchIntensity = 0.5f;

    // Largest per-frame step for the burn, so a scene-load hitch (a reload can produce a multi-second
    // first frame) can't swallow the animation. Same 1/30s cap as the splash-screen fades.
    private const float MaxBurnStep = 1f / 30f;

    private Material _runtimeMaterial;
    private Graphic _graphic;
    private float _burnElapsed;
    private bool _burnFinished;
    private bool _warnedMaterialSwap;
    private bool _warnedExternalWrite;

    private void Awake()
    {
        if (_logoMaterial == null)
            return;

        _runtimeMaterial = new Material(_logoMaterial) { name = _logoMaterial.name + " (Runtime)" };
        _runtimeMaterial.SetFloat(BurnProgressId, 1f);
        _runtimeMaterial.SetFloat(GlitchStrengthId, 0f);

        if (TryGetComponent(out _graphic))
            _graphic.material = _runtimeMaterial;
    }

    private void OnEnable()
    {
        if (_runtimeMaterial == null)
            return;

        // Every time the home screen opens, restart the burn from fully burned (invisible).
        // The burn is driven from Update (not a coroutine) and re-asserted every frame, so it
        // always converges to _targetBurnProgress while the logo is active.
        _burnElapsed = -Mathf.Max(0f, _delay);
        _burnFinished = false;
        _runtimeMaterial.SetFloat(BurnProgressId, 1f);

        if (_enableGlitch)
            StartCoroutine(GlitchLoop());
    }

    private void Update()
    {
        if (_runtimeMaterial == null)
            return;

        EnsureRuntimeMaterialAssigned();

        if (!_burnFinished)
        {
            _burnElapsed += Mathf.Min(Time.unscaledDeltaTime, MaxBurnStep);
            float t = _duration > 0f ? Mathf.Clamp01(_burnElapsed / _duration) : 1f;
            _runtimeMaterial.SetFloat(BurnProgressId, _burnElapsed <= 0f ? 1f : Mathf.Lerp(1f, _targetBurnProgress, t));

            if (t >= 1f)
            {
                _burnFinished = true;
                if (_burnSound != null && SFXController.Instance != null)
                    SFXController.Instance.Play(_burnSound);
            }
            return;
        }

        // Burn done: hold the target. If anything else overwrote it, put it back and say so once.
        if (!Mathf.Approximately(_runtimeMaterial.GetFloat(BurnProgressId), _targetBurnProgress))
        {
            if (!_warnedExternalWrite)
            {
                _warnedExternalWrite = true;
                Debug.LogWarning($"[LogoMaterialController] _BurnProgress on '{name}' was changed externally " +
                                 $"({_runtimeMaterial.GetFloat(BurnProgressId):F3}); restoring {_targetBurnProgress:F3}.", this);
            }
            _runtimeMaterial.SetFloat(BurnProgressId, _targetBurnProgress);
        }
    }

    /// <summary>
    /// Re-applies the runtime copy if something replaced the Graphic's material; otherwise burn
    /// writes would go to a material that isn't being drawn. Warns once with the culprit.
    /// </summary>
    private void EnsureRuntimeMaterialAssigned()
    {
        if (_graphic == null || _graphic.material == _runtimeMaterial)
            return;

        if (!_warnedMaterialSwap)
        {
            _warnedMaterialSwap = true;
            Debug.LogWarning($"[LogoMaterialController] Graphic material on '{name}' was replaced with " +
                             $"'{(_graphic.material != null ? _graphic.material.name : "null")}'; re-assigning the runtime burn material.", this);
        }
        _graphic.material = _runtimeMaterial;
    }

    private void OnDisable()
    {
        // Reset glitch cleanly if the object is disabled mid-burst.
        if (_runtimeMaterial != null)
            _runtimeMaterial.SetFloat(GlitchStrengthId, 0f);
        SetGlitchAudio(false);
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }

    /// <summary>
    /// Randomly triggers glitch bursts of varying length. Writes _GlitchStrength to the
    /// material and toggles the AudioSource from the same coroutine so both are always
    /// frame-perfectly in sync — no math replication required.
    /// </summary>
    private IEnumerator GlitchLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Random.Range(_glitchIntervalMin, _glitchIntervalMax));

            float duration = Random.Range(_glitchDurationMin, _glitchDurationMax);
            float rampTime = Mathf.Max(duration * 0.15f, 0.001f);

            SetGlitchAudio(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float inRamp  = Mathf.Clamp01(elapsed / rampTime);
                float outRamp = Mathf.Clamp01((duration - elapsed) / rampTime);
                _runtimeMaterial.SetFloat(GlitchStrengthId, Mathf.Min(inRamp, outRamp) * _glitchIntensity);
                yield return null;
            }

            _runtimeMaterial.SetFloat(GlitchStrengthId, 0f);
            SetGlitchAudio(false);
        }
    }

    private void SetGlitchAudio(bool active)
    {
        if (_glitchAudioSource == null || _glitchAudioSource.enabled == active)
            return;

        _glitchAudioSource.enabled = active;

        if (active && _glitchAudioSource.clip != null)
            _glitchAudioSource.time = Random.Range(0f, _glitchAudioSource.clip.length);
    }
}
