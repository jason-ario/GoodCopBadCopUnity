using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Handles all voiced megaphone barks and on-screen dialogue text for the campaign.
/// Combines the bark system (animated speaker + audio + canvas text) and the simple
/// UI text overlay into a single manager.
///
/// Scripted sequences are exposed as public methods and can be called directly or
/// triggered by subscribing to CampaignManager.OnTutorialStepRequested.
///
/// Set <see cref="disabled"/> to true in the Inspector or via debug tooling to suppress all output.
/// </summary>
public class MegaphoneDialogueManager : MonoBehaviour
{
    public static MegaphoneDialogueManager Instance;

    [Header("Bark Canvas")]
    [SerializeField] private GameObject _barkCanvas;
    [SerializeField] private TextMeshProUGUI _barkText;
    [SerializeField] private Animator _speakerAnimator;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClips;

    [Header("Settings")]
    [Tooltip("When true, all dialogue output is suppressed.")]
    public bool disabled;

    private static readonly int SpeakingParam = Animator.StringToHash("Speaking");
    private const float PostSpeakHideDuration = 3f;

    private bool _isSpeaking;
    private Coroutine _hideCoroutine;

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _barkCanvas.SetActive(false);

        ShiftManager.Instance.OnShiftStart += OnShiftStart;
        ShiftManager.Instance.OnShiftReady += OnShiftReady;
        GameManager.Instance.OnGameStart += OnGameStart;

        CampaignManager.OnTutorialStepRequested += HandleTutorialStep;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart -= OnShiftStart;
            ShiftManager.Instance.OnShiftReady -= OnShiftReady;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;

