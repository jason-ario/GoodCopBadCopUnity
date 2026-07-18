using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the TentacleBlackout fullscreen shader effect.
/// Assign a <c>Material</c> using the <c>GoodCopBadCop/TentacleBlackout</c> shader and
/// call <see cref="FadeToBlack"/> / <see cref="FadeFromBlack"/> from gameplay code.
/// </summary>
public class TentacleBlackoutController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Material using the GoodCopBadCop/TentacleBlackout shader. " +
             "Should be the same material instance assigned to TentacleBlackoutFeature.")]
    private Material _material;

    [SerializeField]
    [Tooltip("Easing applied when fading to black (0→1).")]
    private AnimationCurve _fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [SerializeField]
    [Tooltip("Easing applied when fading from black (1→0).")]
    private AnimationCurve _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ─── Shader property ID ───────────────────────────────────────────────────

    private static readonly int ProgressID = Shader.PropertyToID("_Progress");

    // ─── State ────────────────────────────────────────────────────────────────

    private Coroutine _activeCoroutine;

    /// <summary>True while a fade animation is running.</summary>
    public bool IsPlaying => _activeCoroutine != null;

    /// <summary>Current shader _Progress value (0 = clear, 1 = fully dark).</summary>
    public float CurrentProgress =>
        _material != null ? _material.GetFloat(ProgressID) : 0f;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Animates the effect from clear (0) to fully dark (1).
    /// Cancels any in-progress animation before starting.
    /// </summary>
    /// <param name="duration">Duration in seconds (uses unscaled time).</param>
    /// <param name="onComplete">Optional callback fired when the animation finishes.</param>
    public void FadeToBlack(float duration, Action onComplete = null)
        => BeginFade(0f, 1f, duration, _fadeOutCurve, onComplete);

    /// <summary>
    /// Animates the effect from fully dark (1) to clear (0).
    /// Cancels any in-progress animation before starting.
    /// </summary>
    /// <param name="duration">Duration in seconds (uses unscaled time).</param>
    /// <param name="onComplete">Optional callback fired when the animation finishes.</param>
    public void FadeFromBlack(float duration, Action onComplete = null)
        => BeginFade(1f, 0f, duration, _fadeInCurve, onComplete);

    /// <summary>
    /// Jumps to a specific progress value immediately, cancelling any running animation.
    /// </summary>
    public void SetProgress(float progress)
    {
        CancelActive();
        ApplyProgress(progress);
    }

    /// <summary>Stops any active animation and resets the effect to fully clear.</summary>
    public void Reset()
    {
        CancelActive();
        ApplyProgress(0f);
    }

    // ─── Internal ────────────────────────────────────────────────────────────

    private void BeginFade(float from, float to, float duration,
                           AnimationCurve curve, Action onComplete)
    {
        CancelActive();
        _activeCoroutine = StartCoroutine(FadeRoutine(from, to, duration, curve, onComplete));
    }

    private IEnumerator FadeRoutine(float from, float to, float duration,
                                    AnimationCurve curve, Action onComplete)
    {
        duration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t        = elapsed / duration;
            float progress = Mathf.LerpUnclamped(from, to, curve.Evaluate(t));
            ApplyProgress(progress);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        ApplyProgress(to);
        _activeCoroutine = null;
        onComplete?.Invoke();
    }

    private void ApplyProgress(float progress)
    {
        if (_material == null)
        {
            Debug.LogWarning("[TentacleBlackoutController] No material assigned.", this);
            return;
        }
        _material.SetFloat(ProgressID, Mathf.Clamp01(progress));
    }

    private void CancelActive()
    {
        if (_activeCoroutine == null) return;
        StopCoroutine(_activeCoroutine);
        _activeCoroutine = null;
    }

    private void OnDestroy()
    {
        CancelActive();
        // Ensure material is left in a clean state
        if (_material != null)
            _material.SetFloat(ProgressID, 0f);
    }
}
