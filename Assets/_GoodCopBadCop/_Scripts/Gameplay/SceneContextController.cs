using System;
using UnityEngine;

public class SceneContextController : MonoBehaviour
{
    [SerializeField] Animator rollingShutters;

    public void OnLevelSelected()
    {
        Debug.Log("Level Selected");
        rollingShutters.SetBool("Open", true);
    }
}
