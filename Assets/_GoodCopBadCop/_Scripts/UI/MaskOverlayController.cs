using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the first-person screen overlay shown when the local player wears the radiation mask.
/// This is a placeholder implementation; the final visual should be replaced with a proper
/// shader-driven or post-process effect.
/// </summary>
public class MaskOverlayController : MonoBehaviour
{
    public static MaskOverlayController Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Duration in seconds for the overlay to fade in or out.")]
    [SerializeField] private float fadeDuration = 0.4f;

    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        // Start hidden
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Shows or hides the mask overlay with a smooth fade.
    /// Safe to call every frame — transitions are only restarted when the target state changes.
    /// </summary>
    public void SetVisible(bool visible)
    {
        float targetAlpha = visible ? 1f : 0f;

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeTo(targetAlpha));
    }

    private IEnumerator FadeTo(float targetAlpha)
    {
        if (canvasGroup == null) yield break;

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        _fadeCoroutine = null;
    }
}
