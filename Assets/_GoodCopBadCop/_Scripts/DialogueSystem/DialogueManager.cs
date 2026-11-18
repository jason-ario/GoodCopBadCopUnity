using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class DialogueManager : NetworkBehaviour
{
    public static DialogueManager Instance;
    [SerializeField] private float secondsPerCharacter = 0.06f;
    [SerializeField] private float subtitleLingerSeconds = 1f;

    // Character/phone voice playback, one coroutine per AudioSource. Several conversations (one per
    // NPC) can talk at the same time, so a new line only stops the voice already playing on ITS OWN
    // speaker's source — never another conversation's.
    private readonly Dictionary<AudioSource, Coroutine> _dialogueAudioCoroutines = new Dictionary<AudioSource, Coroutine>();

    // Conversation key that started each source's voice line (see PlayDialogueAudio ownerKey).
    private readonly Dictionary<AudioSource, ulong> _dialogueAudioOwners = new Dictionary<AudioSource, ulong>();

    /// <summary>
    /// Conversation key for scripted megaphone / phone dialogue lines (no NPC NetworkObject). Its
    /// advance clears only the megaphone's own waiting line and voice, so a megaphone/phone line
    /// running beside an NPC conversation never touches that conversation.
    /// </summary>
    public const ulong MegaphoneSpeakerKey = ulong.MaxValue - 1;

    private AudioSource _activeMegaphoneSource;
    private Coroutine _subtitleDestroyCoroutine;

    /// <summary>
    /// Separate coroutine slot for megaphone barks so that character speech
    /// (SayDialogueClientRpc → StopDialogueAudio) cannot cancel an in-flight
    /// megaphone bark and leave MegaphoneDialogueManager._isSpeaking stuck true.
    /// </summary>
    private Coroutine _megaphoneAudioCoroutine;

    [SerializeField] private float minDelayBetweenClips = 0.03f;
    [SerializeField] private float maxDelayBetweenClips = 0.1f;

    [Header("Pitch Variation")]
    [Tooltip("Probability (0–1) that any given voice clip gets a random pitch shift applied.")]
    [Range(0f, 1f)]
    [SerializeField] private float _pitchShiftChance = 0.25f;

    [Tooltip("Minimum pitch multiplier relative to the AudioSource's current pitch.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float _pitchShiftMin = 0.93f;

    [Tooltip("Maximum pitch multiplier relative to the AudioSource's current pitch.")]
    [Range(1f, 2f)]
    [SerializeField] private float _pitchShiftMax = 1.2f;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    [SerializeField] private Subtitles NPCSubtitlesPrefab;
    [SerializeField] private Subtitles playerSubtitlesPrefab;
    [SerializeField] RectTransform subtitlesContainer;

    [Tooltip("Wobble applied to every subtitle (NPC, player, choice echo, in-world bubble) unless a " +
             "scripted line primes its own via SetNextLineWobbleProfile. Should match " +
             "ScriptedDialogueRunner's default so all dialogue looks the same.")]
    [SerializeField] private TMPWobbleProfile defaultWobbleProfile;

    /// <summary>Wobble used by subtitles that aren't primed by a scripted line (see <see cref="defaultWobbleProfile"/>).</summary>
    public TMPWobbleProfile DefaultWobbleProfile => defaultWobbleProfile;

    [Tooltip("CanvasGroup on this same GameObject (the dialogue system's root Canvas), used to " +
             "visually hide all subtitles and dialogue choices — without touching their own " +
             "active-state bookkeeping — while the pause menu is open.")]
    [SerializeField] private CanvasGroup dialogueCanvasGroup;

    private Subtitles _waitingSubtitle;

    // Speaker (NetworkObjectId) whose conversation owns _waitingSubtitle; ulong.MaxValue = none/legacy.
    private ulong _waitingSubtitleSpeakerNetId = ulong.MaxValue;

    /// <summary>
    /// The persistent "player choice echo" line spawned by <see cref="ShowChoiceEcho"/>.
    /// It normally ignores advance/skip input and disappears via its own lifecycle; the player
    /// who actually spoke the choice may opt into immediate local dismissal on advance.
    /// </summary>
    private GameObject _activeChoiceEcho;

    /// <summary>True when the local player owns the active echo and advancing should dismiss it immediately.</summary>
    private bool _dismissChoiceEchoOnAdvance;

    /// <summary>
    /// True for the single subtitle spawn immediately following <see cref="ShowChoiceEcho"/> —
    /// lets the NPC's response subtitle spawn without evicting the echo above it. Consumed (set
    /// false) the first time <see cref="DestroyPreviousSubtitles"/> runs afterward, so any
    /// further/later subtitle spawn evicts the echo normally.
    /// </summary>
    private bool _choiceEchoProtected;

    private Coroutine _choiceEchoDestroyCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Hides all subtitles and dialogue choice UI (via <see cref="dialogueCanvasGroup"/>) while
    /// the pause menu is open, and reveals them again as soon as it closes. Toggling the Canvas
    /// Group's alpha/interactivity — rather than any container's active state — leaves ongoing
    /// coroutines (typewriter reveal, auto-hide timers) and flags such as
    /// <see cref="HasActiveSubtitles"/> completely undisturbed, so dialogue resumes exactly
    /// where it left off once the player unpauses.
    /// </summary>
    private void Update()
    {
        UpdateSubtitlesVisibilityForChoices();

        if (dialogueCanvasGroup == null) return;

        bool isPaused = UIController.Instance != null && UIController.Instance.IsPaused;
        dialogueCanvasGroup.alpha = isPaused ? 0f : 1f;
        dialogueCanvasGroup.interactable = !isPaused;
        dialogueCanvasGroup.blocksRaycasts = !isPaused;
    }

    private CanvasGroup _subtitlesCanvasGroup;

    /// <summary>
    /// Locally hides every subtitle in <see cref="subtitlesContainer"/> (NPC lines, player lines
    /// and the choice echo) while the dialogue choice panel is showing. Only the CanvasGroup
    /// alpha is changed, so subtitle lifetimes, typewriter coroutines and
    /// <see cref="HasActiveSubtitles"/> keep working exactly as before.
    /// </summary>
    private void UpdateSubtitlesVisibilityForChoices()
    {
        if (subtitlesContainer == null) return;

        if (_subtitlesCanvasGroup == null)
        {
            if (!subtitlesContainer.TryGetComponent(out _subtitlesCanvasGroup))
                _subtitlesCanvasGroup = subtitlesContainer.gameObject.AddComponent<CanvasGroup>();
        }

        bool choicesVisible = dialogueChoiceSystem != null && dialogueChoiceSystem.IsChoicePanelVisible;
        _subtitlesCanvasGroup.alpha = choicesVisible ? 0f : 1f;
    }
    

    public void SayDialogue(SpeakingInteraction speaking, string dialogue, bool clearHistory = false,
        bool waitForInput = false, Action onComplete = null, bool playLaughSfx = false)
    {
        ulong networkObjectId = ulong.MaxValue;
        if (speaking != null)
        {
            var netObj = speaking.GetComponent<NetworkObject>();
            if (netObj != null) networkObjectId = netObj.NetworkObjectId;
        }

        if (IsServer)
        {
            SayDialogueClientRpc(dialogue, networkObjectId, clearHistory, waitForInput, playLaughSfx);
        }
        else
        {
            SayDialogueServerRpc(dialogue, networkObjectId, clearHistory, waitForInput, playLaughSfx);
        }

        if (waitForInput)
        {
            StartCoroutine(WaitForInputRoutine(onComplete, networkObjectId));
        }
    }

    /// <summary>
    /// Convenience overload that resolves the SpeakingInteraction from a SuspectCharacter.
    /// Kept for backwards compatibility with SuspectController and DialogueSequence.
    /// </summary>
    public void SayDialogue(SuspectCharacter character, string dialogue, bool clearHistory = false,
        bool waitForInput = false, Action onComplete = null, bool playLaughSfx = false)
    {
        SayDialogue(character != null ? character.Speaking : null, dialogue, clearHistory, waitForInput, onComplete, playLaughSfx);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayDialogueServerRpc(string dialogue, ulong networkObjectId, bool clearHistory = false,
        bool waitForInput = false, bool playLaughSfx = false)
    {
        SayDialogueClientRpc(dialogue, networkObjectId, clearHistory, waitForInput, playLaughSfx);
    }

    [ClientRpc]
    private void SayDialogueClientRpc(string dialogue, ulong networkObjectId, bool clearHistory = false,
        bool waitForInput = false, bool playLaughSfx = false)
    {
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

        ulong speakerNetId = speaking.NetworkObjectId;

        // A non-participant still receives scripted dialogue subtitles, but cannot advance the
        // sequence. Do not show them a misleading continue prompt; unscripted callers retain
        // their existing wait-for-input behaviour. Scoped per speaker: several scripted
        // conversations can run in parallel.
        bool showWaitForInput = waitForInput &&
            (!ScriptedDialogueRunner.IsSpeakerInActiveDialogue(speakerNetId) ||
             ScriptedDialogueRunner.IsLocalParticipantOfSpeaker(speakerNetId));

        // Booth/suspect and scripted lines are broadcast to every client. Only conversation
        // participants and free players within overhear range of the speaker see the subtitle;
        // everyone else (including players in a different conversation) just hears the voice.
        if (OverhearRange.CanLocalPlayerSee(speaking.transform))
            SpawnSubtitles(dialogue, speaking.SpeakerName, Color.white, false, clearHistory, showWaitForInput,
                overhearSource: speaking.transform, speakerNetId: speakerNetId);
        else if (clearHistory && !OverhearRange.IsLocalPlayerConversationParticipant)
            ClearHistory();

        // Lines flagged to play a laugh instead of normal speech (e.g. ScriptedDialogueNode/
        // Choice.playLaughSfx) show their subtitle immediately but skip the voice-clip cycling
        // entirely, playing the speaker's laugh SFX in its place instead.
        if (playLaughSfx)
        {
            StopDialogueAudio(speaking.AudioSource);
            speaking.PlayLaugh();
        }
        else if (!speaking.IsLaughing)
        {
            PlayDialogueAudio(dialogue, speaking.VoiceAudioClips, speaking.AudioSource, isMutant: speaking.IsMutantVoiceActive);
        }
    }

    /// <param name="ownerKey">Conversation key (NPC NetworkObjectId or <see cref="MegaphoneSpeakerKey"/>) whose advance should stop this line; default = none.</param>
    public void PlayDialogueAudio(string dialogue, AudioClip[] audioClips, AudioSource audioSource, UnityAction onComplete = null,
        bool isMutant = false, ulong ownerKey = ulong.MaxValue)
    {
        StopDialogueAudio(audioSource);
        if (audioClips.Length == 0) { onComplete?.Invoke(); return; }
        if (audioSource == null) { onComplete?.Invoke(); return; }

        _dialogueAudioOwners[audioSource] = ownerKey;
        _dialogueAudioCoroutines[audioSource] =
            StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, onComplete, isMutant, isMegaphone: false));
    }

    /// <summary>
    /// Plays audio for a megaphone bark using a dedicated coroutine slot that is
    /// independent of the character voice coroutines. Character speech calls
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

        if (_activeMegaphoneSource != null)
        {
            _activeMegaphoneSource.Stop();
            _activeMegaphoneSource = null;
        }

        if (audioClips.Length == 0) { onComplete?.Invoke(); return; }
        if (audioSource == null) { onComplete?.Invoke(); return; }

        _megaphoneAudioCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, () =>
        {
            _megaphoneAudioCoroutine = null;
            onComplete?.Invoke();
        }, isMutant: false, isMegaphone: true));
    }

    IEnumerator PlayDialogueAudio(string dialogue, AudioSource audioSource, AudioClip[] audioClips, UnityAction onComplete = null, bool isMutant = false, bool isMegaphone = false)
    {
        // Reset pitch before starting. A previous coroutine stopped mid-shift would leave
        // the AudioSource in a dirty state; reading it as basePitch would compound the shift
        // on every subsequent call. Dialogue AudioSources are always expected to start at 1.
        audioSource.pitch = 1f;
        float basePitch = 1f;

        // Track the active megaphone source so a megaphone stop can call Stop() on it. Character
        // sources are tracked by _dialogueAudioCoroutines. isMegaphone is passed explicitly by the
        // caller (PlayMegaphoneAudio vs. PlayDialogueAudio) rather than inferred from state.
        if (isMegaphone)
            _activeMegaphoneSource = audioSource;

        // Halved again (0.25f) on top of the existing 0.5f scale-down so voice clip
        // playback covers roughly a quarter of the raw text-length estimate — dialogue
        // audio was running noticeably longer than the actual spoken lines warranted.
        float duration = dialogue.Length * secondsPerCharacter * 0.25f;
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

            if (isMutant && UnityEngine.Random.value < _pitchShiftChance)
                audioSource.pitch = basePitch * UnityEngine.Random.Range(_pitchShiftMin, _pitchShiftMax);

            audioSource.clip = clip;
            audioSource.Play();

            float clipDuration = clip.length;
            yield return new WaitForSeconds(clipDuration);

            audioSource.pitch = basePitch;

            float extraDelay = UnityEngine.Random.Range(minDelayBetweenClips, maxDelayBetweenClips);
            yield return new WaitForSeconds(extraDelay);

            timer += clipDuration + extraDelay;
        }

        audioSource.pitch = basePitch;

        // A coroutine only reaches this point if it was never stopped, i.e. it is still the one
        // registered for its source (starting a new line on a source always stops the old one).
        if (isMegaphone)
            _activeMegaphoneSource = null;
        else
        {
            _dialogueAudioCoroutines.Remove(audioSource);
            _dialogueAudioOwners.Remove(audioSource);
        }

        onComplete?.Invoke();
    }

    /// <summary>Stops every character/phone voice line playing on this client.</summary>
    void StopDialogueAudio()
    {
        foreach (var kvp in _dialogueAudioCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
            if (kvp.Key != null) kvp.Key.Stop();
        }
        _dialogueAudioCoroutines.Clear();
        _dialogueAudioOwners.Clear();
    }

    /// <summary>Stops only the voice line playing on <paramref name="source"/> (one speaker).</summary>
    void StopDialogueAudio(AudioSource source)
    {
        if (source == null) return;
        if (_dialogueAudioCoroutines.TryGetValue(source, out Coroutine running))
        {
            if (running != null) StopCoroutine(running);
            _dialogueAudioCoroutines.Remove(source);
            _dialogueAudioOwners.Remove(source);
            source.Stop();
        }
    }

    /// <summary>
    /// Stops the voice of one conversation: every line started for <paramref name="speakerNetId"/>
    /// (see PlayDialogueAudio ownerKey) plus that NPC's own voice source. Stops every voice for the
    /// legacy global key <see cref="ulong.MaxValue"/>.
    /// </summary>
    void StopDialogueAudioForSpeaker(ulong speakerNetId)
    {
        if (speakerNetId == ulong.MaxValue)
        {
            StopDialogueAudio();
            return;
        }

        var owned = new List<AudioSource>();
        foreach (var kvp in _dialogueAudioOwners)
            if (kvp.Value == speakerNetId) owned.Add(kvp.Key);
        foreach (var source in owned)
            StopDialogueAudio(source);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
        {
            var speaking = netObj.GetComponent<SpeakingInteraction>();
            if (speaking != null) StopDialogueAudio(speaking.AudioSource);
        }
    }

    /// <summary>
    /// Immediately stops the local player's conversation partner's voice line (every voice if the
    /// partner can't be resolved). The active subtitle is left intact and disappears on its timer.
    /// </summary>
    public void SkipCurrentLine()
    {
        if (OverhearRange.TryGetLocalConversationPartner(out SpeakingInteraction partner))
            StopDialogueAudio(partner.AudioSource);
        else
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

    /// <summary>The currently displayed choice echo, or null. Lets callers clear only an echo they own.</summary>
    public GameObject ActiveChoiceEcho => _activeChoiceEcho;

    /// <summary>
    /// Spawns a persistent caption showing the resolved player dialogue choice. It stacks above
    /// the NPC response subtitle and normally disappears on its own after a few seconds (see
    /// <see cref="AutoHideChoiceEcho"/>) or as soon as a genuinely later subtitle spawns (see
    /// <see cref="_choiceEchoProtected"/>). The player who chose the line may additionally
    /// dismiss their own echo immediately by advancing.
    /// </summary>
    /// <param name="dismissOnLocalAdvance">Set only for the player who chose this line, so their own echo closes immediately when they advance.</param>
    /// <param name="overhearSource">Optional conversation source; while set, the echo hides live whenever the local player is out of overhear range (see <see cref="OverhearSubtitleGate"/>).</param>
    /// <param name="participantsAlwaysSee">With <paramref name="overhearSource"/>: conversation participants ignore the range check.</param>
    public void ShowChoiceEcho(string text, string playerName, Color color, bool dismissOnLocalAdvance = false,
        Transform overhearSource = null, bool participantsAlwaysSee = true)
    {
        HideChoiceEcho();

        DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Player, playerName, text);

        Subtitles echo = Instantiate(playerSubtitlesPrefab, subtitlesContainer);
        echo.SetText(text, playerName, color);
        echo.SetWobble(defaultWobbleProfile);
        echo.transform.SetAsFirstSibling();
        OverhearSubtitleGate.Attach(echo.gameObject, overhearSource, participantsAlwaysSee);

        _activeChoiceEcho = echo.gameObject;
        _dismissChoiceEchoOnAdvance = dismissOnLocalAdvance;
        _choiceEchoProtected = true;

        float duration = text.Length * secondsPerCharacter + subtitleLingerSeconds;
        _choiceEchoDestroyCoroutine = StartCoroutine(AutoHideChoiceEcho(echo.gameObject, duration));
    }

    /// <summary>Auto-hides the choice echo a few seconds after its typewriter reveal finishes, independent of any advance/skip input.</summary>
    private IEnumerator AutoHideChoiceEcho(GameObject echoObj, float displayDuration)
    {
        var textReveal = echoObj.GetComponentInChildren<TMPTextReveal>();
        if (textReveal != null)
            yield return new WaitUntil(() => echoObj == null || textReveal == null || !textReveal.IsRevealing);

        if (echoObj == null) yield break;

        yield return new WaitForSeconds(displayDuration);

        if (_activeChoiceEcho == echoObj)
            HideChoiceEcho();
    }

    /// <summary>
    /// Removes the choice echo spawned by <see cref="ShowChoiceEcho"/>, if any is active.
    /// </summary>
    public void HideChoiceEcho()
    {
        if (_choiceEchoDestroyCoroutine != null)
        {
            StopCoroutine(_choiceEchoDestroyCoroutine);
            _choiceEchoDestroyCoroutine = null;
        }

        _choiceEchoProtected = false;
        _dismissChoiceEchoOnAdvance = false;

        if (_activeChoiceEcho != null)
        {
            Destroy(_activeChoiceEcho);
            _activeChoiceEcho = null;
        }
    }

    /// <param name="overhearSource">Optional speaker/conversation source; while set, the subtitle hides live whenever the local player is out of overhear range (see <see cref="OverhearSubtitleGate"/>). Leave null for unconditional lines (phone, megaphone).</param>
    /// <param name="participantsAlwaysSee">With <paramref name="overhearSource"/>: conversation participants ignore the range check.</param>
    /// <param name="speakerNetId">NetworkObjectId of the speaking NPC (<see cref="ulong.MaxValue"/> for speaker-less lines). A wait-for-input line remembers it so only that conversation's advance clears it.</param>
    public GameObject SpawnSubtitles(string text, string characterName = null, Color nameColor = default,
        bool isPlayer = false, bool clearHistory = false, bool waitForInput = false,
        Transform overhearSource = null, bool participantsAlwaysSee = true, ulong speakerNetId = ulong.MaxValue)
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

        if (!isPlayer)
            ConversationNameTag.ReportSpeaker(characterName, nameColor);

        subtitles.SetText(text, characterName, nameColor);

        // Wobble: NPC lines consume a scripted priming (which may be an explicit null to suppress);
        // everything else (booth/world dialogue, barks, player lines) falls back to the default.
        TMPWobbleProfile wobble = defaultWobbleProfile;
        if (!isPlayer && _nextLineWobblePrimed)
            wobble = _nextLineWobbleProfile;
        subtitles.SetWobble(wobble);

        // Font override for NPC lines. Each priming is consumed once per subtitle spawn.
        if (!isPlayer)
        {
            _nextLineWobbleProfile = null;
            _nextLineWobblePrimed = false;
            subtitles.SetFontOverride(_nextLineFontOverride);
            _nextLineFontOverride = null;
        }
        subtitles.transform.SetAsLastSibling();
        OverhearSubtitleGate.Attach(subtitles.gameObject, overhearSource, participantsAlwaysSee);

        if (waitForInput)
        {
            _waitingSubtitle = subtitles;
            _waitingSubtitleSpeakerNetId = speakerNetId;
            subtitles.ShowContinuePrompt(true);
        }
        else
        {
            _waitingSubtitle = null;
            _waitingSubtitleSpeakerNetId = ulong.MaxValue;
            float duration = text.Length * secondsPerCharacter + subtitleLingerSeconds;

            // If the choice echo survived eviction above (protected) to coexist with this
            // subtitle, stop its independent auto-hide timer and destroy it together with
            // this subtitle instead — so they disappear at the same time rather than the
            // (usually shorter) echo vanishing first.
            GameObject echoToSync = _activeChoiceEcho;
            if (echoToSync != null && _choiceEchoDestroyCoroutine != null)
            {
                StopCoroutine(_choiceEchoDestroyCoroutine);
                _choiceEchoDestroyCoroutine = null;
            }

            _subtitleDestroyCoroutine = StartCoroutine(DestroySubtitles(subtitles.gameObject, duration, echoToSync));
        }

        return subtitles.gameObject;
    }

    /// <summary>
    /// Returns true while dialogue audio is actively playing.
    /// </summary>
    public bool IsSpeaking => _dialogueAudioCoroutines.Count > 0;

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

    // -------------------------------------------------------------------------
    // Wobble text — consumed once when the next NPC subtitle is spawned.
    // -------------------------------------------------------------------------

    private TMPWobbleProfile _nextLineWobbleProfile;
    private bool _nextLineWobblePrimed;

    /// <summary>
    /// Primes the next NPC subtitle spawned via <see cref="SpawnSubtitles"/> to use the
    /// given wobble <paramref name="profile"/>. Pass <c>null</c> to suppress wobble on the
    /// next line. The value is consumed and cleared on use; unprimed lines use
    /// <see cref="DefaultWobbleProfile"/>.
    /// Called by <see cref="ScriptedDialogueRunner"/> via ClientRpc before each line.
    /// </summary>
    public void SetNextLineWobbleProfile(TMPWobbleProfile profile)
    {
        _nextLineWobbleProfile = profile;
        _nextLineWobblePrimed = true;
    }

    // -------------------------------------------------------------------------
    // Font override — consumed once when the next NPC subtitle is spawned.
    // -------------------------------------------------------------------------

    private TMP_FontAsset _nextLineFontOverride;

    /// <summary>
    /// Primes the next NPC subtitle spawned via <see cref="SpawnSubtitles"/> to use the given
    /// <paramref name="font"/>. Pass <c>null</c> to keep the subtitle prefab's default font.
    /// The value is consumed and cleared on use. Called by <see cref="ScriptedDialogueRunner"/>
    /// via ClientRpc before each line.
    /// </summary>
    public void SetNextLineFontOverride(TMP_FontAsset font) => _nextLineFontOverride = font;

    /// <summary>
    /// Advances the waiting dialogue line for everyone. <paramref name="speakerNetId"/> scopes the
    /// advance to one conversation (that NPC's NetworkObjectId): only a waiting line owned by that
    /// speaker is cleared and only that speaker's voice stops, so parallel conversations with other
    /// NPCs are untouched. <see cref="ulong.MaxValue"/> (default) is the legacy global advance used
    /// by speaker-less lines (megaphone) and unscripted callers.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void AdvanceDialogueServerRpc(ulong speakerNetId = ulong.MaxValue)
    {
        AdvanceDialogueClientRpc(speakerNetId);
    }

    // Advance counters consumed by WaitForInputRoutine (see there).
    private int _anyAdvanceCount;
    private int _globalAdvanceCount;
    private readonly Dictionary<ulong, int> _speakerAdvanceCounts = new Dictionary<ulong, int>();

    private int SpeakerAdvanceCount(ulong speakerNetId) =>
        _speakerAdvanceCounts.TryGetValue(speakerNetId, out int count) ? count : 0;

    [ClientRpc]
    private void AdvanceDialogueClientRpc(ulong speakerNetId)
    {
        _anyAdvanceCount++;
        if (speakerNetId == ulong.MaxValue)
            _globalAdvanceCount++;
        else
            _speakerAdvanceCounts[speakerNetId] = SpeakerAdvanceCount(speakerNetId) + 1;

        StopDialogueAudioForSpeaker(speakerNetId);

        // WaitForInputRoutine runs only on the server (SayDialogue is always called server-side
        // in scripted sequences), so non-host clients have no coroutine that would call
        // ClearHistory() after this flag is set. Without this explicit clear, the final
        // waitForInput subtitle in a session stays on screen indefinitely on client machines
        // because there is no subsequent SpawnSubtitles call to evict it via
        // DestroyPreviousSubtitles(). Only the line owned by the advancing conversation is
        // cleared — another conversation's waiting line on this screen stays.
        if (_waitingSubtitle != null && OwnsWaitingSubtitle(speakerNetId))
        {
            ClearHistory();
            _waitingSubtitle = null;
            _waitingSubtitleSpeakerNetId = ulong.MaxValue;
        }
    }

    /// <summary>True when an advance for <paramref name="speakerNetId"/> may clear the current waiting line.</summary>
    private bool OwnsWaitingSubtitle(ulong speakerNetId) =>
        speakerNetId == ulong.MaxValue || _waitingSubtitleSpeakerNetId == ulong.MaxValue ||
        _waitingSubtitleSpeakerNetId == speakerNetId;

    /// <summary>
    /// Waits until the line is advanced. With a <paramref name="speakerNetId"/> it completes on that
    /// speaker's advance (or a global one) only, so concurrent conversations on the server don't
    /// complete each other's lines; with the default <see cref="ulong.MaxValue"/> any advance completes it.
    /// </summary>
    public IEnumerator WaitForInputRoutine(Action onComplete = null, ulong speakerNetId = ulong.MaxValue)
    {
        int startAny = _anyAdvanceCount;
        int startGlobal = _globalAdvanceCount;
        int startSpeaker = SpeakerAdvanceCount(speakerNetId);

        bool Advanced() => speakerNetId == ulong.MaxValue
            ? _anyAdvanceCount != startAny
            : _globalAdvanceCount != startGlobal || SpeakerAdvanceCount(speakerNetId) != startSpeaker;

        yield return null;

        while (!Advanced())
        {
            // During scripted dialogue, ScriptedDialogueRunner.Update() is the sole owner of
            // raw input handling (skip-reveal-then-advance) and drives this routine indirectly
            // via AdvanceDialogueServerRpc -> AdvanceDialogueClientRpc -> _dialogueInputReceived.
            // Reading input here too caused a same-frame race: a single click that completed the
            // typewriter reveal could also be read as the "prompt already active" advance input
            // in this routine before ShowPromptAfterTypewriter's IsPromptActive update settled,
            // skipping straight to the next line instead of just finishing the reveal.
            // A world conversation (SuspectWorldDialogue) owns all dialogue input while the local
            // player is in it, and advances only through SpeakingInteraction's vote/timer gate.
            // Without this, a host with any waitForInput routine alive (e.g. from a scripted or
            // megaphone line) would read the same E press and call AdvanceDialogueServerRpc
            // directly — an un-gated broadcast that clears the world line for EVERY client
            // instantly, so one player could skip lines for both without waiting for the timer.
            // Only the line this routine waits on may be advanced from here, and never a line of a
            // scripted conversation (those advance solely through ScriptedDialogueRunner's gate).
            if (!ScriptedDialogueRunner.IsScriptedModeActive && !SuspectWorldDialogue.IsLocalPlayerInConversation &&
                !ScriptedDialogueRunner.IsUnskippableRevealActive &&
                !ScriptedDialogueRunner.IsSpeakerInActiveDialogue(speakerNetId) &&
                _waitingSubtitle != null && OwnsWaitingSubtitle(speakerNetId) && IsAdvanceInputPressed())
            {
                if (!_waitingSubtitle.IsPromptActive)
                {
                    // First input just completes the typewriter reveal for this line — does not advance yet,
                    // mirroring the skip/advance convention used in IntroCinematicController.
                    CompleteCurrentReveal();
                }
                else
                {
                    Debug.Log("[WorldGate] Un-gated AdvanceDialogueServerRpc from WaitForInputRoutine (local input).");
                    AdvanceDialogueServerRpc(speakerNetId);
                }
            }
            yield return null;
        }

        // Clear only this conversation's waiting line (AdvanceDialogueClientRpc normally already
        // did) — never a parallel conversation's line that is on the host's screen. Legacy
        // speaker-less routines keep the original unconditional clear.
        if (speakerNetId == ulong.MaxValue || (_waitingSubtitle != null && OwnsWaitingSubtitle(speakerNetId)))
        {
            ClearHistory();
            _waitingSubtitle = null;
            _waitingSubtitleSpeakerNetId = ulong.MaxValue;
        }
        onComplete?.Invoke();
    }

    /// <summary>
    /// Returns true if any of the accepted dialogue-advance inputs were pressed this frame —
    /// the Interact key (rebindable, default E), left mouse click (when not over UI), or a gamepad face/start button. Matches
    /// the input set used by <see cref="IntroCinematicController"/> for consistency.
    /// </summary>
    private bool IsAdvanceInputPressed()
    {
        if (UIController.Instance != null && UIController.Instance.IsPaused) return false;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        return GoodCopBadCop.Input.RebindableInput.GetKeyDown(GoodCopBadCop.Input.GameAction.Interact)
               || (Input.GetMouseButtonDown(0) && !overUI)
               || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false)
               || (Gamepad.current?.startButton.wasPressedThisFrame ?? false);
    }

    /// <summary>
    /// Immediately dismisses the active choice echo only when it belongs to this local player.
    /// Call this when the local player submits an advance vote, before the shared advance gate
    /// has necessarily opened for every participant.
    /// </summary>
    public void DismissOwnChoiceEchoOnAdvance()
    {
        if (_dismissChoiceEchoOnAdvance)
            HideChoiceEcho();
    }

    /// <summary>
    /// Clears the currently displayed subtitle(s) in response to an advance/skip input.
    /// Listener echoes remain independent of advance input, but the player who spoke the active
    /// choice line gets their own echo dismissed immediately when they advance.
    /// </summary>
    public void ClearHistory()
    {
        CancelSubtitleDestroy();
        DismissOwnChoiceEchoOnAdvance();

        foreach (Transform child in subtitlesContainer)
        {
            if (_activeChoiceEcho != null && child.gameObject == _activeChoiceEcho) continue;
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// Local, unconditional teardown of every on-screen dialogue subtitle — NPC lines, player
    /// lines, the choice echo, and any wait-for-input line — plus the active dialogue voice audio.
    /// Used when a conversation is force-ended (e.g. a verdict closes out the suspect mid-dialogue)
    /// so nothing from it lingers on screen. Megaphone audio is unaffected.
    /// </summary>
    public void ForceClearAllSubtitles()
    {
        StopDialogueAudio();
        CancelSubtitleDestroy();
        HideChoiceEcho();
        _waitingSubtitle = null;
        _waitingSubtitleSpeakerNetId = ulong.MaxValue;

        if (subtitlesContainer == null) return;
        foreach (Transform child in subtitlesContainer)
            Destroy(child.gameObject);
    }

    /// <summary>
    /// Clears previous subtitles ahead of spawning a brand-new one. The choice echo is
    /// protected for exactly one call — the NPC response subtitle spawned right after
    /// <see cref="ShowChoiceEcho"/> — so it survives to coexist with that response. Any further
    /// subtitle spawn after that (a genuinely new/later line) evicts the echo along with it.
    /// </summary>
    void DestroyPreviousSubtitles()
    {
        CancelSubtitleDestroy();
        foreach (Transform child in subtitlesContainer)
        {
            if (_activeChoiceEcho != null && child.gameObject == _activeChoiceEcho)
            {
                if (_choiceEchoProtected)
                {
                    _choiceEchoProtected = false;
                    continue;
                }

                if (_choiceEchoDestroyCoroutine != null)
                {
                    StopCoroutine(_choiceEchoDestroyCoroutine);
                    _choiceEchoDestroyCoroutine = null;
                }
                _activeChoiceEcho = null;
            }

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

    /// <summary>
    /// Auto-destroys <paramref name="subtitle"/> after its typewriter reveal finishes plus
    /// <paramref name="displayDuration"/>. If <paramref name="linkedChoiceEcho"/> is provided
    /// (the choice echo that coexists above this subtitle), it is destroyed at the same moment
    /// so the two disappear together instead of the echo vanishing on its own earlier timer.
    /// </summary>
    IEnumerator DestroySubtitles(GameObject subtitle, float displayDuration, GameObject linkedChoiceEcho = null)
    {
        // Wait for the typewriter animation to complete before starting the display countdown.
        var textReveal = subtitle.GetComponentInChildren<TMPTextReveal>();
        if (textReveal != null)
            yield return new WaitUntil(() => subtitle == null || textReveal == null || !textReveal.IsRevealing);

        if (subtitle == null) yield break;

        yield return new WaitForSeconds(displayDuration);

        if (subtitle != null)
            Destroy(subtitle);

        if (linkedChoiceEcho != null && _activeChoiceEcho == linkedChoiceEcho)
        {
            Destroy(linkedChoiceEcho);
            _activeChoiceEcho = null;
        }

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