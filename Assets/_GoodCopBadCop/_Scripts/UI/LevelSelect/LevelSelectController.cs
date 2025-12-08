using System;
using UnityEngine;
using UnityEngine.Events;

public class LevelSelectController : MonoBehaviour
{
    public static LevelSelectController Instance;
    
    private void Awake()
    {
        Instance = this;
    }

    public void ExitLevelSelect()
    {
        UIController.Instance.CloseLevelSelectUI();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }

    public void StartInterrogation()
    {
        SceneContextController.Instance.OnLevelSelected(); 
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
        UIController.Instance.CloseLevelSelectUI();
    }
}
