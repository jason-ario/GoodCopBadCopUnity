using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Persistent bottom-centre notification displayed when a suspect arrives at the booth
/// and the local player is away, or when a delivery is waiting for player attention (e.g.
/// a shipment waiting at the checkpoint gate). Reveals text character-by-character via
/// TMPTextReveal, then fades out after a configurable duration.
///
/// Supports an optional "looping" mode (see <see cref="Show(string, bool)"/>): instead of
/// fading out permanently, it fades out, waits <see cref="_repeatGapDuration"/>, then fades
/// back in with the same message — repeating indefinitely until <see cref="Hide"/> is called
/// externally (e.g. once the player actually interacts with whatever is being waited on).
///
/// Assign to the player UI canvas in the Inspector and wire up via UIController.
/// </summary>
public class BoothWaitingNotification : MonoBehaviour
{
    private const string DefaultMessage = "Someone is waiting at the booth";

    [SerializeField] private TMPTextReveal _textReveal;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _displayDuration = 4f;
    [SerializeField] private float _fadeDuration = 0.4f;
    [Tooltip("Only used in looping mode (see Show(string, loop: true)) — how long the notification stays hidden between fade-out and fade-back-in.")]
    [SerializeField] private float _repeatGapDuration = 1.5f;

    private Coroutine _hideCoroutine;
    private bool _looping;

    private void Awake()
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;

        gameObject.SetActive(false);
    }

    /// <summary>Shows the notification with the default message.</summary>
    public void Show()
    {
        Show(DefaultMessage);
    }

    /// <summary>
    /// Shows the notification with a custom message, revealed character by character.
    /// If <paramref name="loop"/> is true, the notification repeatedly fades out and back in
    /// (display -> fade out -> gap -> fade in -> repeat) instead of disappearing for good after
    /// <see cref="_displayDuration"/> — it keeps cycling until <see cref="Hide"/> is called
    /// externally.
    /// </summary>
    public void Show(string message, bool loop = false)
    {
        _looping = loop;
        gameObject.SetActive(true);

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.alpha = 1f;
        }

        if (_textReveal != null)
            _textReveal.RevealText(message);

        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _hideCoroutine = StartCoroutine(AutoHide(message));
    }

    /// <summary>
    /// Immediately hides the notification for good (e.g. when the player returns to the booth,
    /// or opens the gate a waiting shipment needed). Stops any in-progress looping cycle.
    /// </summary>
    public void Hide()
    {
        _looping = false;

        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(0f, _fadeDuration).OnComplete(() =>
            {
                if (_textReveal != null)
                    _textReveal.Clear();

                gameObject.SetActive(false);
            });
        }
        else
        {
            if (_textReveal != null)
                _textReveal.Clear();

            gameObject.SetActive(false);
        }
    }

    private IEnumerator AutoHide(string message)
    {
        yield return new WaitForSeconds(_displayDuration);

        if (!_looping)
        {
            Hide();
            yield break;
        }

        // Looping mode: fade out and wait, but don't deactivate — Hide() may be called by an
        // external caller at any point in this cycle (e.g. the gate opens mid-fade), so re-check
        // _looping after every wait before committing to fading back in.
        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(0f, _fadeDuration);
        }

        yield return new WaitForSeconds(_fadeDuration + _repeatGapDuration);

        if (!_looping)
            yield break;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(1f, _fadeDuration);
        }

        if (_textReveal != null)
            _textReveal.RevealText(message);

        yield return new WaitForSeconds(_fadeDuration);

        if (!_looping)
            yield break;

        _hideCoroutine = StartCoroutine(AutoHide(message));
    }

    private void OnDestroy()
    {
        if (_canvasGroup != null)
            DOTween.Kill(_canvasGroup);
    }
}
