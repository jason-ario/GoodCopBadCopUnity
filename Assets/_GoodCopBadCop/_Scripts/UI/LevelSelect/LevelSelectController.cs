using System;
using UnityEngine;
using UnityEngine.Events;

public class LevelSelectController : MonoBehaviour
{
    public static LevelSelectController Instance;
    [SerializeField] SuspectData[] suspects;

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

    public void StartInterrogation(SuspectData suspect)
    {
        SceneContextController.Instance.OnLevelSelected(); 
        SuspectController.Instance.LoadSuspect(suspect);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
        UIController.Instance.CloseLevelSelectUI();
    }
}
