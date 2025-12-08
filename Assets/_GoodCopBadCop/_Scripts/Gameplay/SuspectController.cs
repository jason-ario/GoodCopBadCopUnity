using System;
using FIMSpace.FLook;
using UnityEngine;

public class SuspectController : MonoBehaviour
{
    [SerializeField] private FLookAnimator _lookAnimator;

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

