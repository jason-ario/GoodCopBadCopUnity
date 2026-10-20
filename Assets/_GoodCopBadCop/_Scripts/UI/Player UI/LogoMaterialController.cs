using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the main menu logo's burn-in and random glitch bursts.
///
/// The burn restarts from fully burned (invisible) every time the logo is enabled, i.e. every
/// time the Home Screen opens. The Home Screen is deactivated whenever the player switches menus
/// (and briefly during menu setup on load), which kills any running coroutine; because each
/// OnEnable starts a fresh run, an interrupted burn can never leave the logo stuck invisible.
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

    private Material _runtimeMaterial;

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

        // Every time the home screen opens, restart the burn from fully burned (invisible).
        // Each activation owns a fresh run, so a menu switch that kills the coroutine mid-burn
        // can't leave the logo stuck invisible.
        StartCoroutine(AnimateBurnProgress());
        if (_enableGlitch)
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

    /// <summary>
    /// Sets _BurnProgress to 1 (invisible), waits for the configured delay, then animates it to
    /// the target value. Unscaled time, so a leftover Time.timeScale of 0 can't stall it.
    /// </summary>
    private IEnumerator AnimateBurnProgress()
    {
        _runtimeMaterial.SetFloat(BurnProgressId, 1f);

        float delayElapsed = 0f;
        while (delayElapsed < _delay)
        {
            delayElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        float elapsed = 0f;
        while (elapsed < _duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _duration);
            _runtimeMaterial.SetFloat(BurnProgressId, Mathf.Lerp(1f, _targetBurnProgress, t));
            yield return null;
        }

        _runtimeMaterial.SetFloat(BurnProgressId, _targetBurnProgress);

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
