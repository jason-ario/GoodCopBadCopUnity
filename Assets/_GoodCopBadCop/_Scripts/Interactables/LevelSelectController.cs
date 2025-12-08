using System;
using UnityEngine;

public class LevelSelectController : MonoBehaviour
{
    public void ExitLevelSelect()
    {
        UIController.Instance.CloseLevelSelectUI();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}
