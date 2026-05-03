using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Playables;

public class MainMenuController : MonoBehaviour
{
    public static MainMenuController Instance;

    [Header("Screens")]
    [SerializeField] public GameObject mainMenu;
    [SerializeField] private GameObject homeScreen;
    [SerializeField] private GameObject settingsScreen;
    [SerializeField] private GameObject joinGameScreen;
    [SerializeField] private Animator screenFade;

    [Header("Cutscenes")] 
    [SerializeField] private PlayableDirector playableDirector;
    
    [Header("Scene Setup")]
    [SerializeField] private Animator rollingShutter;
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
            joinGameScreen,
            settingsScreen
        };
    }
    
    private void Start()
    {
        UIController.Instance.ClosePlayerUI();

        SwitchToScreen(homeScreen);
        
        playableDirector.gameObject.SetActive(true);
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
    
    /// <summary>Starts a new solo session. Creates a lobby, spawns the player at the lobby position, and fades the menu out.</summary>
    public async void StartNewGame()
    {
        Debug.Log("[StartNewGame] Called.");
        try
        {
            Debug.Log("[StartNewGame] Calling BeginLobbyTransition.");
            GameManager.Instance.BeginLobbyTransition();

            Debug.Log("[StartNewGame] Awaiting CreateLobby...");
            bool success = await LobbyManager.Instance.CreateLobby();
            Debug.Log($"[StartNewGame] CreateLobby returned: {success}");

            if (success)
            {
                await WaitUntilHostReady();
                Debug.Log($"[StartNewGame] IsServer={GameManager.Instance.IsServer} IsHost={Unity.Netcode.NetworkManager.Singleton.IsHost} — calling TransitionToLobby.");
                GameManager.Instance.TransitionToLobby();
            }
            else
            {
                Debug.LogError("[StartNewGame] CreateLobby failed — cancelling transition.");
                GameManager.Instance.CancelLobbyTransition();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[StartNewGame] Unhandled exception: {e}");
            GameManager.Instance.CancelLobbyTransition();
        }
    }

    /// <summary>Waits until the NetworkManager has fully promoted this peer to host/server.</summary>
    private static async System.Threading.Tasks.Task WaitUntilHostReady()
    {
        const int TimeoutMs = 5000;
        const int PollIntervalMs = 50;
        int elapsed = 0;

        while (!Unity.Netcode.NetworkManager.Singleton.IsHost && elapsed < TimeoutMs)
        {
            await System.Threading.Tasks.Task.Delay(PollIntervalMs);
            elapsed += PollIntervalMs;
        }

        if (!Unity.Netcode.NetworkManager.Singleton.IsHost)
            Debug.LogWarning("[StartNewGame] Timed out waiting for host to be ready.");
    }

    public void OpenJoinLobbyScreen() =>
        SwitchToScreen(joinGameScreen);
    
    public void OpenSettingsScreen() =>
        SwitchToScreen(settingsScreen);

    public void BackToHomeScreen() =>
        SwitchToScreen(homeScreen);

    /// <summary>Backward-compatible alias kept for existing UnityEvent bindings in Cutscenes 2.unity.</summary>
    public void BackToStartShiftScreen() =>
        SwitchToScreen(homeScreen);

    /// <summary>Returns true when a save file with meaningful progress exists.</summary>
    public bool HasSaveFile => SaveDataManager.Instance != null && SaveDataManager.Instance.HasSaveFile;

    #endregion

    #region Game Start

    public void StartGame()
    {
        StopAllCoroutines();
        
        GameManager.Instance.TryStartGame();
    }

    /// <summary>
    /// Resumes a previous session. Creates a solo lobby and starts immediately with no transition.
    /// Only call this when HasSaveFile is true.
    /// </summary>
    public async void ContinueGame()
    {
        if (!HasSaveFile)
        {
            Debug.LogWarning("ContinueGame called but no save file exists.");
            return;
        }

        bool success = await LobbyManager.Instance.CreateLobby();
        if (success)
            GameManager.Instance.TryStartGame(skipTransition: true);
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
        ShiftManager.Instance.OpenWindow();
    }

    #endregion

    public void TransitionToGameplay()
    {
        mainMenu.SetActive(false);

        if (playableDirector != null)
        {
            playableDirector.Stop();
            playableDirector.gameObject.SetActive(false);
        }

        HideAllMenus();
    }
}
