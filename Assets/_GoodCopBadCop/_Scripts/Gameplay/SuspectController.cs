using System;
using FIMSpace.FLook;
using LLMUnity;
using UnityEngine;

public class SuspectController : MonoBehaviour
{
    public static SuspectController Instance;
    [SerializeField] private FLookAnimator _lookAnimator;
    [SerializeField] private LLMCharacter _llmCharacter;
    [TextArea(5,10)]
    [SerializeField] string prompt;

    [SerializeField] private GameObject llmChatController;
    [SerializeField] private SuspectData suspectData;
    
    private void Start()
    {
        DisableLook();
        LoadSuspect(suspectData);
    }

    public void LoadSuspect(SuspectData suspectData)
    {
        _llmCharacter.AIName = suspectData.name;
        string suspectPrompt = "Your name is " + suspectData.name + ". ";
        suspectPrompt += "Your age is " + suspectData.age + ". ";
        suspectPrompt += "Your occupation is " + suspectData.occupation + ". ";
        suspectPrompt += "Your personality is " + suspectData.personality + ". ";
        suspectPrompt += "Your weakness is " + suspectData.weakness + ". "; 
        suspectPrompt += "Your conversation style is " + suspectData.conversationStyle + ". ";
        suspectPrompt += "You are " + (suspectData.isGuilty ? "guilty" : "innocent") + ". ";
        _llmCharacter.prompt = prompt + suspectPrompt;
        llmChatController.SetActive(true);
    }

    public void EnableLook()
    {
        _lookAnimator.ObjectToFollow = Camera.main.transform;
    }
    
    public void DisableLook()
    {
        _lookAnimator.ObjectToFollow = null;
    }
}

