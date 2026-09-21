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
             "bone (LeadBone) on entering conversation, and the suspect looks back at the " +
             "player's camera for the duration of the conversation.")]
    [SerializeField] private FLookAnimator lookAnimator;

    [Tooltip("Fine-tunes the dialogue camera dolly-in framing height (meters, +up/-down) for this " +
             "specific character. Taller characters typically want a small negative value to pull " +
             "the shot back down toward their upper body; shorter ones may want a small positive value.")]
    [SerializeField] private float dialogueCameraVerticalOffset = 0f;

    /// <summary>Exposes <see cref="dialogueCameraVerticalOffset"/> for callers that resolve this NPC's
    /// look target generically (e.g. <see cref="ScriptedDialogueRunner"/>) and need to pass the same
    /// per-character framing tune-up through to <see cref="DialogueChoiceSystem.EnterScriptedDialogueModeOutside"/>.</summary>
    public float DialogueCameraVerticalOffset => dialogueCameraVerticalOffset;

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
    private bool _inConversation;
    private ConversationState _state = ConversationState.Idle;
    private Transform _previousObjectToFollow;
    private bool _restoreObjectToFollow;
    private bool _awaitingEngagementResponse;

    // Server-only: guards against starting the authoritative conversation loop twice.
    private bool _serverConversationRunning;

    public bool InConversation => _inConversation;

    private void Awake()
    {
        if (startSitting && animator != null)
            animator.SetBool("Sitting", true);

        SubscribeToSpeaking();
    }

    private void OnDestroy()
    {
        UnsubscribeFromSpeaking();
    }

    private void SubscribeToSpeaking()
    {
        if (speaking == null) return;
        speaking.OnWorldChoicesShown += HandleWorldChoicesShown;
        speaking.OnWorldChoiceFinalized += HandleWorldChoiceFinalized;
        speaking.OnWorldAdvanceGateChanged += HandleWorldAdvanceGateChanged;
    }

    private void UnsubscribeFromSpeaking()
    {
        if (speaking == null) return;
        speaking.OnWorldChoicesShown -= HandleWorldChoicesShown;
        speaking.OnWorldChoiceFinalized -= HandleWorldChoiceFinalized;
        speaking.OnWorldAdvanceGateChanged -= HandleWorldAdvanceGateChanged;
    }

    /// <summary>
    /// Configures this component at runtime — used when a scripted sequence dynamically attaches
    /// a world dialogue conversation to a suspect once their scripted task is complete (e.g.
    /// Day_02 handing a suspect off to sit in the yard instead of despawning them). Prefer
    /// authoring <see cref="daySets"/> in the Inspector for scene-placed suspects; use this only
    /// for prefab instances spawned purely at runtime.
    /// </summary>
    public void Configure(SpeakingInteraction speakingRef, Animator animatorRef, DaySet[] sets, bool startSittingNow = true, FLookAnimator lookAnimatorRef = null)
    {
        UnsubscribeFromSpeaking();
        speaking = speakingRef;
        animator = animatorRef;
        daySets = sets;
        startSitting = startSittingNow;
        lookAnimator = lookAnimatorRef;
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
            return;
        }

        if (suspectData != null && suspectData.questionResponses != null && suspectData.questionResponses.Length > 0)
        {
            var resolved = new DialogueOption[suspectData.questionResponses.Length];
            for (int i = 0; i < resolved.Length; i++)
            {
                SuspectData.QuestionResponseSet qr = suspectData.questionResponses[i];
                resolved[i] = new DialogueOption
                {
                    playerLine = qr.question,
                    npcResponse = qr.earlyDaysAnswer
                };
            }
            _options = resolved;
        }
        else
        {
            _options = manualOptions;
        }

        _greetingLineForCurrentConversation = greetingLine;
    }

    /// <summary>
    /// Opens the conversation: locks player movement/camera and joins the NPC's world-dialogue
    /// conversation. Multiple players may join the same conversation — the first joiner starts
    /// the authoritative sequence on the server (greeting, then options/response looping until
    /// every participant leaves), later joiners are folded into the existing one (see
    /// <see cref="SpeakingInteraction.RequestBeginEngagement"/>). Safe to call repeatedly —
    /// ignored while already in conversation or while the NPC is mid-line.
    /// </summary>
    public void BeginConversation()
    {
        if (_inConversation || _awaitingEngagementResponse) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking) return;
        if (speaking == null) return;

        // Local pre-check only, so a misconfigured NPC (no options authored) never locks the
        // player's movement/camera waiting on a conversation that will never show anything. The
        // server independently (and authoritatively) re-resolves the same data in
        // ServerRunConversationLoop once the conversation actually starts.
        ResolveOptionsForConversation();
        if (_options == null || _options.Length == 0) return;

        _awaitingEngagementResponse = true;
        speaking.RequestBeginEngagement(OnEngagementResponse);
    }

    private void OnEngagementResponse(bool granted)
    {
        _awaitingEngagementResponse = false;

        if (!granted)
        {
            // No longer expected — joining a world-dialogue conversation is always granted now
            // that multiple participants are supported — but guard defensively in case a future
            // caller reintroduces a rejection path.
            Debug.LogWarning($"[SuspectWorldDialogue] Engagement with '{name}' was unexpectedly denied.");
            return;
        }

        StartConversation();
    }

    private void StartConversation()
    {
        _inConversation = true;

        UIController.Instance.ClosePlayerUI();

        Transform headBone = lookAnimator != null && lookAnimator.LeadBone != null ? lookAnimator.LeadBone : null;
        Transform lookTarget = headBone != null ? headBone
            : (speaking != null && speaking.LookTarget != null ? speaking.LookTarget : transform);
        DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(lookTarget, dialogueCameraVerticalOffset);

        if (lookAnimator != null)
        {
            _previousObjectToFollow = lookAnimator.ObjectToFollow;
            _restoreObjectToFollow = true;

            Transform playerCamera = PlayerInstance.Instance != null ? PlayerInstance.Instance.CameraTransform : null;
            if (playerCamera != null)
                lookAnimator.ObjectToFollow = playerCamera;
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
            yield return StartCoroutine(ServerSayLineAndWaitForAdvance(_greetingLineForCurrentConversation));

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

            yield return StartCoroutine(ServerSayLineAndWaitForAdvance(chosen.npcResponse));
        }

        _serverConversationRunning = false;
    }

    private IEnumerator ServerSayLineAndWaitForAdvance(string line)
    {
        speaking.SayWorldDialogue(line, waitForInput: true);

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
        DialogueChoiceSystem.Instance.ShowScriptedChoices(texts, OnOptionChosen);
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

        bool pressedAdvance = Input.GetKeyDown(KeyCode.E)
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
        // SpeakingInteraction.SubmitWorldAdvanceServerRpc).
        // Close this player's own spoken-choice echo immediately. The shared NPC response still
        // waits for every participant's advance vote (or its timeout) before progressing.
        DialogueManager.Instance.DismissOwnChoiceEchoOnAdvance();
        speaking.SubmitWorldAdvanceServerRpc();
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

        speaking?.EndEngagement();

        UIController.Instance.HideBackButton();
        DialogueChoiceSystem.Instance.HideChoicePanel();
        DialogueManager.Instance?.ClearHistory();
        speaking?.HideWorldDialogueSubtitle();
        DialogueChoiceSystem.Instance.ExitScriptedDialogueModeOutside();
        UIController.Instance.ShowPlayerUI();
        StartCoroutine(RestoreGameplayCursorAfterExit());

        if (_restoreObjectToFollow && lookAnimator != null)
        {
            lookAnimator.ObjectToFollow = _previousObjectToFollow;
            _restoreObjectToFollow = false;
        }
    }
}
