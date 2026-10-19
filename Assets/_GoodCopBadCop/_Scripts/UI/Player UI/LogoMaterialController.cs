using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the main menu logo's burn-in and random glitch bursts.
///
/// The burn is state-driven rather than tied to a single Start() coroutine: the Home Screen is
/// deactivated whenever the player switches menus (and briefly during menu setup on load), which
/// kills any running coroutine. Every OnEnable re-applies the current burn value and resumes an
/// unfinished burn, so the logo can never be left stuck at "fully burned" (invisible).
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
    [SerializeField] private AudioSource _glitchAudioSource;
    [SerializeField] private float _glitchIntervalMin = 3f;
    [SerializeField] private float _glitchIntervalMax = 8f;
    [SerializeField] private float _glitchDurationMin = 0.15f;
    [SerializeField] private float _glitchDurationMax = 1.5f;
    [SerializeField, Range(0f, 1f)] private float _glitchIntensity = 0.5f;

    private Material _runtimeMaterial;
    private float _burnElapsed;   // Time spent in the burn (excluding the delay).
    private float _delayElapsed;
    private bool _burnComplete;

    private void Awake()
    {
        if (_logoMaterial == null)
            return;

        _runtimeMaterial = new Material(_logoMaterial) { name = _logoMaterial.name + " (Runtime)" };
        _runtimeMaterial.SetFloat(BurnProgressId, 1f);
        _runtimeMaterial.SetFloat(GlitchStrengthId, 0f);

        if (TryGetComponent(out Graphic graphic))
            graphic.material = _runtimeMaterial;
    }

    private void OnEnable()
    {
        if (_runtimeMaterial == null)
            return;

        // Re-apply the current state immediately so there's never a stale frame, then resume
        // the burn if a menu switch interrupted it.
        ApplyBurn();
        if (!_burnComplete)
            StartCoroutine(AnimateBurnProgress());

        StartCoroutine(GlitchLoop());
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

    private float CurrentBurnValue()
    {
        if (_burnComplete)
            return _targetBurnProgress;

        float t = _duration > 0f ? Mathf.Clamp01(_burnElapsed / _duration) : 1f;
        return Mathf.Lerp(1f, _targetBurnProgress, t);
    }

    private void ApplyBurn() => _runtimeMaterial.SetFloat(BurnProgressId, CurrentBurnValue());

    /// <summary>
    /// Waits out the remaining delay, then animates _BurnProgress from 1 to the target value,
    /// resuming from wherever a previous run was interrupted. Unscaled time, so a leftover
    /// Time.timeScale of 0 can't stall it.
    /// </summary>
    private IEnumerator AnimateBurnProgress()
    {
        while (_delayElapsed < _delay)
        {
            _delayElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        while (_burnElapsed < _duration)
        {
            _burnElapsed += Time.unscaledDeltaTime;
            ApplyBurn();
            yield return null;
        }

        _burnComplete = true;
        ApplyBurn();

        if (_burnSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_burnSound);
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
