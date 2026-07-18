using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using GoodCopBadCop.UI.SettingsMenu;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Playables;
using R3;
using VContainer;

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
    [SerializeField] private Animator screenFade;

    [Header("Screen Controllers")]
    [SerializeField] private CampaignScreenController campaignScreenController;
    [SerializeField] private StartCampaignScreen preGameLobbyController;

    [Header("Cutscenes")]
    [SerializeField] private PlayableDirector playableDirector;

    [Header("Button Group")]
    [SerializeField] private Animator buttonGroupAnimator;
    [SerializeField] private RuntimeAnimatorController buttonGroupFastController;
    [SerializeField] private GameObject continueButton;

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
    private ISettingsMenuView settingsMenuView;
    private DisposableBag settingsDisposables;
    private bool isSettingsOpen;

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    [Inject]
    public void Construct(ISettingsMenuView settingsMenuView)
    {
        this.settingsMenuView = settingsMenuView;
        settingsMenuView.BackRequested.Subscribe(_ => CloseSettingsScreen()).AddTo(ref settingsDisposables);
    }

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
            preGameLobbyScreen
        };
    }

    private void OnDestroy()
    {
        settingsDisposables.Dispose();
    }

    private void Start()
    {
        UIController.Instance.ClosePlayerUI();
        settingsMenuView.SetVisible(false);

#if UNITY_EDITOR
        if (_debugSkipToGame)
        {
            DebugSkipToGame();
            return;
        }
#endif

        SwitchToScreen(homeScreen);
        playableDirector.gameObject.SetActive(true);
        RefreshContinueButton();
    }

    // ---------------------------------------------------------------------------
    // Screen Switching
    // ---------------------------------------------------------------------------

    private void SwitchToScreen(GameObject target)
    {
        if (target == null)
            return;

        if (target != homeScreen)
        {
            EnsureButtonGroupFastController();
        }

        foreach (var screen in _allScreens)
        {
            if (screen != null)
            {
                screen.SetActive(screen == target);
            }
        }

        _currentScreen = target;
    }

    private void EnsureButtonGroupFastController()
    {
        if (_buttonGroupFaded)
        {
            return;
        }

        _buttonGroupFaded = true;
        if (buttonGroupAnimator != null && buttonGroupFastController != null)
        {
            buttonGroupAnimator.runtimeAnimatorController = buttonGroupFastController;
        }
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
    public void OpenSettingsScreen()
    {
        EnsureButtonGroupFastController();
        HideAllMenus();
        isSettingsOpen = true;
        settingsMenuView.SetVisible(true);
    }

    /// <summary>Returns to the home screen from any screen.</summary>
    public void BackToHomeScreen()
    {
        SwitchToScreen(homeScreen);
        RefreshContinueButton();
    }

    private void CloseSettingsScreen()
    {
        if (!isSettingsOpen)
        {
            return;
        }

        isSettingsOpen = false;
        settingsMenuView.SetVisible(false);
        BackToHomeScreen();
    }

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
    public void BackToStartShiftScreen()
    {
        SwitchToScreen(homeScreen);
        RefreshContinueButton();
    }

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
    /// Resumes the player's most recently saved slot without requiring manual slot selection.
    /// Mirrors the DebugSkipToGame flow: selects the best slot, creates a lobby, transitions,
    /// initialises the slot, and starts the game — bypassing the pre-game lobby screen.
    /// </summary>
    public async void ContinueGame()
    {
        int slotIndex = SaveDataManager.Instance.GetMostRecentOccupiedSlotIndex();
        if (slotIndex < 0)
        {
            Debug.LogWarning("[ContinueGame] No occupied save slot found.");
            return;
        }

        try
        {
            SaveDataManager.Instance.SelectSlot(slotIndex);
            GameManager.Instance.BeginLobbyTransition();

            // Start the fade and stinger before creating the lobby so that Netcode's automatic
            // player-prefab spawn (which fires the moment the host connects) is hidden
            // behind the dark screen — preventing a mid-transition camera jump.
            UIController.Instance.FadeIn();
            GameManager.Instance.PlayTransitionStinger();
            await Task.Delay(TimeSpan.FromSeconds(UIController.Instance.FadeInDuration));

            bool success = await LobbyManager.Instance.CreateLobby();

            if (success)
            {
                await WaitUntilHostReady();
                SaveDataManager.Instance.InitialiseActiveSlot();
                GameManager.Instance.TransitionToLobby();
                GameManager.Instance.TryStartGame();
            }
            else
            {
                Debug.LogError("[ContinueGame] CreateLobby failed — cancelling transition.");
                UIController.Instance.FadeOut();
                GameManager.Instance.CancelLobbyTransition();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ContinueGame] Unhandled exception: {e}");
            UIController.Instance.FadeOut();
            GameManager.Instance.CancelLobbyTransition();
        }
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
        {
            if (screen != null)
            {
                screen.SetActive(false);
            }
        }
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

    /// <summary>
    /// Shows or hides the Continue button based on whether any occupied save slot exists.
    /// Called on Start and whenever we return to the home screen.
    /// </summary>
    private void RefreshContinueButton()
    {
        if (continueButton != null)
            continueButton.SetActive(HasSaveFile);
    }

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
