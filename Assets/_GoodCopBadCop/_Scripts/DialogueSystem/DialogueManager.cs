using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance;
    [SerializeField] private TextMeshProUGUI suspectDialogueText;
    [SerializeField] private float secondsPerCharacter = 0.06f;
    Coroutine audioDialogueCoroutine;
    
    private void Awake()
    {
        Instance = this;
        suspectDialogueText.text = "";
    }

    public void SayDialogue(string dialogue, AudioSource characterAudio = null, AudioClip[] audioClips = null)
    {
        StopDialogueAudio();
        suspectDialogueText.SetText(dialogue);
        
        if (characterAudio != null && audioClips != null && audioClips.Length > 0)
        {
            audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, characterAudio, audioClips));
        }
    }

    IEnumerator PlayDialogueAudio(string dialogue, AudioSource audioSource, AudioClip[] audioClips)
    {
        float duration = dialogue.Length * secondsPerCharacter;
        float timer = 0;

        while (timer < duration)
        {
            // Pick a random clip
            AudioClip clip = audioClips[UnityEngine.Random.Range(0, audioClips.Length)];
            audioSource.PlayOneShot(clip);

            // Wait for the clip to finish or the duration to end
            float waitTime = clip.length;
            yield return new WaitForSeconds(waitTime);
            
            timer += waitTime;
        }

        yield return new WaitForSeconds(1f); // Brief pause before clearing
        suspectDialogueText.text = "";
        audioDialogueCoroutine = null;
    }

    void StopDialogueAudio()
    {
        if (audioDialogueCoroutine != null)
        {
            StopCoroutine(audioDialogueCoroutine);
            audioDialogueCoroutine = null;
        }
    }
}
