using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using Random = System.Random;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPTextReveal : MonoBehaviour
{
    [SerializeField] private float characterDelay = 0.015f;

    private TextMeshProUGUI tmp;
    private Coroutine revealRoutine;
    private TMPWidthFitter widthFitter;
    [SerializeField] bool revealOnEnable = false;
    [SerializeField] float revealOnEnableDelay = 0.5f;
    [SerializeField] AudioClip[] revealSounds;
    [SerializeField] private float minSoundInterval = 0.1f;

    private string _fullText;
    private float _lastSoundPlayTime = float.NegativeInfinity;

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
        widthFitter = GetComponent<TMPWidthFitter>();
    }

    private void OnEnable()
    {
        if (revealOnEnable)
        {
            StartCoroutine(RevealAfterEnableCoroutine());
        }
        else
        {
            tmp.text = " ";
        }
    }

    IEnumerator RevealAfterEnableCoroutine()
    {
        string text = tmp.text;
        Clear();
        yield return new WaitForSeconds(revealOnEnableDelay);
        RevealText(text);
    }

    public Coroutine RevealText(string text)
    {
        _fullText = text;
        StopCurrentRoutine();
        gameObject.SetActive(true);
        revealRoutine = StartCoroutine(RevealRoutine(text));
        return revealRoutine;
    }

    public void SetTextInstant(string text)
    {
        StopCurrentRoutine();

        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        tmp.text = text;
        tmp.maxVisibleCharacters = 9999;
        tmp.ForceMeshUpdate();
    }

    public void Clear()
    {
        StopCurrentRoutine();

        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        tmp.text = " ";
        tmp.maxVisibleCharacters = 0;
        tmp.canvasRenderer.Clear();
    }

    private IEnumerator RevealRoutine(string fullText)
    {
        _lastSoundPlayTime = float.NegativeInfinity;

        // Set full text with all characters hidden to populate textInfo without a visual flash.
        tmp.maxVisibleCharacters = 0;
        tmp.text = fullText;
        tmp.ForceMeshUpdate();

        // Cache the source-string index of every visible character before we start
        // overwriting tmp.text, since textInfo is rebuilt on each text assignment.
        int totalChars = tmp.textInfo.characterCount;
        int[] charStringIndices = new int[totalChars];
        for (int i = 0; i < totalChars; i++)
            charStringIndices[i] = tmp.textInfo.characterInfo[i].index;

        // Reveal by progressively growing tmp.text to a longer substring each step.
        // This makes TMP measure only the currently visible portion, so TMPWidthFitter
        // and any ContentSizeFitter in the hierarchy reflect the actual visible width.
        tmp.maxVisibleCharacters = 99999;

        for (int i = 0; i < totalChars; i++)
        {
            tmp.text = fullText.Substring(0, charStringIndices[i] + 1);

            if (revealSounds.Length != 0 && Time.time - _lastSoundPlayTime >= minSoundInterval)
            {
                _lastSoundPlayTime = Time.time;
                SFXController.Instance.Play(revealSounds[UnityEngine.Random.Range(0, revealSounds.Length)]);
            }

            yield return new WaitForSeconds(characterDelay);
        }

        // Restore the complete string so any closing rich-text tags are included.
        tmp.text = fullText;
        revealRoutine = null;
    }

    /// <summary>Returns true while a reveal coroutine is actively running.</summary>
    public bool IsRevealing => revealRoutine != null;

    /// <summary>
    /// Reveals <paramref name="text"/> and yields until the typewriter finishes, or until
    /// <paramref name="timeout"/> seconds have elapsed, whichever comes first. On timeout (or if
    /// this object is not active) the text is snapped to its final state.
    ///
    /// Prefer this over <c>yield return RevealText(...)</c> whenever the caller lives on a
    /// *different* GameObject. Yielding on the returned <see cref="Coroutine"/> handle couples the
    /// caller's lifetime to this object's: Unity destroys a coroutine when its owning GameObject is
    /// deactivated, and a caller waiting on that handle is never resumed — it hangs forever with no
    /// error. That is how the end-of-shift report used to strand players, since the reveal chain
    /// gated the Continue button and any child deactivation mid-reveal killed it silently.
    /// This method is driven by the *caller's* coroutine and only ever polls state, so a
    /// deactivated or stalled reveal degrades to an instant text set instead of a deadlock.
    /// </summary>
    public IEnumerator RevealTextBounded(string text, float timeout)
    {
        // StartCoroutine fails on an inactive GameObject; snap instead of pretending to animate.
        if (!gameObject.activeInHierarchy)
        {
            SetTextInstant(text);
            yield break;
        }

        RevealText(text);

        float elapsed = 0f;
        while (IsRevealing)
        {
            if (elapsed >= timeout || !gameObject.activeInHierarchy)
            {
                CompleteReveal();
                SetTextInstant(text);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Immediately completes the typewriter animation, showing the full text at once.
    /// Does nothing if no reveal is in progress.
    /// </summary>
    public void CompleteReveal()
    {
        if (!IsRevealing) return;
        StopCurrentRoutine();
        SetTextInstant(_fullText);
    }

    private void StopCurrentRoutine()
    {
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }
    }
}