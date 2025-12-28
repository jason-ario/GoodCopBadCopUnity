using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    public static MainMenuController Instance;

    [Header("Screens")]
    [SerializeField] public GameObject mainMenu;
    [SerializeField] private GameObject homeScreen;
    [SerializeField] private GameObject startShiftScreen;
    [SerializeField] private GameObject newLoadCampaignScreen;
    [SerializeField] private GameObject startCampaignScreen;
    [SerializeField] private StartCampaignScreen startCampaignScreenScript;
    [SerializeField] private GameObject joinGameScreen;
    [SerializeField] private Animator screenFade;
 
    [Header("Scene Setup")]
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private GameObject[] chairs;
    [SerializeField] private GameObject sceneCamera;
    [SerializeField] private Transform camEndPos;
    [SerializeField] private WindowLampController windowLampController;
    [SerializeField] private float timeTillOpenWindow = 8f;

    private GameObject currentScreen;
    private List<GameObject> allScreens;
    [SerializeField] public CanvasGroup canvasGroup;

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        allScreens = new List<GameObject>
        {
            homeScreen,
            startShiftScreen,
            newLoadCampaignScreen,
            startCampaignScreen,
            joinGameScreen
        };
    }
    
    private void Start()
    {
        UIController.Instance.ClosePlayerUI();

        SwitchToScreen(homeScreen);
        
        sceneCamera.transform.DOMove(camEndPos.position, 30f);

        StartCoroutine(WaitAndOpenWindow());
    }

    #endregion

    #region Screen Switching

    private void SwitchToScreen(GameObject target)
    {
        if (target == null)
            return;

        foreach (var screen in allScreens)
            screen.SetActive(screen == target);

        currentScreen = target;
    }

    public void OpenStartShiftScreen() =>
        SwitchToScreen(startShiftScreen);

    public void OpenNewLoadCampaignScreen() =>
        SwitchToScreen(newLoadCampaignScreen);

    public void OpenJoinLobbyScreen() =>
        SwitchToScreen(joinGameScreen);

    public void BackToHomeScreen() =>
        SwitchToScreen(homeScreen);

    public void BackToStartShiftScreen() =>
        SwitchToScreen(startShiftScreen);

    #endregion

    #region Multiplayer Entry Points (UI → LobbyManager)

    /// <summary>
    /// Host flow: start server + create lobby
    /// </summary>
    public void OpenStartCampaignAsHost()
    {
        SwitchToScreen(startCampaignScreen);
        startCampaignScreenScript.StartCampaignAsHost();
    }

    /// <summary>
    /// Client flow: waiting screen (actual join triggered elsewhere)
    /// </summary>
    public void OpenStartCampaignAsClient()
    {
        SwitchToScreen(startCampaignScreen);
    }

    #endregion

    #region Game Start

    public void StartGame()
    {
        StopAllCoroutines();
        
        GameManager.Instance.TryStartGame();
    }

    public void HideAllMenus()
    {
        foreach (var screen in allScreens)
            screen.SetActive(false);
    }

    #endregion

    #region Scene Effects

    private IEnumerator WaitAndOpenWindow()
    {
        yield return new WaitForSeconds(timeTillOpenWindow);
        GameManager.Instance.OpenWindow();
    }

    #endregion

    public void TransitionToGameplay()
    {
        foreach (var chair in chairs)
        {
            chair.SetActive(false);
        }
        
        mainMenu.SetActive(false);
        
        HideAllMenus();
    }
}
