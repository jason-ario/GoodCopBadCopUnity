using System;
using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private GameObject campaignScreen;
    [SerializeField] private GameObject multiplayerScreen;
    [SerializeField] private GameObject joinGameScreen;
    [SerializeField] private GameObject preGameLobbyScreen;
    [SerializeField] private GameObject settingsScreen;
    [SerializeField] private Animator screenFade;

    [Header("Screen Controllers")]
    [SerializeField] private CampaignScreenController campaignScreenController;
    [SerializeField] private StartCampaignScreen preGameLobbyController;

    [Header("Cutscenes")]
    [SerializeField] private PlayableDirector playableDirector;

    [Header("Button Group")]
    [SerializeField] private Animator buttonGroupAnimator;
    [SerializeField] private RuntimeAnimatorController buttonGroupFastController;

    [Header("Scene Setup")]
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private GameObject sceneCamera;
    [SerializeField] private Transform camEndPos;
    [SerializeField] private WindowLampController windowLampController;
    [SerializeField] private float timeTillOpenWindow = 8f;

    [SerializeField] public CanvasGroup canvasGroup;

    [Header("Debug")]
    [SerializeField] private bool _debugSkipToGame;
    [SerializeField] private int _debugSlotIndex = 0;

    private GameObject _currentScreen;
    private List<GameObject> _allScreens;
    private bool _buttonGroupFaded;

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _allScreens = new List<GameObject>
        {
            homeScreen,
            campaignScreen,
            multiplayerScreen,
            joinGameScreen,
            preGameLobbyScreen,
            settingsScreen
        };
    }

    private void Start()
    {
        UIController.Instance.ClosePlayerUI();

#if UNITY_EDITOR
        if (_debugSkipToGame)
        {
            DebugSkipToGame();
            return;
        }
#endif

        SwitchToScreen(homeScreen);
        playableDirector.gameObject.SetActive(true);
    }

    // ---------------------------------------------------------------------------
    // Screen Switching
    // ---------------------------------------------------------------------------

    private void SwitchToScreen(GameObject target)
    {
        if (target == null)
            return;

        // Swap the button group to the fast animator on the first navigation away from the home screen.
        if (!_buttonGroupFaded && target != homeScreen)
        {
            _buttonGroupFaded = true;
            if (buttonGroupAnimator != null && buttonGroupFastController != null)
                buttonGroupAnimator.runtimeAnimatorController = buttonGroupFastController;
        }

        foreach (var screen in _allScreens)
            screen.SetActive(screen == target);

        _currentScreen = target;
    }

    /// <summary>Opens the campaign slot-selection screen.</summary>
    public void OpenCampaignScreen() =>
        SwitchToScreen(campaignScreen);

    /// <summary>Opens the multiplayer hub (host / join choice).</summary>
    public void OpenMultiplayerScreen() =>
        SwitchToScreen(multiplayerScreen);

    /// <summary>Opens the join-by-code screen from the multiplayer hub.</summary>
    public void OpenJoinLobbyScreen() =>
        SwitchToScreen(joinGameScreen);

    /// <summary>Opens the settings screen.</summary>
    public void OpenSettingsScreen() =>
        SwitchToScreen(settingsScreen);

    /// <summary>Returns to the home screen from any screen.</summary>
    public void BackToHomeScreen() =>
        SwitchToScreen(homeScreen);

    /// <summary>Returns to the multiplayer hub from the join screen.</summary>
    public void BackToMultiplayerScreen() =>
        SwitchToScreen(multiplayerScreen);

    /// <summary>
    /// Opens the pre-game lobby screen. Called after networking is established,
    /// either from the campaign slot flow or the multiplayer hub.
    /// </summary>
    public void OpenPreGameLobbyScreen(bool multiplayerMode)
    {
        preGameLobbyController?.Setup(multiplayerMode);
        SwitchToScreen(preGameLobbyScreen);
    }

    /// <summary>Backward-compatible alias kept for existing UnityEvent bindings.</summary>
    public void BackToStartShiftScreen() =>
        SwitchToScreen(homeScreen);

    // ---------------------------------------------------------------------------
    // Multiplayer Hub Actions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a lobby and navigates to the pre-game lobby screen as host.
    /// Called from the "Host Game" button in the multiplayer hub.
    /// </summary>
    public async void HostMultiplayerGame()
    {
        try
        {
            GameManager.Instance.BeginLobbyTransition();
            bool success = await LobbyManager.Instance.CreateLobby();

            if (success)
            {
                await WaitUntilHostReady();
                GameManager.Instance.TransitionToLobby();
                OpenPreGameLobbyScreen(multiplayerMode: true);
            }
            else
            {
                Debug.LogError("[HostMultiplayerGame] CreateLobby failed.");
                GameManager.Instance.CancelLobbyTransition();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HostMultiplayerGame] Unhandled exception: {e}");
            GameManager.Instance.CancelLobbyTransition();
        }
    }

    // ---------------------------------------------------------------------------
    // Campaign (Solo) Actions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Starts a new solo session. Creates a lobby, transitions to the lobby area,
    /// and opens the pre-game lobby screen.
    /// Called after the player has selected a save slot.
    /// </summary>
    public async void StartNewGame()
    {
        Debug.Log("[StartNewGame] Called.");
        try
        {
            GameManager.Instance.BeginLobbyTransition();
            bool success = await LobbyManager.Instance.CreateLobby();

            if (success)
            {
                await WaitUntilHostReady();
                Debug.Log($"[StartNewGame] Lobby ready — opening pre-game lobby screen.");
                OpenPreGameLobbyScreen(multiplayerMode: true);
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

    /// <summary>
    /// Resumes a previous solo session without the full transition sequence.
    /// Expects the active slot to already be set via <see cref="SaveDataManager.SelectSlot"/>.
    /// </summary>
    public async void ContinueGame()
    {
        if (SaveDataManager.Instance.ActiveSlot == null)
        {
            Debug.LogWarning("[ContinueGame] No active slot selected.");
            return;
        }

        bool success = await LobbyManager.Instance.CreateLobby();
        if (success)
            GameManager.Instance.TryStartGame(skipTransition: true);
    }

    // ---------------------------------------------------------------------------
    // Legacy / Game Start
    // ---------------------------------------------------------------------------

    public void StartGame()
    {
        StopAllCoroutines();
        GameManager.Instance.TryStartGame();
    }

    public void HideAllMenus()
    {
        foreach (var screen in _allScreens)
            screen.SetActive(false);
    }

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

    // ---------------------------------------------------------------------------
    // Save Data
    // ---------------------------------------------------------------------------

    /// <summary>Returns true when any slot has meaningful progress.</summary>
    public bool HasSaveFile =>
        SaveDataManager.Instance != null && SaveDataManager.Instance.HasSaveFile;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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
            Debug.LogWarning("[WaitUntilHostReady] Timed out waiting for host to be ready.");
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only debug shortcut that skips the entire main menu flow and jumps straight into gameplay.
    /// Mirrors the full sequence: slot selection → lobby creation → game start.
    /// Toggle via the "Debug Skip To Game" field in the Inspector.
    /// </summary>
    private async void DebugSkipToGame()
    {
        Debug.Log($"[DebugSkipToGame] Skipping UI flow — using slot {_debugSlotIndex}.");

        SaveDataManager.Instance.SelectSlot(_debugSlotIndex);
        GameManager.Instance.BeginLobbyTransition();

        bool success = await LobbyManager.Instance.CreateLobby();
        if (!success)
        {
            Debug.LogError("[DebugSkipToGame] CreateLobby failed — aborting debug skip.");
            GameManager.Instance.CancelLobbyTransition();
            SwitchToScreen(homeScreen);
            playableDirector.gameObject.SetActive(true);
            return;
        }

        await WaitUntilHostReady();

        SaveDataManager.Instance.InitialiseActiveSlot();
        GameManager.Instance.TransitionToLobby();
        GameManager.Instance.TryStartGame();
    }
#endif

    private IEnumerator WaitAndOpenWindow()
    {
        yield return new WaitForSeconds(timeTillOpenWindow);
        ShiftManager.Instance.OpenWindow();
    }
}
