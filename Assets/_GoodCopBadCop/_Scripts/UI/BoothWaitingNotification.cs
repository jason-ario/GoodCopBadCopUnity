using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Persistent bottom-centre notification displayed when a suspect arrives at the booth
/// and the local player is away. Reveals text character-by-character via TMPTextReveal,
/// then fades out after a configurable duration.
/// Assign to the player UI canvas in the Inspector and wire up via UIController.
/// </summary>
public class BoothWaitingNotification : MonoBehaviour
{
    private const string DefaultMessage = "Someone is waiting at the booth";

    [SerializeField] private TMPTextReveal _textReveal;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _displayDuration = 4f;
    [SerializeField] private float _fadeDuration = 0.4f;

    private Coroutine _hideCoroutine;

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

    /// <summary>Shows the notification with a custom message, revealed character by character.</summary>
    public void Show(string message)
    {
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

        _hideCoroutine = StartCoroutine(AutoHide());
    }

    /// <summary>Immediately hides the notification (e.g. when the player returns to the booth).</summary>
    public void Hide()
    {
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

    private IEnumerator AutoHide()
    {
        yield return new WaitForSeconds(_displayDuration);
        Hide();
    }

    private void OnDestroy()
    {
        if (_canvasGroup != null)
            DOTween.Kill(_canvasGroup);
    }
}
