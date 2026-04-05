using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

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

    private Subtitles _waitingSubtitle;

    private void Awake()
    {
        Instance = this;
    }

    public void SayDialogue(SuspectCharacter character, string dialogue, bool clearHistory = false,
        bool waitForInput = false, Action onComplete = null)
    {
        ulong networkObjectId = ulong.MaxValue;
        if (character != null)
        {
            var netObj = character.GetComponent<NetworkObject>();
            if (netObj != null) networkObjectId = netObj.NetworkObjectId;
        }

        if (IsServer)
        {
            SayDialogueClientRpc(dialogue, networkObjectId, clearHistory, waitForInput);
        }
        else
        {
            SayDialogueServerRpc(dialogue, networkObjectId, clearHistory, waitForInput);
        }

        if (waitForInput)
        {
            StartCoroutine(WaitForInputRoutine(onComplete));
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayDialogueServerRpc(string dialogue, ulong networkObjectId, bool clearHistory = false, bool waitForInput = false)
    {
        SayDialogueClientRpc(dialogue, networkObjectId, clearHistory, waitForInput);
    }

    [ClientRpc]
    private void SayDialogueClientRpc(string dialogue, ulong networkObjectId, bool clearHistory = false, bool waitForInput = false)
    {
        StopDialogueAudio();

        SuspectCharacter character = null;
        if (networkObjectId != ulong.MaxValue &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj))
        {
            character = netObj.GetComponent<SuspectCharacter>();
        }

        if (character == null && SuspectController.Instance != null)
            character = SuspectController.Instance.CurrentSuspect;

        if (character == null)
        {
            Debug.LogWarning("DialogueManager: No character resolved for dialogue: " + dialogue);
            return;
        }

        GameObject subtitle = SpawnSubtitles(dialogue, character.Data.FirstName, Color.white, false, clearHistory, waitForInput);

        PlayDialogueAudio(dialogue, character.Data.voiceAudioClips, character.audioSource);
        /*
        if (character.audioSource != null &&
            character.Data.voiceAudioClips != null &&
            character.Data.voiceAudioClips.Length > 0)
        {
            audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(
                dialogue,
                character.audioSource,
                character.Data.voiceAudioClips,
                subtitle));
        }*/
    }

    public void PlayDialogueAudio(string dialogue, AudioClip[] audioClips, AudioSource audioSource, UnityAction onComplete = null)
    {
        StopDialogueAudio();
        if (audioClips.Length == 0) return;
        if (audioSource == null) return;
        
        audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, onComplete));
    }

    IEnumerator PlayDialogueAudio(string dialogue, AudioSource audioSource, AudioClip[] audioClips, UnityAction onComplete = null)
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

        onComplete?.Invoke();
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

    public GameObject SpawnSubtitles(string text, string characterName = null, Color nameColor = default,
        bool isPlayer = false, bool clearHistory = false, bool waitForInput = false)
    {
        if (clearHistory)
            DestroyPreviousSubtitles();

        Subtitles subtitles = Instantiate(isPlayer ? playerSubtitlesPrefab : NPCSubtitlesPrefab, subtitlesContainer);

        subtitles.SetText(text, characterName, nameColor);
        subtitles.transform.SetAsLastSibling();

        if (waitForInput)
        {
            _waitingSubtitle = subtitles;
            subtitles.ShowContinuePrompt(true);
        }
        else
        {
            _waitingSubtitle = null;
            StartCoroutine(DestroySubtitles(subtitles.gameObject));
        }

        return subtitles.gameObject;
    }

    private bool _dialogueInputReceived = false;

    /// <summary>
    /// Called by any client pressing Space — notifies the server to advance for everyone.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void AdvanceDialogueServerRpc()
    {
        AdvanceDialogueClientRpc();
    }

    [ClientRpc]
    private void AdvanceDialogueClientRpc()
    {
        _dialogueInputReceived = true;
    }

    public IEnumerator WaitForInputRoutine(Action onComplete = null)
    {
        _dialogueInputReceived = false;
        yield return null;

        while (!_dialogueInputReceived)
        {
            if (Input.GetKeyDown(KeyCode.E) && _waitingSubtitle != null && _waitingSubtitle.IsPromptActive)
            {
                AdvanceDialogueServerRpc();
            }
            yield return null;
        }

        ClearHistory();
        _waitingSubtitle = null;
        onComplete?.Invoke();
    }

    public void ClearHistory()
    {
        DestroyPreviousSubtitles();
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


public class DialogueSequence
{
    private struct Entry
    {
        public SuspectCharacter character;
        public string text;
        public bool clearHistory;
        public bool waitForInput;
        public Action onShow;       // fires immediately when this line is shown
    }

    private readonly List<Entry> _entries = new();
    private Action _onComplete;

    public DialogueSequence Say(SuspectCharacter character, string text,
        bool clearHistory = false, bool waitForInput = false, Action onShow = null)
    {
        _entries.Add(new Entry
        {
            character = character,
            text = text,
            clearHistory = clearHistory,
            waitForInput = waitForInput,
            onShow = onShow
        });
        return this; // fluent chaining
    }

    public DialogueSequence OnComplete(Action onComplete)
    {
        _onComplete = onComplete;
        return this;
    }

    public IEnumerator Play()
    {
        foreach (var entry in _entries)
        {
            entry.onShow?.Invoke();

            DialogueManager.Instance.SayDialogue(entry.character, entry.text,
                clearHistory: entry.clearHistory,
                waitForInput: entry.waitForInput);

            if (entry.waitForInput)
            {
                yield return DialogueManager.Instance.WaitForInputRoutine();
            }
        }

        _onComplete?.Invoke();
    }
}