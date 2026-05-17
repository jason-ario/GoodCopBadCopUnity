using System;
using System.Collections;
using TMPro;
using UnityEngine;
using Random = System.Random;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance;
    
    [SerializeField] private GameObject tutorialCanvas;
    [SerializeField] private TextMeshProUGUI tutorialText;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] AudioClip[] audioClips;
    [SerializeField] private Animator speakerAnimator;
    private bool isSpeaking;
    Coroutine waitAndHideCoroutine;
    
    public bool disabled;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += ShowStartShiftTutorial;
        GameManager.Instance.OnGameStart += OnGameStart;
        tutorialCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;
    }

    private void OnGameStart()
    {
        StartCoroutine(GameStartBarkCoroutine());
    }

    private IEnumerator GameStartBarkCoroutine()
    {
        yield return new WaitForSeconds(12f);

        if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutsideLocal)
            ShowTutorialText("All inspectors please report to duty.");
    }

    IEnumerator WaitAndSayStartingDialogue()
    {
        yield return new WaitForSeconds(5);
        SayShiftReadyDialogue();
    }

    void SayShiftReadyDialogue()
    {  
        ShowTutorialText("All inspectors report to stations for their shift.");
    }

    /// <summary>Played when the player dismisses the end-of-shift report and enters the night phase.</summary>
    public void SayEndOfShiftDialogue()
    {
        StartCoroutine(EndOfShiftDialogueSequence());
    }

    private IEnumerator EndOfShiftDialogueSequence()
    {
        ShowTutorialText("Your shift is over.");

        // Wait for the first line to finish speaking before queuing the next.
        yield return new WaitUntil(() => !isSpeaking);

        yield return new WaitForSeconds(3f);

        ShowTutorialText("Complete your tasks to prepare for your next shift.");
    }

    /// <summary>Played at the start of the night phase after the end-of-shift report is dismissed.</summary>
    public void SayBetweenShiftReady()
    {
        ShowTutorialText("You may now prepare for your next shift");
    }

    /// <summary>Played when all night-phase tasks are complete and the shift-start button is ready.</summary>
    public void SayAllTasksComplete()
    {
        ShowTutorialText("All tasks completed, return to the booth for the next shift.");
    }

    /// <summary>Played when all night-phase tasks are complete and the shift-start button is ready.</summary>
    public void SayBeginShiftNow()
    {
        ShowTutorialText("Begin your next shift immediately");
    }

    void ShowStartShiftTutorial()
    {
        if (PlayerPrefs.GetInt("Tutorial") == 0)
        {
            StartCoroutine(StartShiftTutorial());
        }
    }

    IEnumerator StartShiftTutorial()
    {
        if (disabled)
        {
            yield break;
        }
        
        yield return new WaitForSeconds(7f);
        ShowTutorialText("Good morning, sunshine.");
        yield return new WaitForSeconds(5f);
        ShowTutorialText("Welcome to your first day on the job...");
        yield return new WaitForSeconds(5f);
        ShowTutorialText("We've been waiting for you.");
        yield return new WaitForSeconds(5f);
        ShowTutorialText("The last guy didn't last very long. We're hoping you can do better.");
        yield return new WaitForSeconds(5f);
        ShowTutorialText("Judging by the looks of you, I give you a week, tops.");
        yield return new WaitForSeconds(5); 
        ShowTutorialText("But to give you the best shot, I'll be here to help out.");
        yield return new WaitForSeconds(5); 
    }

    private void HideTutorialText()
    {
        tutorialCanvas.gameObject.SetActive(false);
    }

    public void ShowTutorialText(string text)
    {
        if (isSpeaking) { return; }
        
        StartCoroutine(ShowTextSequence(text));
    }

    IEnumerator ShowTextSequence(string text)
    {
        if (waitAndHideCoroutine != null)
        {
            StopCoroutine(waitAndHideCoroutine);
        }
        isSpeaking = true;
        tutorialCanvas.gameObject.SetActive(false);
        yield return new WaitForSeconds(.4f);
        speakerAnimator.SetBool("Speaking", true);
        DialogueManager.Instance.PlayDialogueAudio(text, audioClips, audioSource, StopSpeaking);
        tutorialText.text = text;
        tutorialCanvas.gameObject.SetActive(true);
    }

    void StopSpeaking()
    {
        speakerAnimator.SetBool("Speaking", false);
        isSpeaking = false;
        waitAndHideCoroutine = StartCoroutine(WaitAndHideText());
    }
    
    IEnumerator WaitAndHideText()
    {
        yield return new WaitForSeconds(3f);
        HideTutorialText();
    }
}
