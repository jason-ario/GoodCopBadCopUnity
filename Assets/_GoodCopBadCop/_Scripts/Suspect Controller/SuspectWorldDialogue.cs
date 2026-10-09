using System.Collections;
using System.Collections.Generic;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Adds a simple, direct-interaction conversation to a <see cref="SuspectCharacter"/> that is
/// placed and interactable directly in the world (e.g. the Day 1 Suspect_Soldier), as opposed
/// to the interrogation booth flow which is driven exclusively by <see cref="ScriptedDialogueRunner"/>.
///
/// The player walks up, interacts (LMB / E) and is shown up to 3 dialogue options for the
/// current day (see <see cref="DaySet"/>). Both players may hold the same conversation with
/// this NPC at once (see <see cref="SpeakingInteraction"/>'s participant tracking); picking an
/// option votes for it, and conflicting picks are resolved with the exact same rule
/// <see cref="ScriptedDialogueRunner"/> uses for interrogation-booth choice nodes — a unanimous
/// pick wins outright, a split pick is decided by a random draw from the submitted options only,
/// and a timeout keeps the conversation moving if a participant never answers. Picking hides the
/// choices, plays the NPC's response as a click-through subtitle (typewriter reveal, skip-to-
/// complete, then advance — gated the same way on every participant), and returns to the choice
/// menu. Either participant can leave the conversation at any time via the Back button.
/// </summary>
public class SuspectWorldDialogue : MonoBehaviour
{
    [System.Serializable]
    public class DialogueOption
    {
        [TextArea(1, 3)] public string playerLine;
        [TextArea(2, 6)] public string npcResponse;

        [Tooltip("Optional Animator trigger fired on the NPC when this response plays. Leave empty for no animation.")]
        public string animationTrigger;

        [Tooltip("Optional camera cut while this response plays. Same keys as ScriptedDialogueNode.cameraTrigger: " +
                 "'SuspectCam', 'SuspectFaceCam' / 'suspect face', or a ScriptedDialogueRunner registry key. " +
                 "Leave empty for the default dialogue camera.")]
        public string cameraTrigger;
    }

    [System.Serializable]
    public class DaySet
    {
        [Tooltip("Which ShiftManager.CurrentDay this set applies to. Use the highest matching day if the " +
                 "player's current day has no exact set (see GetSetForDay).")]
        public int day = 1;

        [Tooltip("Line the NPC delivers the moment the conversation opens on this day. Leave empty to skip.")]
        [TextArea(2, 6)]
        public string greetingLine;

        [Tooltip("Optional camera trigger key for the greeting line (see DialogueOption.cameraTrigger).")]
        public string greetingCameraTrigger;

        public DialogueOption[] options;
    }

    [Header("Source")]
    [Tooltip("Per-day dialogue sets. The set whose 'day' best matches ShiftManager.CurrentDay is used " +
             "(falls back to the highest day at or below the current day, or the first set if none qualify).")]
    [SerializeField] private DaySet[] daySets;

    [Tooltip("Legacy single source: when daySets is empty, options are sourced automatically from this " +
             "asset's questionResponses (question + earlyDaysAnswer).")]
    [SerializeField] private SuspectData suspectData;

    [Tooltip("Legacy manual fallback options, used only when daySets is empty and suspectData is not assigned.")]
    [SerializeField] private DialogueOption[] manualOptions;

    [Header("Legacy Setup")]
    [Tooltip("Legacy greeting line, used only when daySets is empty.")]
    [TextArea(2, 6)]
    [SerializeField] private string greetingLine;

    [SerializeField] private SpeakingInteraction speaking;
    [SerializeField] private Animator animator;

    [Header("Look At")]
    [Tooltip("The suspect's FLookAnimator. When assigned, the player camera looks at its head " +
             "bone (LeadBone) on entering conversation, and the suspect looks back at Camera.main " +
             "(the live rendering camera, e.g. the booth suspect cam) for the duration of the conversation.")]
    [SerializeField] private FLookAnimator lookAnimator;

