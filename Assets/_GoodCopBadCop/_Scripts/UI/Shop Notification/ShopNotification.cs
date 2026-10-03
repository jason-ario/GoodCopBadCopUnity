using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a single shop alert message (e.g. purchase confirmation or error) as plain text
/// that pops in with a punch-scale and fades out.
/// Attach this to the root of the Shop Notification prefab.
/// </summary>
public class ShopNotification : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _messageText;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _fadeInDuration = 0.15f;
    [SerializeField] private float _fadeOutDuration = 0.35f;

    [Header("Pop In")]
    [SerializeField] private float _punchStrength = 0.25f;
    [SerializeField] private float _punchDuration = 0.35f;
    [SerializeField] private int _punchVibrato = 6;
    [SerializeField, Range(0f, 1f)] private float _punchElasticity = 0.5f;

    /// <summary>How long <see cref="FadeOut"/> takes, so callers can time destruction to match.</summary>
    public float FadeOutDuration => _fadeOutDuration;

    /// <summary>Populates the notification with the given message, fades it in and plays the pop animation.</summary>
    public void Initialize(string message)
    {
        _messageText.text = message;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(1f, _fadeInDuration);
        }

        DOTween.Kill(transform);
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * _punchStrength, _punchDuration, _punchVibrato, _punchElasticity);
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
