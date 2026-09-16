using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a single shop alert message (e.g. purchase confirmation or error).
/// Attach this to the root of the Shop Notification prefab.
/// </summary>
public class ShopNotification : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _messageText;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _fadeInDuration = 0.25f;
    [SerializeField] private float _fadeOutDuration = 0.35f;

    /// <summary>How long <see cref="FadeOut"/> takes, so callers can time destruction to match.</summary>
    public float FadeOutDuration => _fadeOutDuration;

    /// <summary>Populates the notification with the given message and fades it in.</summary>
    public void Initialize(string message)
    {
        _messageText.text = message;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(1f, _fadeInDuration);
        }
    }

    /// <summary>Starts fading the notification out. Callers should wait <see cref="FadeOutDuration"/> before destroying it.</summary>
    public void FadeOut()
    {
        if (_canvasGroup == null)
            return;

        DOTween.Kill(_canvasGroup);
        _canvasGroup.DOFade(0f, _fadeOutDuration);
    }

    private void OnDestroy()
    {
        DOTween.Kill(transform);

        if (_canvasGroup != null)
            DOTween.Kill(_canvasGroup);
    }
}
