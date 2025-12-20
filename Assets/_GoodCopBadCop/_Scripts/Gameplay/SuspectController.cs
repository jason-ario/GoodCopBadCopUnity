using System;
using FIMSpace.FLook;
using UnityEngine;

public class SuspectController : MonoBehaviour
{
    public static SuspectController Instance;
    [SerializeField] private FLookAnimator _lookAnimator;
    [TextArea(5,10)]
    [SerializeField] string prompt;

    [SerializeField] private GameObject llmChatController;
    [SerializeField] private SuspectData suspectData;
    
    private void Start()
    {
        DisableLook();
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

