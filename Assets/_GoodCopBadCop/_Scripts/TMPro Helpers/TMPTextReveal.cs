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

            if (revealSounds.Length != 0)
                SFXController.Instance.Play(revealSounds[UnityEngine.Random.Range(0, revealSounds.Length)]);

            yield return new WaitForSeconds(characterDelay);
        }

        // Restore the complete string so any closing rich-text tags are included.
        tmp.text = fullText;
        revealRoutine = null;
    }

    /// <summary>Returns true while a reveal coroutine is actively running.</summary>
    public bool IsRevealing => revealRoutine != null;

    private void StopCurrentRoutine()
    {
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }
    }
}