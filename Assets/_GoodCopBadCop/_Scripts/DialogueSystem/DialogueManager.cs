
using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class DialogueManager : NetworkBehaviour
{
    public static DialogueManager Instance;
    [SerializeField] private float secondsPerCharacter = 0.06f;
    Coroutine audioDialogueCoroutine;
    [SerializeField] private float minDelayBetweenClips = 0.03f;
    [SerializeField] private float maxDelayBetweenClips = 0.1f;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    [SerializeField] private Subtitles NPCSubtitlesPrefab;
    [SerializeField] private Subtitles playerSubtitlesPrefab;
    [SerializeField] RectTransform subtitlesContainer;
    
    private void Awake()
    {
        Instance = this;
    }

    public void SayDialogue(string dialogue, AudioSource characterAudio = null, AudioClip[] audioClips = null)
    {
        if (IsServer)
        {
            // Server broadcasts to all clients
            SayDialogueClientRpc(dialogue);
        }
        else
        {
            // Client requests server to broadcast
            SayDialogueServerRpc(dialogue);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayDialogueServerRpc(string dialogue)
    {
        SayDialogueClientRpc(dialogue);
    }

    [ClientRpc]
    private void SayDialogueClientRpc(string dialogue)
    {
        StopDialogueAudio();
        GameObject subtitle = SpawnSubtitles(dialogue, SuspectController.Instance.suspectCharacter.suspectName, SuspectController.Instance.suspectCharacter.suspectNameColor, false);
        
        // Play audio locally on each client
        if (SuspectController.Instance.suspectCharacter.audioSource != null && 
            SuspectController.Instance.suspectCharacter.voiceAudioClips != null && 
            SuspectController.Instance.suspectCharacter.voiceAudioClips.Length > 0)
        {
            audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(
                dialogue, 
                SuspectController.Instance.suspectCharacter.audioSource, 
                SuspectController.Instance.suspectCharacter.voiceAudioClips, 
                subtitle));
        }
    }

    IEnumerator PlayDialogueAudio(string dialogue, AudioSource audioSource, AudioClip[] audioClips, GameObject subtitle)
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

    public GameObject SpawnSubtitles(string text, string characterName = null, Color nameColor = default, bool isPlayer = false)
    {
        Subtitles subtitles;
        
        if (isPlayer)
        {
            subtitles = Instantiate(playerSubtitlesPrefab, subtitlesContainer);
        }
        else
        {
            subtitles = Instantiate(NPCSubtitlesPrefab, subtitlesContainer);
        }
        
        subtitles.SetText(text, characterName, nameColor);
        subtitles.transform.SetAsLastSibling();
        StartCoroutine(DestroySubtitles(subtitles.gameObject));

        return subtitles.gameObject;
    }

    IEnumerator DestroySubtitles(GameObject subtitle)
    {
        yield return new WaitForSeconds(5);
        Destroy(subtitle);
    }

    
}