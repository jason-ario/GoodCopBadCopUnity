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
    private SuspectCharacter characterTalking;

    private void Awake()
    {
        Instance = this;
    }

    public void SayDialogue(SuspectCharacter character, string dialogue, bool clearHistory = false)
    {
        // Resolve the NetworkObjectId of the character whose AudioSource was passed in
        ulong networkObjectId = ulong.MaxValue;
        if (character != null)
        {
            var netObj = character.GetComponent<NetworkObject>();
            if (netObj != null) networkObjectId = netObj.NetworkObjectId;
        }

        if (IsServer)
        {
            SayDialogueClientRpc(dialogue, networkObjectId, clearHistory);
        }
        else
        {
            SayDialogueServerRpc(dialogue, networkObjectId, clearHistory);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayDialogueServerRpc(string dialogue, ulong networkObjectId, bool clearHistory = false)
    {
        SayDialogueClientRpc(dialogue, networkObjectId, clearHistory);
    }

    [ClientRpc]
    private void SayDialogueClientRpc(string dialogue, ulong networkObjectId, bool clearHistory = false)
    {
        StopDialogueAudio();

        // Resolve the character from its NetworkObjectId, falling back to the current suspect
        SuspectCharacter character = null;
        if (networkObjectId != ulong.MaxValue &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj))
        {
            character = netObj.GetComponent<SuspectCharacter>();
        }

        // Fallback to the current suspect if the character couldn't be resolved
        if (character == null)
            character = SuspectController.Instance.suspectCharacter;

        GameObject subtitle = SpawnSubtitles(dialogue, character.suspectName, character.suspectNameColor, false, clearHistory);

        if (character.audioSource != null &&
            character.voiceAudioClips != null &&
            character.voiceAudioClips.Length > 0)
        {
            audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(
                dialogue,
                character.audioSource,
                character.voiceAudioClips,
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

            float clipDuration = clip.length;
            yield return new WaitForSeconds(clipDuration);

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

    public GameObject SpawnSubtitles(string text, string characterName = null, Color nameColor = default, bool isPlayer = false, bool clearHistory = false)
    {
        Subtitles subtitles;

        if (clearHistory)
        {
            DestroyPreviousSubtitles();
        }
        
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

    void DestroyPreviousSubtitles()
    {
        foreach (Transform child in subtitlesContainer)
        {
            Destroy(child.gameObject);
        }
    }

    IEnumerator DestroySubtitles(GameObject subtitle)
    {
        yield return new WaitForSeconds(5);
        Destroy(subtitle);
    }
}