        CampaignManager.OnTutorialStepRequested -= HandleTutorialStep;
    }

    // ---------------------------------------------------------------------------
    // Gameplay Event Hooks
    // ---------------------------------------------------------------------------

    private void OnGameStart()
    {
        StartCoroutine(GameStartBarkCoroutine());
    }

    private IEnumerator GameStartBarkCoroutine()
    {
        yield return new WaitForSeconds(12f);

        if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutside)
            ShowDialogue("All inspectors please report to duty.");
    }

    private void OnShiftStart()
    {
        if (!SaveDataManager.Instance.HasSeenIntroTutorial)
            StartCoroutine(Day1WelcomeBarkSequence());
    }

    private IEnumerator Day1WelcomeBarkSequence()
    {
        if (disabled) yield break;

        yield return new WaitForSeconds(7f);
        ShowDialogue("Good morning, sunshine.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("Welcome to your first day on the job...");
        yield return new WaitForSeconds(5f);
        ShowDialogue("We've been waiting for you.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("The last guy didn't last very long. We're hoping you can do better.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("Judging by the looks of you, I give you a week, tops.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("But to give you the best shot, I'll be here to help out.");
    }

    private void OnShiftReady()
    {
        ShowDialogue("All tasks completed, return to the booth for the next shift.");
    }

    // ---------------------------------------------------------------------------
    // Scripted Sequences (called by ShiftManager or CampaignManager)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Plays when the end-of-shift report is dismissed.
    /// Delivers the end-of-shift barks and then triggers the night phase on ShiftManager
    /// after the task announcement, preserving the original deferred timing.
    /// </summary>
    public void SayEndOfShiftDialogue()
    {
        StartCoroutine(EndOfShiftDialogueSequence());
    }

    private IEnumerator EndOfShiftDialogueSequence()
    {
        ShowDialogue("Your shift is over.");
        yield return new WaitUntil(() => !_isSpeaking);
        yield return new WaitForSeconds(3f);

        ShowDialogue("Complete your tasks to prepare for your next shift.");
        yield return new WaitUntil(() => !_isSpeaking);
        yield return new WaitForSeconds(2f);

        AnnounceNewTask();
    }

    /// <summary>
    /// Shows the new task notification via PlayerTutorialUI, then triggers the night phase
    /// after a short delay so the guidebook badge activates in sync with the bark.
    /// </summary>
    private void AnnounceNewTask()
    {
        StartCoroutine(AnnounceNewTaskCoroutine());
    }

    private IEnumerator AnnounceNewTaskCoroutine()
    {
        if (PlayerTutorialUI.Instance != null)
            PlayerTutorialUI.Instance.ShowTextOnly("New task: Take out the trash.");

        yield return new WaitForSeconds(4f);

        ShiftManager.Instance.TriggerBeginNightPhase();
    }

    /// <summary>Played when between-shift tasks are all done and the booth is ready.</summary>
    public void SayBetweenShiftReady()
    {
        ShowDialogue("You may now prepare for your next shift.");
    }

    /// <summary>Played as an immediate prompt to start the next shift.</summary>
    public void SayBeginShiftNow()
    {
        ShowDialogue("Begin your next shift immediately.");
    }

    /// <summary>Shown when a player tries to leave the booth during a locked shift.</summary>
    public void SayDoorLocked(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        ShowDialogue(lines[UnityEngine.Random.Range(0, lines.Length)]);
    }

    /// <summary>Shown when the shift clock-out is ready.</summary>
    public void SayClockOutReady()
    {
        ShowDialogue("Your shift is over. Clock out to end the day.");
    }

    /// <summary>Shown when not all players are inside the booth at shift start.</summary>
    public void SayNotAllInside()
    {
        ShowDialogue("All inspectors must be inside the booth to begin the shift.");
    }

    // ---------------------------------------------------------------------------
    // Tutorial Step Handler
    // ---------------------------------------------------------------------------

    private void HandleTutorialStep(TutorialStep step)
    {
        // Wire campaign tutorial steps to barks here as the tutorial system is built out.
        // Each case maps a TutorialStep enum value to a bark sequence or UI trigger.
        switch (step)
        {
            case TutorialStep.IntroDay1:
                // Handled via OnShiftStart → Day1WelcomeBarkSequence on the first day.
                break;
            case TutorialStep.NightTasksExplained:
                SayBetweenShiftReady();
                break;
        }
    }

    // ---------------------------------------------------------------------------
    // Core Bark Display
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Displays a voiced bark on the megaphone canvas. Ignored if already speaking.
    /// Audio is routed through DialogueManager for lip-sync and playback management.
    /// </summary>
    public void ShowDialogue(string text)
    {
        if (disabled || _isSpeaking) return;
        StartCoroutine(ShowBarkSequence(text));
    }

    /// <summary>
    /// Displays plain text on the bark canvas without audio. Useful for lightweight prompts.
    /// Ignored if already speaking.
    /// </summary>
    public void ShowTextOnly(string text)
    {
        if (disabled || _isSpeaking) return;

        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _barkText.text = text;
        _barkCanvas.SetActive(true);
        _hideCoroutine = StartCoroutine(WaitAndHide());
    }

    /// <summary>Immediately hides the bark canvas.</summary>
    public void HideDialogue()
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _barkCanvas.SetActive(false);
        _isSpeaking = false;

        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, false);
    }

    // ---------------------------------------------------------------------------
    // Internal Coroutines
    // ---------------------------------------------------------------------------

    private IEnumerator ShowBarkSequence(string text)
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _isSpeaking = true;
        _barkCanvas.SetActive(false);

        yield return new WaitForSeconds(0.4f);

        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, true);

        _barkText.text = text;
        _barkCanvas.SetActive(true);

        DialogueManager.Instance.PlayDialogueAudio(text, _audioClips, _audioSource, OnSpeakingFinished);
    }

    private void OnSpeakingFinished()
    {
        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, false);

        _isSpeaking = false;
        _hideCoroutine = StartCoroutine(WaitAndHide());
    }

    private IEnumerator WaitAndHide()
    {
        yield return new WaitForSeconds(PostSpeakHideDuration);
        _barkCanvas.SetActive(false);
    }
}
