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
    [SerializeField] private float subtitleLingerSeconds = 1f;
    Coroutine audioDialogueCoroutine;
    private Coroutine _subtitleDestroyCoroutine;

    /// <summary>
    /// Separate coroutine slot for megaphone barks so that character speech
    /// (SayDialogueClientRpc → StopDialogueAudio) cannot cancel an in-flight
    /// megaphone bark and leave MegaphoneDialogueManager._isSpeaking stuck true.
    /// </summary>
    private Coroutine _megaphoneAudioCoroutine;

    [SerializeField] private float minDelayBetweenClips = 0.03f;
    [SerializeField] private float maxDelayBetweenClips = 0.1f;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    [SerializeField] private Subtitles NPCSubtitlesPrefab;
    [SerializeField] private Subtitles playerSubtitlesPrefab;
    [SerializeField] RectTransform subtitlesContainer;

    private Subtitles _waitingSubtitle;

    private void Awake()
    {
        Instance = this;
    }
    

    public void SayDialogue(SpeakingInteraction speaking, string dialogue, bool clearHistory = false,
        bool waitForInput = false, Action onComplete = null)
    {
        ulong networkObjectId = ulong.MaxValue;
        if (speaking != null)
        {
            var netObj = speaking.GetComponent<NetworkObject>();
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

    /// <summary>
    /// Convenience overload that resolves the SpeakingInteraction from a SuspectCharacter.
    /// Kept for backwards compatibility with SuspectController and DialogueSequence.
    /// </summary>
    public void SayDialogue(SuspectCharacter character, string dialogue, bool clearHistory = false,
        bool waitForInput = false, Action onComplete = null)
    {
        SayDialogue(character != null ? character.Speaking : null, dialogue, clearHistory, waitForInput, onComplete);
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

        SpeakingInteraction speaking = null;
        if (networkObjectId != ulong.MaxValue &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj))
        {
            speaking = netObj.GetComponent<SpeakingInteraction>();
        }

        if (speaking == null && SuspectController.Instance != null && SuspectController.Instance.CurrentSuspect != null)
            speaking = SuspectController.Instance.CurrentSuspect.Speaking;

        if (speaking == null)
        {
            Debug.LogWarning("DialogueManager: No SpeakingInteraction resolved for dialogue: " + dialogue);
            return;
        }

        SpawnSubtitles(dialogue, speaking.SpeakerName, Color.white, false, clearHistory, waitForInput);
        PlayDialogueAudio(dialogue, speaking.VoiceAudioClips, speaking.AudioSource);
    }

    public void PlayDialogueAudio(string dialogue, AudioClip[] audioClips, AudioSource audioSource, UnityAction onComplete = null)
    {
        StopDialogueAudio();
        if (audioClips.Length == 0) { onComplete?.Invoke(); return; }
        if (audioSource == null) { onComplete?.Invoke(); return; }
        
        audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, onComplete));
    }

    /// <summary>
    /// Plays audio for a megaphone bark using a dedicated coroutine slot that is
    /// independent of <see cref="audioDialogueCoroutine"/>. Character speech calls
    /// (SayDialogue → StopDialogueAudio) will not cancel this playback, ensuring
    /// <paramref name="onComplete"/> always fires.
    /// </summary>
    public void PlayMegaphoneAudio(string dialogue, AudioClip[] audioClips, AudioSource audioSource, UnityAction onComplete = null)
    {
        if (_megaphoneAudioCoroutine != null)
        {
            StopCoroutine(_megaphoneAudioCoroutine);
            _megaphoneAudioCoroutine = null;
        }

        if (audioClips.Length == 0) { onComplete?.Invoke(); return; }
        if (audioSource == null) { onComplete?.Invoke(); return; }

        _megaphoneAudioCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, () =>
        {
            _megaphoneAudioCoroutine = null;
            onComplete?.Invoke();
        }));
    }

    IEnumerator PlayDialogueAudio(string dialogue, AudioSource audioSource, AudioClip[] audioClips, UnityAction onComplete = null)
    {
        float duration = dialogue.Length * secondsPerCharacter * 0.5f;
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

    /// <summary>
    /// Immediately stops dialogue audio on the local client.
    /// The active subtitle is left intact and will disappear on its natural timer.
    /// </summary>
    public void SkipCurrentLine()
    {
        StopDialogueAudio();
    }

    /// <summary>
    /// Returns true if any active subtitle is still running its typewriter reveal.
    /// </summary>
    public bool IsAnySubtitleRevealing()
    {
        foreach (Transform child in subtitlesContainer)
        {
            var reveal = child.GetComponentInChildren<TMPTextReveal>();
            if (reveal != null && reveal.IsRevealing) return true;
        }
        return false;
    }

    /// <summary>
    /// Immediately completes the typewriter animation on all active subtitles,
    /// showing their full text without skipping the line entirely.
    /// </summary>
    public void CompleteCurrentReveal()
    {
        foreach (Transform child in subtitlesContainer)
        {
            var reveal = child.GetComponentInChildren<TMPTextReveal>();
            if (reveal != null)
                reveal.CompleteReveal();
        }
    }

    public void InitiateChoices(Transform lookTarget, string[] choices)
    {
        dialogueChoiceSystem.StartDialogueChoices(lookTarget,choices);
    }

    public GameObject SpawnSubtitles(string text, string characterName = null, Color nameColor = default,
        bool isPlayer = false, bool clearHistory = false, bool waitForInput = false)
    {
        DestroyPreviousSubtitles();

        // Log to dialogue history
        if (isPlayer)
        {
            DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Player, characterName, text);
        }
        else
        {
            DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Suspect, characterName, text);
        }

        Subtitles subtitles = Instantiate(isPlayer ? playerSubtitlesPrefab : NPCSubtitlesPrefab, subtitlesContainer);

        subtitles.SetText(text, characterName, nameColor);

        // Apply wobble effect to NPC lines. The profile is consumed once per subtitle spawn.
        if (!isPlayer)
        {
            subtitles.SetWobble(_nextLineWobbleProfile);
            _nextLineWobbleProfile = null;
        }
        subtitles.transform.SetAsLastSibling();

        if (waitForInput)
        {
            _waitingSubtitle = subtitles;
            subtitles.ShowContinuePrompt(true);
        }
        else
        {
            _waitingSubtitle = null;
            float duration = text.Length * secondsPerCharacter + subtitleLingerSeconds;
            _subtitleDestroyCoroutine = StartCoroutine(DestroySubtitles(subtitles.gameObject, duration));
        }

        return subtitles.gameObject;
    }

    /// <summary>
    /// Returns true while dialogue audio is actively playing.
    /// </summary>
    public bool IsSpeaking => audioDialogueCoroutine != null;

    /// <summary>
    /// True while one or more subtitle instances are present in the subtitles container.
    /// Use this to gate UI that should not appear while an NPC is responding.
    /// </summary>
    public bool HasActiveSubtitles
    {
        get
        {
            if (subtitlesContainer == null) return false;
            foreach (Transform child in subtitlesContainer)
            {
                // In Unity, Destroyed objects return true for child != null 
                // until the end of the frame, but we can check if they are active.
                if (child.gameObject.activeInHierarchy) return true;
            }
            return false;
        }
    }

    private bool _dialogueInputReceived = false;

    // -------------------------------------------------------------------------
    // Wobble text — consumed once when the next NPC subtitle is spawned.
    // -------------------------------------------------------------------------

    private TMPWobbleProfile _nextLineWobbleProfile;

    /// <summary>
    /// Primes the next NPC subtitle spawned via <see cref="SpawnSubtitles"/> to use the
    /// given wobble <paramref name="profile"/>. Pass <c>null</c> to suppress wobble on the
    /// next line. The value is consumed and cleared on use.
    /// Called by <see cref="ScriptedDialogueRunner"/> via ClientRpc before each line.
    /// </summary>
    public void SetNextLineWobbleProfile(TMPWobbleProfile profile) => _nextLineWobbleProfile = profile;

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
        StopDialogueAudio();
    }

    public IEnumerator WaitForInputRoutine(Action onComplete = null)
    {
        _dialogueInputReceived = false;
        yield return null;

        while (!_dialogueInputReceived)
        {
            if (Input.GetKeyDown(KeyCode.E) && _waitingSubtitle != null && _waitingSubtitle.IsPromptActive)
            {
                // During scripted dialogue, route through the multi-player advance gate so
                // both players must confirm (or the timeout fires) before the sequence continues.
                if (ScriptedDialogueRunner.IsScriptedModeActive)
                    ScriptedDialogueRunner.Instance.AdvanceScriptedLineServerRpc();
                else
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
        CancelSubtitleDestroy();
        foreach (Transform child in subtitlesContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void CancelSubtitleDestroy()
    {
        if (_subtitleDestroyCoroutine != null)
        {
            StopCoroutine(_subtitleDestroyCoroutine);
            _subtitleDestroyCoroutine = null;
        }
    }

    IEnumerator DestroySubtitles(GameObject subtitle, float displayDuration)
    {
        // Wait for the typewriter animation to complete before starting the display countdown.
        var textReveal = subtitle.GetComponentInChildren<TMPTextReveal>();
        if (textReveal != null)
            yield return new WaitUntil(() => subtitle == null || textReveal == null || !textReveal.IsRevealing);

        if (subtitle == null) yield break;

        yield return new WaitForSeconds(displayDuration);

        if (subtitle != null)
            Destroy(subtitle);

        _subtitleDestroyCoroutine = null;
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