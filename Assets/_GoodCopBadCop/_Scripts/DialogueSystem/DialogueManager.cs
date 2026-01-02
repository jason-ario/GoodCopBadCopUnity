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
    [SerializeField] private float minDelayBetweenClips = 0.03f;
    [SerializeField] private float maxDelayBetweenClips = 0.1f;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    
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
        int lastClipIndex = -1;

        while (timer < duration)
        {
            int randomIndex;
            
            if (audioClips.Length > 1)
            {
                do
                {
                    randomIndex = UnityEngine.Random.Range(0, audioClips.Length);
                } while (randomIndex == lastClipIndex);
            }
            else
            {
                randomIndex = 0;
            }

            lastClipIndex = randomIndex;
            AudioClip clip = audioClips[randomIndex];
            audioSource.PlayOneShot(clip);

            // Wait for the clip to finish
            float clipDuration = clip.length;
            yield return new WaitForSeconds(clipDuration);
            
            // Add a random delay between clips for a more natural rhythm
            float extraDelay = UnityEngine.Random.Range(minDelayBetweenClips, maxDelayBetweenClips);
            yield return new WaitForSeconds(extraDelay);
            
            timer += clipDuration + extraDelay;
        }

        yield return new WaitForSeconds(1f);
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

    public void InitiateChoices()
    {
        dialogueChoiceSystem.StartDialogueChoices();
    }
}
