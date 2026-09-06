using System.Collections;
using FIMSpace.FLook;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Adds a simple, direct-interaction conversation to a <see cref="SuspectCharacter"/> that is
/// placed and interactable directly in the world (e.g. the Day 1 Suspect_Soldier), as opposed
/// to the interrogation booth flow which is driven exclusively by <see cref="ScriptedDialogueRunner"/>.
///
/// The player walks up, interacts (LMB / E) and is shown up to 3 dialogue options for the
/// current day (see <see cref="DaySet"/>). Picking an option hides the choices, plays the NPC's
/// unique response as a click-through subtitle (typewriter reveal, skip-to-complete, then
/// advance), and returns to the choice menu — mirroring <see cref="ScriptedDialogueRunner"/>'s
/// line-advance UX. The player can leave the conversation at any time via the Back button.
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

    [Header("Idle State")]
    [Tooltip("When true, sets the Animator's 'Sitting' bool parameter to true on Awake, so this " +
             "NPC starts (and remains, outside of conversation lines) in its sitting idle pose. " +
             "Requires the assigned Animator's controller to have a 'Sitting' bool parameter.")]
    [SerializeField] private bool startSitting = false;

    private enum ConversationState
    {
        Idle,
        ShowingGreeting,
        ShowingOptions,
        ShowingResponse
    }

    private DialogueOption[] _options;
    private string _greetingLineForCurrentConversation;
    private bool _inConversation;
    private ConversationState _state = ConversationState.Idle;
    private Transform _previousObjectToFollow;
    private bool _restoreObjectToFollow;

    public bool InConversation => _inConversation;

    private void Awake()
    {
        if (startSitting && animator != null)
            animator.SetBool("Sitting", true);
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
        speaking = speakingRef;
        animator = animatorRef;
        daySets = sets;
        startSitting = startSittingNow;
        lookAnimator = lookAnimatorRef;
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
    /// Opens the conversation: locks player movement/camera and either plays the day's greeting
    /// line (click-through) before showing options, or shows options immediately if there's no
    /// greeting. Safe to call repeatedly — ignored while already in conversation or while the
    /// NPC is mid-line.
    /// </summary>
    public void BeginConversation()
    {
        if (_inConversation) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking) return;

        ResolveOptionsForConversation();
        if (_options == null || _options.Length == 0) return;

        _inConversation = true;

        UIController.Instance.ClosePlayerUI();

        Transform headBone = lookAnimator != null && lookAnimator.LeadBone != null ? lookAnimator.LeadBone : null;
        Transform lookTarget = headBone != null ? headBone
            : (speaking != null && speaking.LookTarget != null ? speaking.LookTarget : transform);
        DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(lookTarget);

        if (lookAnimator != null)
        {
            _previousObjectToFollow = lookAnimator.ObjectToFollow;
            _restoreObjectToFollow = true;

            Transform playerCamera = PlayerInstance.Instance != null ? PlayerInstance.Instance.CameraTransform : null;
            if (playerCamera != null)
                lookAnimator.ObjectToFollow = playerCamera;
        }

        UIController.Instance.ShowBackButton(EndConversation);

        if (!string.IsNullOrEmpty(_greetingLineForCurrentConversation) && speaking != null)
        {
            _state = ConversationState.ShowingGreeting;
            speaking.Say(_greetingLineForCurrentConversation, waitForInput: true);
        }
        else
        {
            ShowOptions();
        }
    }

    private void ShowOptions()
    {
        _state = ConversationState.ShowingOptions;

        string[] texts = new string[_options.Length];
        for (int i = 0; i < _options.Length; i++)
            texts[i] = _options[i].playerLine;

        DialogueChoiceSystem.Instance.ShowScriptedChoices(texts, OnOptionChosen);
    }

    private void OnOptionChosen(int index)
    {
        if (!_inConversation) return;
        if (index < 0 || index >= _options.Length) return;

        // The scripted-choice callback path does not hide the panel on its own — do it here so
        // the choices disappear the moment a pick is made, before the response plays.
        DialogueChoiceSystem.Instance.HideChoicePanel();

        DialogueOption chosen = _options[index];

        if (animator != null && !string.IsNullOrEmpty(chosen.animationTrigger))
            animator.SetTrigger(chosen.animationTrigger);

        if (speaking != null)
        {
            _state = ConversationState.ShowingResponse;
            speaking.Say(chosen.npcResponse, waitForInput: true);
        }
        else
        {
            ShowOptions();
        }
    }

    private void Update()
    {
        if (!_inConversation) return;
        if (_state != ConversationState.ShowingGreeting && _state != ConversationState.ShowingResponse) return;
        if (DialogueManager.Instance == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool pressedAdvance = Input.GetKeyDown(KeyCode.E)
                               || (Input.GetMouseButtonDown(0) && !overUI)
                               || AnyGamepadAdvanceButtonThisFrame();
        if (!pressedAdvance) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // First input: complete the typewriter locally without advancing the line.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second input (or first when the typewriter already finished): advance past the line.
        ConversationState finishedState = _state;
        _state = ConversationState.Idle;
        DialogueManager.Instance.AdvanceDialogueServerRpc();

        if (!_inConversation) return;

        if (finishedState == ConversationState.ShowingGreeting || finishedState == ConversationState.ShowingResponse)
            ShowOptions();
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
    /// Leaves the conversation at any point: hides the choice panel and back button, clears any
    /// active subtitle, and restores normal player control. Wired to the Back button shown in
    /// <see cref="BeginConversation"/>.
    /// </summary>
    public void EndConversation()
    {
        if (!_inConversation) return;
        _inConversation = false;
        _state = ConversationState.Idle;

        UIController.Instance.HideBackButton();
        DialogueChoiceSystem.Instance.HideChoicePanel();
        DialogueManager.Instance?.ClearHistory();
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
