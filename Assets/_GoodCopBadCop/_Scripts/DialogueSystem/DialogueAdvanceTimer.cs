using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Countdown timer shown to all clients after the first player advances a scripted dialogue
/// line or submits a choice. The fill depletes over the configured duration; the sequence
/// auto-continues on the server when the timer expires.
///
/// Attach to a HUD GameObject that has a child <see cref="Image"/> configured as a radial fill.
/// Wire <see cref="_root"/> to the container and <see cref="_fillImage"/> to the radial image.
/// </summary>
public class DialogueAdvanceTimer : MonoBehaviour
{
    public static DialogueAdvanceTimer Instance { get; private set; }

    [Tooltip("Root GameObject to show/hide. Assign the parent container of the timer icon.")]
    [SerializeField] private GameObject _root;

    [Tooltip("Radial-fill Image whose fillAmount is animated from 1 to 0 over the duration.")]
    [SerializeField] private Image _fillImage;

    private Coroutine _fillCoroutine;

    private void Awake()
    {
        Instance = this;
        _root.SetActive(false);
    }

    /// <summary>Shows the timer and begins depleting it over <paramref name="duration"/> seconds.</summary>
    public void Show(float duration)
    {
        if (_fillCoroutine != null)
            StopCoroutine(_fillCoroutine);

        _root.SetActive(true);
        _fillCoroutine = StartCoroutine(AnimateFill(duration));
    }

    /// <summary>Hides the timer immediately, cancelling any in-progress animation.</summary>
    public void Hide()
    {
        if (_fillCoroutine != null)
        {
            StopCoroutine(_fillCoroutine);
            _fillCoroutine = null;
        }

        _root.SetActive(false);

        if (_fillImage != null)
            _fillImage.fillAmount = 1f;
    }

    private IEnumerator AnimateFill(float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (_fillImage != null)
                _fillImage.fillAmount = 1f - (elapsed / duration);
            yield return null;
        }

        if (_fillImage != null)
            _fillImage.fillAmount = 0f;

        _fillCoroutine = null;
    }
}
