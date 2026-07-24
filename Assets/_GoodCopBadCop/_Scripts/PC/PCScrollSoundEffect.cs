using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a looping "scrolling" sound effect for as long as a Scrollbar's value keeps changing
/// (i.e. scroll velocity is non-zero), fading it out shortly after the value settles. Reacts to
/// any source that moves the scrollbar - handle drag (ClickablePCScrollbar), track jump
/// (ClickablePCScrollbarTrack), content drag (ClickablePCScrollArea), or the mouse wheel -
/// since all of them go through Scrollbar.value and therefore raise onValueChanged.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PCScrollSoundEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip scrollLoopSfx;

    [Header("Settings")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private float fadeInDuration = 0.05f;
    [SerializeField] private float fadeOutDuration = 0.15f;
    [Tooltip("Time in seconds without a value change before scrolling is considered stopped.")]
    [SerializeField] private float stopDelay = 0.08f;

    private float _lastScrollTime = float.NegativeInfinity;
    private bool _isScrolling;
    private Coroutine _fadeCoroutine;

    private void OnEnable()
    {
        if (scrollbar != null)
            scrollbar.onValueChanged.AddListener(HandleValueChanged);
    }

    private void OnDisable()
    {
        if (scrollbar != null)
            scrollbar.onValueChanged.RemoveListener(HandleValueChanged);

        StopScrollingImmediate();
    }

    private void Update()
    {
        if (_isScrolling && Time.time - _lastScrollTime >= stopDelay)
            StopScrolling();
    }

    private void HandleValueChanged(float _)
    {
        _lastScrollTime = Time.time;

        if (!_isScrolling)
            StartScrolling();
    }

    private void StartScrolling()
    {
        if (audioSource == null || scrollLoopSfx == null)
            return;

        _isScrolling = true;

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        audioSource.clip = scrollLoopSfx;
        audioSource.loop = true;
        if (!audioSource.isPlaying)
            audioSource.Play();

        _fadeCoroutine = StartCoroutine(FadeVolume(volume, fadeInDuration, stopOnComplete: false));
    }

    private void StopScrolling()
    {
        _isScrolling = false;

        if (audioSource == null)
            return;

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeVolume(0f, fadeOutDuration, stopOnComplete: true));
    }

    private void StopScrollingImmediate()
    {
        _isScrolling = false;

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (audioSource != null)
            audioSource.Stop();
    }

    private IEnumerator FadeVolume(float targetVolume, float duration, bool stopOnComplete)
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        if (duration > 0f)
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                yield return null;
            }
        }

        audioSource.volume = targetVolume;

        if (stopOnComplete)
            audioSource.Stop();

        _fadeCoroutine = null;
    }
}
