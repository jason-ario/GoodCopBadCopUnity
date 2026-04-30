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
        tutorialCanvas.SetActive(false);
        StartCoroutine(WaitAndSayStartingDialogue());
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
