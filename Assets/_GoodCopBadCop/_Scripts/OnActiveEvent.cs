using System;
using UnityEngine;
using UnityEngine.Events;

public class OnActiveEvent : MonoBehaviour
{
    public UnityEvent onActivate;

    private void Awake()
    {
        onActivate.Invoke();
    }
}
