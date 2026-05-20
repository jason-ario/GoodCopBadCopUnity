using System.Collections;
using UnityEngine;

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

    private bool _animationStarted = false;

    private void Start()
    {
        StartCoroutine(AnimateBurnProgress());
        _animationStarted = true;
    }

    private void OnEnable()
    {
        if (_animationStarted)
            _logoMaterial.SetFloat(BurnProgressId, _targetBurnProgress);

        StartCoroutine(GlitchLoop());
    }

    private void OnDisable()
    {
        // Reset glitch cleanly if the object is disabled mid-burst.
        _logoMaterial.SetFloat(GlitchStrengthId, 0f);
        SetGlitchAudio(false);
    }

    /// <summary>
    /// Waits for the configured delay, then animates _BurnProgress from 1 to the target value.
    /// </summary>
    private IEnumerator AnimateBurnProgress()
    {
        _logoMaterial.SetFloat(BurnProgressId, 1f);

        yield return new WaitForSeconds(_delay);

        float elapsed = 0f;
        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _duration);
            _logoMaterial.SetFloat(BurnProgressId, Mathf.Lerp(1f, _targetBurnProgress, t));
            yield return null;
        }

        SFXController.Instance.Play(_burnSound);
        _logoMaterial.SetFloat(BurnProgressId, _targetBurnProgress);
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
            yield return new WaitForSeconds(Random.Range(_glitchIntervalMin, _glitchIntervalMax));

            float duration = Random.Range(_glitchDurationMin, _glitchDurationMax);
            float rampTime = Mathf.Max(duration * 0.15f, 0.001f);

            SetGlitchAudio(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float inRamp  = Mathf.Clamp01(elapsed / rampTime);
                float outRamp = Mathf.Clamp01((duration - elapsed) / rampTime);
                _logoMaterial.SetFloat(GlitchStrengthId, Mathf.Min(inRamp, outRamp) * _glitchIntensity);
                yield return null;
            }

            _logoMaterial.SetFloat(GlitchStrengthId, 0f);
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
