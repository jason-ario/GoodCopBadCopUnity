using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    public static MainMenuController Instance;
    
    [Header("Screens")]
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject homeScreen;
    [SerializeField] private GameObject startShiftScreen;
    [SerializeField] private GameObject newLoadCampaignScreen;
    [SerializeField] private GameObject startCampaignScreen;
    [SerializeField] private GameObject joinGameScreen;
    [SerializeField] StartCampaignScreen startCampaignScreenScript;

    [Header("Scene Setup")]
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private GameObject[] chairs;
    [SerializeField] private GameObject _camera;
    [SerializeField] private Transform _camEndPos;
    [SerializeField] WindowLampController windowLampController;
    [SerializeField] private float _timeTillOpenWindow = 8;

    private GameObject _currentScreen;
    private List<GameObject> _allScreens;

    private void Awake()
    {
        Instance = this;
        
        // Initialize the list of screens for easier management
        _allScreens = new List<GameObject> 
        { 
            homeScreen, startShiftScreen, newLoadCampaignScreen, 
            startCampaignScreen, joinGameScreen 
        };
    }

    private void Start()
    {
        UIController.Instance.ClosePlayerUI();
        
        // Initial state: ensure only home screen is active
        SwitchToScreen(homeScreen);
        
        _camera.transform.DOMove(_camEndPos.position, 30);
        StartCoroutine(WaitAndOpenWindow());
    }

    private void SwitchToScreen(GameObject targetScreen)
    {
        if (targetScreen == null) return;

        foreach (var screen in _allScreens)
        {
            screen.SetActive(screen == targetScreen);
        }
        _currentScreen = targetScreen;
    }

    IEnumerator WaitAndOpenWindow()
    {
        yield return new WaitForSeconds(_timeTillOpenWindow);
        GameManager.Instance.OpenWindow();
    }

    public void OpenStartShiftScreen() => SwitchToScreen(startShiftScreen);
    
    public void OpenNewLoadCampaignScreen() => SwitchToScreen(newLoadCampaignScreen);
    
    public void OpenStartCampaignScreen(bool isClient)
    {
        SwitchToScreen(startCampaignScreen);

        if (isClient == false)
        {
            startCampaignScreenScript.StartCampaignAsHost();
        }
        else
        {
            startCampaignScreenScript.OpenAsClient();
        }
    }

    public void OpenJoinLobbyScreen() => SwitchToScreen(joinGameScreen);

    public void BackToHomeScreen() => SwitchToScreen(homeScreen);
    
    public void BackToStartShiftScreen() => SwitchToScreen(startShiftScreen);

    public void StartGame()
    {
        foreach (var chair in chairs)
        {
            chair.SetActive(false);
        }
        
        StopAllCoroutines();
        
        GameManager.Instance.ResetWindow();
        UIController.Instance.ShowPlayerUI();
        
        mainMenu.SetActive(false);
        
        GameManager.Instance.TryStartGame();
    }

    public void HideAllMenus()
    {
        foreach (var screen in _allScreens)
        {
            screen.SetActive(false);
        }
    }
}
