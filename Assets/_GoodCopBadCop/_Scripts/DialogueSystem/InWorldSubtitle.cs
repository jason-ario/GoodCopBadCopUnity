using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small world-space subtitle bubble that floats above a character's head. Used by
/// <see cref="ScriptedDialogueRunner"/> to show dialogue lines above the suspect's and the
/// active player's heads to a player who is not currently a participant in an ongoing
/// scripted dialogue (e.g. a bystander, or a player who backed out with the leave shortcut).
/// <para>
/// Reveals text with <see cref="TMPTextReveal"/> — the same typewriter effect used by the
/// main <see cref="Subtitles"/> dialogue box — and automatically hides itself
/// <see cref="autoHideSeconds"/> after the reveal finishes, unless replaced by a new line first.
/// </para>
/// </summary>
public class InWorldSubtitle : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private Image background;
    [SerializeField] private int maxCharactersPerLine = 28;

    [Tooltip("Seconds the bubble stays visible after the line finishes revealing, unless replaced sooner.")]
    [SerializeField] private float autoHideSeconds = 4f;

    [Tooltip("If true, the bubble rotates to always face the main camera.")]
    [SerializeField] private bool billboard = true;

    private Coroutine _autoHideRoutine;
    private Camera _mainCamera;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!billboard || !canvasGroup || canvasGroup.alpha <= 0f) return;

        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        transform.forward = transform.position - _mainCamera.transform.position;
    }

    /// <summary>Reveals <paramref name="text"/> in the bubble, restarting the auto-hide timer.</summary>
    public void ShowLine(string text, string speakerName = null, Color nameColor = default)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_autoHideRoutine != null)
        {
            StopCoroutine(_autoHideRoutine);
            _autoHideRoutine = null;
        }

        gameObject.SetActive(true);
        SetVisible(true);

        string wrapped = WrapText(text, maxCharactersPerLine);
        textReveal.RevealText(wrapped);

        _autoHideRoutine = StartCoroutine(AutoHideRoutine());
    }

    /// <summary>Immediately hides the bubble.</summary>
    public void Hide()
    {
        if (_autoHideRoutine != null)
        {
            StopCoroutine(_autoHideRoutine);
            _autoHideRoutine = null;
        }

        SetVisible(false);
        textReveal?.Clear();
    }

    private IEnumerator AutoHideRoutine()
    {
        // Wait for the typewriter to finish before starting the linger countdown.
        yield return new WaitUntil(() => textReveal == null || !textReveal.IsRevealing);
        yield return new WaitForSeconds(autoHideSeconds);

        _autoHideRoutine = null;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = visible ? 1f : 0f;

        if (!visible)
            gameObject.SetActive(false);
    }

    private static string WrapText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        var sb = new System.Text.StringBuilder();
        string[] words = text.Split(' ');
        int currentLineLength = 0;

        foreach (string word in words)
        {
            if (currentLineLength + word.Length + 1 > maxChars)
            {
                sb.Append('\n');
                currentLineLength = 0;
            }
            else if (currentLineLength > 0)
            {
                sb.Append(' ');
                currentLineLength++;
            }

            sb.Append(word);
            currentLineLength += word.Length;
        }

        return sb.ToString();
    }
}
