using System.Collections;
using UnityEngine;

/// <summary>
/// Adds a simple, direct-interaction conversation to a <see cref="SuspectCharacter"/> that is
/// placed and interactable directly in the world (e.g. the Day 1 Suspect_Soldier), as opposed
/// to the interrogation booth flow which is driven exclusively by <see cref="ScriptedDialogueRunner"/>.
///
/// The player walks up, interacts (LMB / E) and is shown up to 3 dialogue options sourced from
/// <see cref="SuspectData.questionResponses"/> (using the Day 1 / early-days answer). Picking an
/// option plays the NPC's unique response and re-shows the options so the player can ask another
/// question, or leave the conversation at any time via the Back button.
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

    [Header("Idle State")]
    [Tooltip("When true, sets the Animator's 'Sitting' bool parameter to true on Awake, so this " +
             "NPC starts (and remains, outside of conversation lines) in its sitting idle pose. " +
             "Requires the assigned Animator's controller to have a 'Sitting' bool parameter.")]
    [SerializeField] private bool startSitting = false;

    /// <summary>Seconds to wait before re-showing choices, matching DialogueChoiceSystem's own pacing.</summary>
    private const float ReshowDelaySeconds = 1f;

    private DialogueOption[] _options;
    private string _greetingLineForCurrentConversation;
    private bool _inConversation;

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
    public void Configure(SpeakingInteraction speakingRef, Animator animatorRef, DaySet[] sets, bool startSittingNow = true)
    {
        speaking = speakingRef;
        animator = animatorRef;
        daySets = sets;
        startSitting = startSittingNow;
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
    /// Opens the conversation: locks player movement/camera, greets the player, and shows the
    /// dialogue options. Safe to call repeatedly — ignored while already in conversation or while
    /// the NPC is mid-line.
    /// </summary>
    public void BeginConversation()
    {
        if (_inConversation) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking) return;

        ResolveOptionsForConversation();
        if (_options == null || _options.Length == 0) return;

        _inConversation = true;

        Transform lookTarget = speaking != null && speaking.LookTarget != null ? speaking.LookTarget : transform;
        DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(lookTarget);

        if (!string.IsNullOrEmpty(_greetingLineForCurrentConversation) && speaking != null)
            speaking.Say(_greetingLineForCurrentConversation);

        ShowOptions();

        UIController.Instance.ShowBackButton(EndConversation);
    }

    private void ShowOptions()
    {
        string[] texts = new string[_options.Length];
        for (int i = 0; i < _options.Length; i++)
            texts[i] = _options[i].playerLine;

        DialogueChoiceSystem.Instance.ShowScriptedChoices(texts, OnOptionChosen);
    }

    private void OnOptionChosen(int index)
    {
        if (!_inConversation) return;
        if (index < 0 || index >= _options.Length) return;

        DialogueOption chosen = _options[index];

        if (animator != null && !string.IsNullOrEmpty(chosen.animationTrigger))
            animator.SetTrigger(chosen.animationTrigger);

        if (speaking != null)
            speaking.Say(chosen.npcResponse);

        StartCoroutine(ReshowOptionsAfterResponse());
    }

    /// <summary>
    /// Waits for the NPC's response subtitle to appear and fully clear before re-showing the
    /// options, mirroring <see cref="DialogueChoiceSystem"/>'s own reshow pacing so the player is
    /// never looking at both the subtitle and the choice panel simultaneously.
    /// </summary>
    private IEnumerator ReshowOptionsAfterResponse()
    {
        yield return new WaitForSeconds(ReshowDelaySeconds);

        if (DialogueManager.Instance != null)
        {
            yield return new WaitUntil(() => DialogueManager.Instance.HasActiveSubtitles);
            yield return new WaitUntil(() => !DialogueManager.Instance.HasActiveSubtitles);
        }

        if (_inConversation)
            ShowOptions();
    }

    /// <summary>
    /// Leaves the conversation at any point: hides the choice panel and back button and restores
    /// normal player control. Wired to the Back button shown in <see cref="BeginConversation"/>.
    /// </summary>
    public void EndConversation()
    {
        if (!_inConversation) return;
        _inConversation = false;

        UIController.Instance.HideBackButton();
        DialogueChoiceSystem.Instance.HideChoicePanel();
        DialogueChoiceSystem.Instance.ExitScriptedDialogueModeOutside();
    }
}
