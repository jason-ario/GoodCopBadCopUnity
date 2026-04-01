using System;
using UnityEngine;
using UnityEngine.Events;

public class LevelSelectController : MonoBehaviour
{
    public static LevelSelectController Instance;

    [Header("UI")] 
    [SerializeField] private RectTransform suspectContainer;
    [SerializeField] private CaseFileSelect caseFileSelectPrefab;
    
    private void Awake()
    {
        Instance = this;
        
    }
    public void ExitLevelSelect()
    {
        UIController.Instance.CloseLevelSelectUI();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}
