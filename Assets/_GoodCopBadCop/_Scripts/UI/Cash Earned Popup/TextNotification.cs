using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Plain text entry (no panel) spawned into the top-left cash notification stack by
/// <see cref="CashNotificationPopupManager.SpawnTextNotification"/>. Used for low-priority
/// messages such as "X has joined the game." Fades in, then fades out before the manager
/// destroys it.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TextNotification : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.4f;

    private CanvasGroup _canvasGroup;

    public void Initialize(string message, float lifetime)
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        if (messageText != null)
            messageText.text = message;

        StartCoroutine(FadeRoutine(lifetime));
    }

    private IEnumerator FadeRoutine(float lifetime)
    {
        yield return Fade(0f, 1f, fadeInDuration);

        float hold = lifetime - fadeInDuration - fadeOutDuration;
        if (hold > 0f)
            yield return new WaitForSeconds(hold);

        yield return Fade(1f, 0f, fadeOutDuration);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            _canvasGroup.alpha = to;
            yield break;
        }

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            _canvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        _canvasGroup.alpha = to;
    }
}
