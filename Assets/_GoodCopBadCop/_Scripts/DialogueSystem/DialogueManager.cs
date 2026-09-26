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
    Coroutine audioDialogueCoroutine;
    private AudioSource _activeDialogueSource;
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

    [Tooltip("CanvasGroup on this same GameObject (the dialogue system's root Canvas), used to " +
             "visually hide all subtitles and dialogue choices — without touching their own " +
             "active-state bookkeeping — while the pause menu is open.")]
    [SerializeField] private CanvasGroup dialogueCanvasGroup;

    private Subtitles _waitingSubtitle;

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
            StartCoroutine(WaitForInputRoutine(onComplete));
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

        // A non-participant still receives scripted dialogue subtitles, but cannot advance the
        // sequence. Do not show them a misleading continue prompt; unscripted callers retain
        // their existing wait-for-input behaviour.
        bool showWaitForInput = waitForInput &&
            (ScriptedDialogueRunner.ActiveDialogueSpeakerNetId == 0 || ScriptedDialogueRunner.IsScriptedModeActive);
        SpawnSubtitles(dialogue, speaking.SpeakerName, Color.white, false, clearHistory, showWaitForInput);

        // Lines flagged to play a laugh instead of normal speech (e.g. ScriptedDialogueNode/
        // Choice.playLaughSfx) show their subtitle immediately but skip the voice-clip cycling
        // entirely, playing the speaker's laugh SFX in its place instead.
        if (playLaughSfx)
        {
            speaking.PlayLaugh();
        }
        else if (!speaking.IsLaughing)
        {
            PlayDialogueAudio(dialogue, speaking.VoiceAudioClips, speaking.AudioSource, isMutant: speaking.IsMutantVoiceActive);
        }
    }

    public void PlayDialogueAudio(string dialogue, AudioClip[] audioClips, AudioSource audioSource, UnityAction onComplete = null, bool isMutant = false)
    {
        StopDialogueAudio();
        if (audioClips.Length == 0) { onComplete?.Invoke(); return; }
        if (audioSource == null) { onComplete?.Invoke(); return; }
        
        audioDialogueCoroutine = StartCoroutine(PlayDialogueAudio(dialogue, audioSource, audioClips, onComplete, isMutant, isMegaphone: false));
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

        // Track the active source so StopDialogueAudio / megaphone stop can call Stop() on it.
        // isMegaphone is passed explicitly by the caller (PlayMegaphoneAudio vs. PlayDialogueAudio)
        // rather than inferred from coincidental state — inferring it from whether
        // _megaphoneAudioCoroutine happened to be non-null caused suspect dialogue audio to be
        // mistaken for megaphone audio (and vice versa) whenever both were in flight together.
        if (isMegaphone)
            _activeMegaphoneSource = audioSource;
        else
            _activeDialogueSource = audioSource;

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

        if (isMegaphone)
            _activeMegaphoneSource = null;
        else
            _activeDialogueSource = null;

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

        if (_activeDialogueSource != null)
        {
            _activeDialogueSource.Stop();
            _activeDialogueSource = null;
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

    /// <summary>
    /// Spawns a persistent caption showing the resolved player dialogue choice. It stacks above
    /// the NPC response subtitle and normally disappears on its own after a few seconds (see
    /// <see cref="AutoHideChoiceEcho"/>) or as soon as a genuinely later subtitle spawns (see
    /// <see cref="_choiceEchoProtected"/>). The player who chose the line may additionally
    /// dismiss their own echo immediately by advancing.
    /// </summary>
    /// <param name="dismissOnLocalAdvance">Set only for the player who chose this line, so their own echo closes immediately when they advance.</param>
    public void ShowChoiceEcho(string text, string playerName, Color color, bool dismissOnLocalAdvance = false)
    {
        HideChoiceEcho();

        DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Player, playerName, text);

        Subtitles echo = Instantiate(playerSubtitlesPrefab, subtitlesContainer);
        echo.SetText(text, playerName, color);
        echo.transform.SetAsFirstSibling();

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

        // Apply wobble effect and font override to NPC lines. Each is consumed once per subtitle spawn.
        if (!isPlayer)
        {
            subtitles.SetWobble(_nextLineWobbleProfile);
            _nextLineWobbleProfile = null;
            subtitles.SetFontOverride(_nextLineFontOverride);
            _nextLineFontOverride = null;
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

        // WaitForInputRoutine runs only on the server (SayDialogue is always called server-side
        // in scripted sequences), so non-host clients have no coroutine that would call
        // ClearHistory() after this flag is set. Without this explicit clear, the final
        // waitForInput subtitle in a session stays on screen indefinitely on client machines
        // because there is no subsequent SpawnSubtitles call to evict it via
        // DestroyPreviousSubtitles(). On the host, WaitForInputRoutine will also call
        // ClearHistory() on the next frame — that second call is a safe no-op.
        if (_waitingSubtitle != null)
        {
            ClearHistory();
            _waitingSubtitle = null;
        }
    }

    public IEnumerator WaitForInputRoutine(Action onComplete = null)
    {
        _dialogueInputReceived = false;
        yield return null;

        while (!_dialogueInputReceived)
        {
            // During scripted dialogue, ScriptedDialogueRunner.Update() is the sole owner of
            // raw input handling (skip-reveal-then-advance) and drives this routine indirectly
            // via AdvanceDialogueServerRpc -> AdvanceDialogueClientRpc -> _dialogueInputReceived.
            // Reading input here too caused a same-frame race: a single click that completed the
            // typewriter reveal could also be read as the "prompt already active" advance input
            // in this routine before ShowPromptAfterTypewriter's IsPromptActive update settled,
            // skipping straight to the next line instead of just finishing the reveal.
            if (!ScriptedDialogueRunner.IsScriptedModeActive && _waitingSubtitle != null && IsAdvanceInputPressed())
            {
                if (!_waitingSubtitle.IsPromptActive)
                {
                    // First input just completes the typewriter reveal for this line — does not advance yet,
                    // mirroring the skip/advance convention used in IntroCinematicController.
                    CompleteCurrentReveal();
                }
                else
                {
                    // During scripted dialogue, route through the multi-player advance gate so
                    // both players must confirm (or the timeout fires) before the sequence continues.
                    if (ScriptedDialogueRunner.IsScriptedModeActive)
                        ScriptedDialogueRunner.Instance.AdvanceScriptedLineServerRpc();
                    else
                        AdvanceDialogueServerRpc();
                }
            }
            yield return null;
        }

        ClearHistory();
        _waitingSubtitle = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Returns true if any of the accepted dialogue-advance inputs were pressed this frame —
    /// keyboard E, left mouse click (when not over UI), or a gamepad face/start button. Matches
    /// the input set used by <see cref="IntroCinematicController"/> for consistency.
    /// </summary>
    private bool IsAdvanceInputPressed()
    {
        if (UIController.Instance != null && UIController.Instance.IsPaused) return false;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        return Input.GetKeyDown(KeyCode.E)
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