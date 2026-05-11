using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Persistent bottom-centre notification displayed when a suspect arrives at the booth
/// and the local player is away. Shows white text that fades out after a configurable duration.
/// Assign to the player UI canvas in the Inspector and wire up via UIController.
/// </summary>
public class BoothWaitingNotification : MonoBehaviour
{
    private const string DefaultMessage = "Someone is waiting at the booth";

    [SerializeField] private TextMeshProUGUI _messageText;
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

    /// <summary>Shows the notification with a custom message.</summary>
    public void Show(string message)
    {
        if (_messageText != null)
            _messageText.text = message;

        gameObject.SetActive(true);

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(1f, _fadeDuration);
        }

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
            _canvasGroup.DOFade(0f, _fadeDuration).OnComplete(() => gameObject.SetActive(false));
        }
        else
        {
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
