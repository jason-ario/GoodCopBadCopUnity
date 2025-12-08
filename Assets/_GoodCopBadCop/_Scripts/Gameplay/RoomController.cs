using System;
using FIMSpace.FLook;
using UnityEngine;

public class RoomController : MonoBehaviour
{
    [SerializeField] SuspectController suspect;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            suspect.EnableLook();
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            suspect.DisableLook();
        }
    }
}
