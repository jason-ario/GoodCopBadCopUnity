using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Full-screen "Lost connection" notification shown when the local client unexpectedly
/// disconnects from the network session. Stays visible (does not auto-hide) since the
/// scene is about to be restarted by <see cref="ConnectionLossHandler"/>.
///
/// Assign to a persistent UI canvas and wire up via ConnectionLossHandler.
/// </summary>
public class ConnectionLostNotification : MonoBehaviour
{
    private const string DefaultMessage = "Lost connection";

    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _fadeDuration = 0.3f;

    private void Awake()
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;

        gameObject.SetActive(false);
    }

    /// <summary>Shows the notification with the default "Lost connection" message.</summary>
    public void Show()
    {
        Show(DefaultMessage);
    }

    /// <summary>Shows the notification with a custom message and fades it in.</summary>
    public void Show(string message)
    {
        gameObject.SetActive(true);

        if (_messageText != null)
            _messageText.text = message;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(1f, _fadeDuration);
        }
    }

    private void OnDestroy()
    {
        if (_canvasGroup != null)
            DOTween.Kill(_canvasGroup);
    }
}
