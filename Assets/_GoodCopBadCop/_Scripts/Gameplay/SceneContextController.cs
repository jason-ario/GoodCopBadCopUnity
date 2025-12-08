using System;
using UnityEngine;

public class SceneContextController : MonoBehaviour
{
    public static SceneContextController Instance;

    [SerializeField] Animator rollingShutters;

    private void Awake()
    {
        Instance = this;
    }

    public void OnLevelSelected()
    {
        Debug.Log("Level Selected");
        rollingShutters.SetBool("Open", true);
    }
}