    [Tooltip("Fine-tunes the dialogue camera dolly-in framing height (meters, +up/-down) for this " +
             "specific character. Taller characters typically want a small negative value to pull " +
             "the shot back down toward their upper body; shorter ones may want a small positive value.")]
    [SerializeField] private float dialogueCameraVerticalOffset = 0f;

    /// <summary>Exposes <see cref="dialogueCameraVerticalOffset"/> for callers that resolve this NPC's
    /// look target generically (e.g. <see cref="ScriptedDialogueRunner"/>) and need to pass the same
    /// per-character framing tune-up through to <see cref="DialogueChoiceSystem.EnterScriptedDialogueModeOutside"/>.</summary>
    public float DialogueCameraVerticalOffset => dialogueCameraVerticalOffset;

    /// <summary>Head-height transform the player dialogue camera should frame: the FLookAnimator
    /// LeadBone (head), then the SpeakingInteraction look target, then this root as a last resort.
    /// Shared with <see cref="ScriptedDialogueRunner"/> so scripted conversations frame the head too.</summary>
    public Transform DialogueLookTarget
    {
        get
        {
            if (lookAnimator != null && lookAnimator.LeadBone != null) return lookAnimator.LeadBone;
            if (speaking != null && speaking.LookTarget != null) return speaking.LookTarget;
            return transform;
        }
    }

    [Header("Idle State")]
    [Tooltip("When true, sets the Animator's 'Sitting' bool parameter to true on Awake, so this " +
             "NPC starts (and remains, outside of conversation lines) in its sitting idle pose. " +
             "Requires the assigned Animator's controller to have a 'Sitting' bool parameter.")]
    [SerializeField] private bool startSitting = false;

    private enum ConversationState
    {
        Idle,
        WaitingForAdvance,
        WaitingForChoice
    }

    private DialogueOption[] _options;
    private string _greetingLineForCurrentConversation;
    private string _greetingCameraKeyForCurrentConversation;
    private bool _inConversation;
    private ConversationState _state = ConversationState.Idle;
    private Transform _previousObjectToFollow;
    private bool _restoreObjectToFollow;
    private bool _awaitingEngagementResponse;

    // Local presentation: true when this conversation entered booth dialogue mode (the local
    // player is inside and this is the booth's current suspect), which activates the booth
    // suspect cam via SuspectController. False = outside mode (player camera dolly-in).
    private bool _boothCameraMode;

    // Local presentation: the per-line override camera currently active for this client.
    private GameObject _activeLineCam;

    // Server-only: guards against starting the authoritative conversation loop twice.
    private bool _serverConversationRunning;

    // Local: set when a forced exit lands while this client's engagement request is still in
    // flight, so a late grant backs straight out instead of opening the conversation.
    private bool _cancelPendingEngagement;

    // Every live instance, so a global event (e.g. the intro cutscene starting) can tear down
    // whichever world conversation the local player is currently in.
    private static readonly List<SuspectWorldDialogue> _instances = new List<SuspectWorldDialogue>();

    public bool InConversation => _inConversation;

