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
    [SerializeField] bool revealOnEnable = false;
    [SerializeField] float revealOnEnableDelay = 0.5f;
    [SerializeField] AudioClip[] revealSounds;

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
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
        tmp.text = fullText;
        tmp.maxVisibleCharacters = 0;
        tmp.ForceMeshUpdate();

        for (int i = 0; i <= fullText.Length; i++)
        {
            tmp.maxVisibleCharacters = i;
            if (revealSounds.Length != 0)
            {
                SFXController.Instance.Play(revealSounds[UnityEngine.Random.Range(0, revealSounds.Length)]);
            }
            yield return new WaitForSeconds(characterDelay);
        }

        revealRoutine = null;
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