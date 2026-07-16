using DG.Tweening;
using GoodCopBadCop.EnvironmentSystem;
using UnityEngine;

/// <summary>
/// Crossfades between the wasteland and underwater ambience AudioSources when
/// the local player enters or exits an <see cref="UnderwaterZone"/>.
/// Place this anywhere in the scene and wire both AudioSource references in the Inspector.
/// </summary>
public sealed class UnderwaterAmbienceAdapter : MonoBehaviour
{
    [SerializeField] private AudioSource wastelandSource;
    [SerializeField] private AudioSource underwaterSource;
    [SerializeField] private float fadeDuration = 2f;

    private float _wastelandOriginalVolume;
    private float _underwaterOriginalVolume;
    // Counter tracks how many zones the player is currently inside.
    // Crossfade fires only on the 0→1 and 1→0 transitions so multiple
    // overlapping zones don't trigger redundant swaps.
    private int _activeZoneCount;

    private void Awake()
    {
        _wastelandOriginalVolume  = wastelandSource  != null ? wastelandSource.volume  : 1f;
        _underwaterOriginalVolume = underwaterSource != null ? underwaterSource.volume : 1f;
    }

    private void OnEnable()
    {
        UnderwaterZone.OnUnderwaterStateChanged += HandleUnderwaterStateChanged;
    }

    private void OnDisable()
    {
        UnderwaterZone.OnUnderwaterStateChanged -= HandleUnderwaterStateChanged;
    }

    private void HandleUnderwaterStateChanged(bool isUnderwater)
    {
        int previous = _activeZoneCount;
        _activeZoneCount = Mathf.Max(0, _activeZoneCount + (isUnderwater ? 1 : -1));

        if (previous == 0 && _activeZoneCount == 1)
            CrossfadeToUnderwater();
        else if (previous >= 1 && _activeZoneCount == 0)
            CrossfadeToWasteland();
    }

    private void CrossfadeToUnderwater()
    {
        wastelandSource?.DOKill();
        underwaterSource?.DOKill();

        // Bring wasteland down
        wastelandSource?.DOFade(0f, fadeDuration);

        // Enable the underwater GO, start playback silenced, then fade in
        if (underwaterSource == null) return;

        underwaterSource.gameObject.SetActive(true);
        if (!underwaterSource.isPlaying)
        {
            underwaterSource.volume = 0f;
            underwaterSource.Play();
        }
        underwaterSource.DOFade(_underwaterOriginalVolume, fadeDuration);
    }

    private void CrossfadeToWasteland()
    {
        wastelandSource?.DOKill();
        underwaterSource?.DOKill();

        // Bring wasteland back up (resume if it stopped)
        if (wastelandSource != null)
        {
            if (!wastelandSource.isPlaying)
                wastelandSource.Play();
            wastelandSource.DOFade(_wastelandOriginalVolume, fadeDuration);
        }

        // Fade out underwater, then stop and deactivate its GameObject
        if (underwaterSource == null) return;

        underwaterSource.DOFade(0f, fadeDuration)
            .OnComplete(() =>
            {
                underwaterSource.Stop();
                underwaterSource.gameObject.SetActive(false);
            });
    }
}