    /// <summary>
    /// Local-only. True while the local player is in (or waiting to join) any world-dialogue
    /// conversation. While true, this conversation owns the player's dialogue lock, cursor and
    /// Interact/Back input — other systems (e.g. an unlocked megaphone sequence in
    /// <see cref="ScriptedDialogueRunner"/>) must not consume that input or restore gameplay
    /// control underneath it.
    /// </summary>
    public static bool IsLocalPlayerInConversation
    {
        get
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                SuspectWorldDialogue dialogue = _instances[i];
                if (dialogue != null && (dialogue._inConversation || dialogue._awaitingEngagementResponse))
                    return true;
            }
            return false;
        }
    }

    private void Awake()
    {
        if (startSitting && animator != null)
            animator.SetBool("Sitting", true);

        _instances.Add(this);
        SubscribeToSpeaking();
    }

    private void OnDestroy()
    {
        _instances.Remove(this);
        UnsubscribeFromSpeaking();
    }

    /// <summary>
    /// Local-only. Ends every world-dialogue conversation the local player is in (or is about
    /// to join) through the normal <see cref="EndConversation"/> path, so the NPC's server-side
    /// participant set, the Back button, choice panel, subtitles, line cameras and dialogue lock
    /// are all released. Used when a global sequence (e.g. the intro cutscene) takes over.
    /// </summary>
    public static void EndAllLocalConversations()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            SuspectWorldDialogue dialogue = _instances[i];
            if (dialogue == null) continue;

            if (dialogue._awaitingEngagementResponse)
                dialogue._cancelPendingEngagement = true;

            if (dialogue._inConversation)
                dialogue.EndConversation();
        }
    }

    private void SubscribeToSpeaking()
    {
        if (speaking == null) return;
        speaking.OnWorldChoicesShown += HandleWorldChoicesShown;
        speaking.OnWorldChoiceFinalized += HandleWorldChoiceFinalized;
        speaking.OnWorldAdvanceGateChanged += HandleWorldAdvanceGateChanged;
        speaking.OnWorldLineCamera += HandleWorldLineCamera;
    }

    private void UnsubscribeFromSpeaking()
    {
        if (speaking == null) return;
        speaking.OnWorldChoicesShown -= HandleWorldChoicesShown;
        speaking.OnWorldChoiceFinalized -= HandleWorldChoiceFinalized;
        speaking.OnWorldAdvanceGateChanged -= HandleWorldAdvanceGateChanged;
        speaking.OnWorldLineCamera -= HandleWorldLineCamera;
    }

    /// <summary>
    /// Configures this component at runtime — used when a scripted sequence dynamically attaches
    /// a world dialogue conversation to a suspect once their scripted task is complete (e.g.
    /// Day_02 handing a suspect off to sit in the yard instead of despawning them). Prefer
    /// authoring <see cref="daySets"/> in the Inspector for scene-placed suspects; use this only
    /// for prefab instances spawned purely at runtime.
    /// </summary>
    public void Configure(SpeakingInteraction speakingRef, Animator animatorRef, DaySet[] sets, bool startSittingNow = true, FLookAnimator lookAnimatorRef = null, SuspectData suspectDataRef = null)
    {
        UnsubscribeFromSpeaking();
        speaking = speakingRef;
        animator = animatorRef;
        daySets = sets;
        startSitting = startSittingNow;
        lookAnimator = lookAnimatorRef;
        if (suspectDataRef != null) suspectData = suspectDataRef;
        SubscribeToSpeaking();
    }

    /// <summary>
    /// Picks the best matching <see cref="DaySet"/> for the given day: exact match preferred,
    /// otherwise the highest day at or below it, otherwise the first authored set.
    /// </summary>
    private DaySet GetSetForDay(int day)
    {
        if (daySets == null || daySets.Length == 0) return null;

        DaySet exact = null;
        DaySet bestBelow = null;
        foreach (DaySet set in daySets)
        {
            if (set.day == day) { exact = set; break; }
            if (set.day < day && (bestBelow == null || set.day > bestBelow.day)) bestBelow = set;
        }

        return exact ?? bestBelow ?? daySets[0];
    }

    private void ResolveOptionsForConversation()
    {
        int currentDay = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 1;
        DaySet set = GetSetForDay(currentDay);

        if (set != null && set.options != null && set.options.Length > 0)
        {
            _options = set.options;
            _greetingLineForCurrentConversation = set.greetingLine;
            _greetingCameraKeyForCurrentConversation = set.greetingCameraTrigger;
            return;
        }

        if (suspectData != null && suspectData.questionResponses != null && suspectData.questionResponses.Length > 0)
        {
            // When this component lives on a full SuspectCharacter (e.g. a booth suspect's
            // player-initiated question conversation — see SuspectCharacter.Awake), resolve each
            // answer through GetQuestionResponse so the current day band, active
            // StoryMismatchAnomaly, and uncanny/replacement overrides are honored exactly like
            // the interrogation-booth scripted flow. Falls back to the plain early-days answer
            // for legacy/manual setups with no SuspectCharacter (e.g. scene-placed NPCs).
            SuspectCharacter character = GetComponent<SuspectCharacter>();
            var resolved = new DialogueOption[suspectData.questionResponses.Length];
            for (int i = 0; i < resolved.Length; i++)
            {
                SuspectData.QuestionResponseSet qr = suspectData.questionResponses[i];
                string response = character != null ? character.GetQuestionResponse(i) : null;
                resolved[i] = new DialogueOption
                {
                    playerLine = qr.question,
                    npcResponse = !string.IsNullOrEmpty(response) ? response : qr.earlyDaysAnswer,
                    cameraTrigger = qr.cameraTrigger
                };
            }
            _options = resolved;
        }
        else
        {
            _options = manualOptions;
        }

        _greetingLineForCurrentConversation = greetingLine;
        _greetingCameraKeyForCurrentConversation = string.Empty;
    }

    /// <summary>
    /// Opens the conversation: locks player movement/camera and joins the NPC's world-dialogue
    /// conversation. Multiple players may join the same conversation — the first joiner starts
    /// the authoritative sequence on the server (greeting, then options/response looping until
    /// every participant leaves), later joiners are folded into the existing one (see
    /// <see cref="SpeakingInteraction.RequestBeginEngagement"/>). Safe to call repeatedly —
    /// ignored while already in conversation or while a join request is in flight.
    /// </summary>
    public void BeginConversation()
    {
        if (_inConversation || _awaitingEngagementResponse) return;
        if (IsVerdictClosed()) return;
        // Intentionally NOT gated on DialogueManager.IsSpeaking: that flag is global and stays
        // true for any voice audio (ambient barks, entry lines, a line still finishing after the
        // player backed out), which silently swallowed the Interact press and made players press
        // twice. The greeting's PlayDialogueAudio already stops any in-flight voice audio, and
        // immediate re-entry after exiting is covered by SuspectCharacter's dialogue block window.
        if (speaking == null) return;

        // Local pre-check only, so a misconfigured NPC (no options authored) never locks the
        // player's movement/camera waiting on a conversation that will never show anything. The
        // server independently (and authoritatively) re-resolves the same data in
        // ServerRunConversationLoop once the conversation actually starts.
        ResolveOptionsForConversation();
        if (_options == null || _options.Length == 0) return;

        _awaitingEngagementResponse = true;
        _cancelPendingEngagement = false;
        speaking.RequestBeginEngagement(OnEngagementResponse);
    }

    private void OnEngagementResponse(bool granted)
    {
        _awaitingEngagementResponse = false;

        // A forced exit (e.g. the intro cutscene starting) landed while this join was in flight.
        if (_cancelPendingEngagement)
        {
            _cancelPendingEngagement = false;
            if (granted)
                speaking?.EndEngagement();
            return;
        }

        if (!granted)
        {
            // No longer expected — joining a world-dialogue conversation is always granted now
            // that multiple participants are supported — but guard defensively in case a future
            // caller reintroduces a rejection path.
            Debug.LogWarning($"[SuspectWorldDialogue] Engagement with '{name}' was unexpectedly denied.");
            return;
        }

        // A verdict landed while this client's join request was in flight — back straight out
        // of the engagement instead of opening a conversation with a closed-out suspect.
        if (IsVerdictClosed())
        {
            speaking?.EndEngagement();
            return;
        }

        StartConversation();
    }

    /// <summary>
    /// True when this NPC is a suspect closed out from dialogue — a verdict has been delivered,
    /// or they have turned into an active full mutant.
    /// </summary>
    private bool IsVerdictClosed()
    {
        SuspectCharacter suspect = GetComponent<SuspectCharacter>();
        return suspect != null && (suspect.IsVerdictClosed || suspect.IsMutantInteractionClosed);
    }

    private void StartConversation()
    {
        _inConversation = true;

        UIController.Instance.ClosePlayerUI();

        Transform lookTarget = DialogueLookTarget;

        // Booth suspects talked to from inside the booth use the booth dialogue mode, which
        // activates the suspect cam (SuspectController.SetSuspectCamActive). Everyone else —
        // scene-placed NPCs and outside players — keeps the player-camera dolly-in.
        _boothCameraMode = IsBoothConversationForLocalPlayer();
        if (_boothCameraMode)
            DialogueChoiceSystem.Instance.EnterScriptedDialogueMode(transform);
        else
            DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(lookTarget, dialogueCameraVerticalOffset);

        // Cut to the default dialogue camera immediately; per-line triggers follow via
        // HandleWorldLineCamera as the server plays each line.
        ApplyLineCamera(string.Empty);

        if (lookAnimator != null)
        {
            _previousObjectToFollow = lookAnimator.ObjectToFollow;
            _restoreObjectToFollow = true;

            // Track the rendering camera (CinemachineBrain), not the player's head vcam: in booth
            // mode the brain blends onto the suspect cam, so the suspect must follow Camera.main.
            Camera mainCamera = Camera.main;
            Transform suspectLookTarget = mainCamera != null ? mainCamera.transform
                : (PlayerInstance.Instance != null ? PlayerInstance.Instance.CameraTransform : null);
            if (suspectLookTarget != null)
                lookAnimator.ObjectToFollow = suspectLookTarget;
        }

        UIController.Instance.ShowBackButton(EndConversation);

        // Presentation only — whatever the conversation is currently showing (the greeting, a
        // response line, or the choice panel) reaches this client via the server's broadcasts
        // (see HandleWorldChoicesShown / HandleWorldAdvanceGateChanged), which also drive any
        // other already-joined participant identically.
    }

    // -------------------------------------------------------------------------
    // Server-only: authoritative conversation sequencing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. Begins the authoritative world-dialogue conversation loop for this NPC.
    /// Called by <see cref="SpeakingInteraction"/> when the first participant joins. Resolves
    /// this day's options/greeting once, then repeatedly shows the option panel to every current
    /// participant, resolves their pick via <see cref="SpeakingInteraction.ServerBeginWorldChoiceVote"/>
    /// (the same voting rule <see cref="ScriptedDialogueRunner"/> uses for choice nodes), and
    /// plays the NPC's response — until every participant has left the conversation.
    /// </summary>
    public void ServerStartConversation()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (_serverConversationRunning) return;

        _serverConversationRunning = true;
        StartCoroutine(ServerRunConversationLoop());
    }

    private IEnumerator ServerRunConversationLoop()
    {
        // Flush the engagement-grant RPC before broadcasting the first line, so the joining
        // client has already entered conversation presentation (see StartConversation) by the
        // time it arrives — mirrors ScriptedDialogueRunner's "flush RPCs before the first line".
        yield return null;

        ResolveOptionsForConversation();

        if (speaking == null || _options == null || _options.Length == 0)
        {
            _serverConversationRunning = false;
            yield break;
        }

        if (!string.IsNullOrEmpty(_greetingLineForCurrentConversation))
            yield return StartCoroutine(ServerSayLineAndWaitForAdvance(_greetingLineForCurrentConversation, _greetingCameraKeyForCurrentConversation));

        while (speaking.HasWorldParticipants)
        {
            int chosenIndex = 0;
            bool resolved = false;
            speaking.ServerBeginWorldChoiceVote(BuildOptionTexts(), idx => { chosenIndex = idx; resolved = true; });
            yield return new WaitUntil(() => resolved);

            if (!speaking.HasWorldParticipants) break;

            DialogueOption chosen = _options[Mathf.Clamp(chosenIndex, 0, _options.Length - 1)];

            // Flush the FinalizeWorldChoiceClientRpc (which shows the choice echo) before firing
            // the NPC response's own SayWorldDialogueClientRpc — mirrors
            // ScriptedDialogueRunner.PlayChoiceNode's identical "flush the RPC" yield, since two
            // ClientRpcs fired in the same server frame can otherwise be delivered out of order.
            yield return null;

            if (animator != null && !string.IsNullOrEmpty(chosen.animationTrigger))
                animator.SetTrigger(chosen.animationTrigger);

            yield return StartCoroutine(ServerSayLineAndWaitForAdvance(chosen.npcResponse, chosen.cameraTrigger));
        }

        _serverConversationRunning = false;
    }

    private IEnumerator ServerSayLineAndWaitForAdvance(string line, string cameraKey = "")
    {
        speaking.SayWorldDialogue(line, waitForInput: true, cameraKey: cameraKey ?? string.Empty);

        bool advanced = false;
        speaking.ServerBeginWorldAdvanceGate(() => advanced = true);
        yield return new WaitUntil(() => advanced);
    }

    private string[] BuildOptionTexts()
    {
        string[] texts = new string[_options.Length];
        for (int i = 0; i < _options.Length; i++)
            texts[i] = _options[i].playerLine;
        return texts;
    }

    // -------------------------------------------------------------------------
    // Local presentation — driven by SpeakingInteraction's broadcasts
    // -------------------------------------------------------------------------

    private void HandleWorldChoicesShown(string[] texts)
    {
        if (!_inConversation) return;
        _state = ConversationState.WaitingForChoice;

        // Return to the default dialogue camera while the player picks the next question.
        ApplyLineCamera(string.Empty);
        DialogueChoiceSystem.Instance.ShowScriptedChoices(texts, OnOptionChosen);
    }

    private void HandleWorldLineCamera(string cameraKey)
    {
        if (!_inConversation) return;
        ApplyLineCamera(cameraKey);
    }

    // -------------------------------------------------------------------------
    // Local presentation — dialogue cameras
    // -------------------------------------------------------------------------

    /// <summary>
    /// True when the local player is inside the booth and this NPC is the booth's current
    /// suspect — the conversation then uses booth dialogue mode and its suspect cam.
    /// </summary>
    private bool IsBoothConversationForLocalPlayer()
    {
        SuspectCharacter character = GetComponent<SuspectCharacter>();
        return character != null &&
               SuspectController.Instance != null &&
               SuspectController.Instance.CurrentSuspect == character &&
               PlayerInstance.Instance != null &&
               !PlayerInstance.Instance.IsOutsideLocal;
    }

    /// <summary>
    /// Cuts to the camera for <paramref name="cameraKey"/>, deactivating the previous line camera.
    /// Keys match <see cref="ScriptedDialogueNode.cameraTrigger"/>: empty = default,
    /// <c>SuspectCam</c>, <c>SuspectFaceCam</c> / <c>suspect face</c>, or a
    /// <see cref="ScriptedDialogueRunner"/> registry key.
    /// </summary>
    private void ApplyLineCamera(string cameraKey)
    {
        GameObject target = ResolveLineCamera(cameraKey);
        if (target == _activeLineCam) return;

        if (_activeLineCam != null)
            _activeLineCam.SetActive(false);

        _activeLineCam = target;

        if (_activeLineCam != null)
            _activeLineCam.SetActive(true);
    }

    private GameObject ResolveLineCamera(string cameraKey)
    {
        bool isSuspectCamKey = string.IsNullOrEmpty(cameraKey) || cameraKey == "SuspectCam" ||
                               cameraKey == "SuspectFaceCam" || cameraKey == "suspect face";

        // World NPCs (and players outside the booth) keep the player-camera dolly-in — the
        // per-character suspect cams are booth-only. Registry keys still apply if authored.
        if (!_boothCameraMode && isSuspectCamKey)
            return null;

        SuspectCharacter character = GetComponent<SuspectCharacter>();
        GameObject wideCam = character != null ? character.SuspectCam : null;
        GameObject resolved;

        if (string.IsNullOrEmpty(cameraKey) || cameraKey == "SuspectCam")
        {
            // Booth base shot, already held open by SuspectController — filtered to null below.
            resolved = wideCam;
        }
        else if (cameraKey == "SuspectFaceCam" || cameraKey == "suspect face")
        {
            // Same rule as ScriptedDialogueRunner: intact booth glass would obscure the close-up,
            // so use the wide shot until the window is broken.
            bool glassIntact = _boothCameraMode &&
                               BreakableGlassController.Instance != null &&
                               BreakableGlassController.Instance.IsWindowVisible;
            GameObject faceCam = character != null ? character.SuspectFaceCam : null;
            resolved = (!glassIntact && faceCam != null) ? faceCam : wideCam;

            if (resolved == null)
                Debug.LogWarning($"[SuspectWorldDialogue] Camera key '{cameraKey}': '{name}' has no camera assigned.", this);
        }
        else
        {
            resolved = ScriptedDialogueRunner.Instance != null
                ? ScriptedDialogueRunner.Instance.GetRegisteredCamera(cameraKey)
                : null;

            if (resolved == null)
                Debug.LogWarning($"[SuspectWorldDialogue] No camera entry found for key '{cameraKey}'.", this);
        }

        // In booth mode the suspect's wide cam is already held open by SuspectController as the
        // base shot — never adopt it as a line camera, or deactivating the line cam would kill it.
        if (_boothCameraMode && resolved != null && resolved == wideCam)
            return null;

        return resolved;
    }

    private void DeactivateLineCamera()
    {
        if (_activeLineCam != null)
            _activeLineCam.SetActive(false);
        _activeLineCam = null;
    }

    private void OnOptionChosen(int index)
    {
        if (!_inConversation) return;

        // Highlights the pick locally and submits the vote — the panel stays open until the
        // server resolves the vote (see HandleWorldChoiceFinalized), exactly like
        // ScriptedDialogueRunner's choice nodes.
        speaking.SubmitWorldChoicePick(index);
    }

    private void HandleWorldChoiceFinalized()
    {
        // DialogueChoiceSystem's panel/highlights are already cleared by SpeakingInteraction's
        // ClientRpc — nothing further needed here. The NPC's response line and the next
        // advance-gate broadcast follow immediately from the server's conversation loop.
    }

    private void HandleWorldAdvanceGateChanged(bool awaiting)
    {
        if (!_inConversation) return;
        _state = awaiting ? ConversationState.WaitingForAdvance : ConversationState.Idle;
    }

    private void Update()
    {
        if (!_inConversation) return;
        if (_state != ConversationState.WaitingForAdvance) return;
        if (DialogueManager.Instance == null) return;

        bool pressedAdvance = GoodCopBadCop.Input.RebindableInput.GetKeyDown(GoodCopBadCop.Input.GameAction.Interact)
                               || (Input.GetMouseButtonDown(0) && !IsPointerOverInteractableUI())
                               || AnyGamepadAdvanceButtonThisFrame();
        if (!pressedAdvance) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // First input: complete the typewriter locally without advancing the line.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second input (or first when the typewriter already finished): vote to advance. The
        // gate only opens — clearing the subtitle and moving on to the next line/options — once
        // every current participant has voted, or the timeout fires (see
        // SpeakingInteraction.SubmitWorldAdvance). The vote is tagged with this line's gate id so
        // it can never be counted against the next line if it arrives late.
        // Close this player's own spoken-choice echo immediately. The shared NPC response still
        // waits for every participant's advance vote (or its timeout) before progressing.
        DialogueManager.Instance.DismissOwnChoiceEchoOnAdvance();
        speaking.SubmitWorldAdvance();
    }

    private static readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();

    /// <summary>
    /// Unlike <see cref="EventSystem.IsPointerOverGameObject()"/>, this only reports true when the
    /// pointer is over an actual interactive control (e.g. the Back button or a choice button),
    /// not any raycast-target graphic. This keeps "click anywhere to skip" working over the
    /// subtitle/backdrop while still letting the Back button (visible the whole conversation)
    /// receive its own click uncontested instead of also being read as an advance/skip input.
    /// </summary>
    private static bool IsPointerOverInteractableUI()
    {
        if (EventSystem.current == null) return false;

        PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach (RaycastResult result in _uiRaycastResults)
        {
            if (result.gameObject == null) continue;
            Selectable selectable = result.gameObject.GetComponentInParent<Selectable>();
            if (selectable != null && selectable.interactable) return true;
        }

        return false;
    }

    private IEnumerator RestoreGameplayCursorAfterExit()
    {
        // Escape invokes the Back button during this frame, and Unity can release a locked
        // cursor as part of handling that same key press. Reapply the gameplay cursor state
        // on the following frame, unless another dialogue or scripted sequence took over.
        yield return null;

        if (!DialogueChoiceSystem.IsInDialogueMode && !ScriptedDialogueRunner.IsScriptedModeActive)
            UIController.Instance?.HideCursor();
    }

    /// <summary>
    /// Returns true if any gamepad button other than East (B) was pressed this frame. Used to
    /// advance/skip the NPC's greeting or response line with a controller, mirroring the E-key /
    /// LMB advance. East is excluded — it's reserved for exiting the conversation via the Back
    /// button (see UIController's centralized Back-button handling) and must not also advance
    /// the line on the same press.
    /// </summary>
    private static bool AnyGamepadAdvanceButtonThisFrame()
    {
        Gamepad gp = Gamepad.current;
        if (gp == null) return false;
        return gp.buttonSouth.wasPressedThisFrame
            || gp.buttonNorth.wasPressedThisFrame
            || gp.buttonWest.wasPressedThisFrame
            || gp.leftShoulder.wasPressedThisFrame
            || gp.rightShoulder.wasPressedThisFrame
            || gp.leftTrigger.wasPressedThisFrame
            || gp.rightTrigger.wasPressedThisFrame
            || gp.leftStickButton.wasPressedThisFrame
            || gp.rightStickButton.wasPressedThisFrame
            || gp.dpad.up.wasPressedThisFrame
            || gp.dpad.down.wasPressedThisFrame
            || gp.dpad.left.wasPressedThisFrame
            || gp.dpad.right.wasPressedThisFrame;
    }

    /// <summary>
    /// Leaves the conversation at any point: leaves the NPC's world-dialogue participant set,
    /// hides the choice panel and back button, clears any active subtitle, and restores normal
    /// player control. The other participant (if any) is unaffected and continues the
    /// conversation on their own — the server's advance/choice gates immediately re-evaluate
    /// against the shrunken participant set (see <see cref="SpeakingInteraction"/>). Wired to the
    /// Back button shown in <see cref="BeginConversation"/>.
    /// </summary>
    public void EndConversation()
    {
        if (!_inConversation) return;
        _inConversation = false;
        _state = ConversationState.Idle;

        // This is a direct suspect conversation and may not be the booth's CurrentSuspect.
        GetComponent<SuspectCharacter>()?.BlockDialogueInteractionForOneSecond();

        speaking?.EndEngagement();

        UIController.Instance.HideBackButton();
        DialogueChoiceSystem.Instance.HideChoicePanel();
        DialogueManager.Instance?.ClearHistory();
        speaking?.HideWorldDialogueSubtitle();

        DeactivateLineCamera();
        if (_boothCameraMode)
            DialogueChoiceSystem.Instance.ExitScriptedDialogueMode(); // also deactivates the booth suspect cam
        else
            DialogueChoiceSystem.Instance.ExitScriptedDialogueModeOutside();
        _boothCameraMode = false;

        UIController.Instance.ShowPlayerUI();
        StartCoroutine(RestoreGameplayCursorAfterExit());

        if (_restoreObjectToFollow && lookAnimator != null)
        {
            lookAnimator.ObjectToFollow = _previousObjectToFollow;
            _restoreObjectToFollow = false;
        }
    }
}
