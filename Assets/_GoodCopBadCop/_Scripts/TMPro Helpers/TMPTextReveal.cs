using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPTextReveal : MonoBehaviour
{
    [SerializeField] private float characterDelay = 0.015f;

    private TextMeshProUGUI tmp;
    private Coroutine revealRoutine;

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
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

        tmp.text = string.Empty;
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