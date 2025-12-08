using System;
using UnityEngine;
using UnityEngine.Events;

public class LevelSelectController : MonoBehaviour
{
    public static LevelSelectController Instance;
    
    public UnityAction OnLevelSelected;

    private void Awake()
    {
        Instance = this;
    }

    public void ExitLevelSelect()
    {
        UIController.Instance.CloseLevelSelectUI();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }

    public void ChooseLevel()
    {
        OnLevelSelected.Invoke();
    }
}
