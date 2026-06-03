using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;


public class DebugConsole : MonoBehaviour
{
    public bool skipMainMenu; 
    public bool skipLobby;
    public bool skipInitialShiftTransition;
    public bool cutsceneMode;

    [SerializeField] private float FastTimescale = 5f;
    private bool _isFastForwarding;

    [Tooltip("Skips the main menu, all cutscenes, and spawns the player directly in the booth with the shift switch ready. Equivalent to skipping main menu + F10.")]
    public bool skipToBoothReady;

    [Tooltip("Skips straight to after the shift ends — triggers EndShift and auto-dismisses the report, landing in the night phase with tasks assigned.")]
    public bool skipToAfterShift;

    [Tooltip("Starts as host, creates a lobby, and begins a new shift automatically. Skips the entire main menu and lobby flow and lands you in the game with the player spawned.")]
    public bool autoStart;

    [Header("Telephone Debug")]
    [Tooltip("Index into Telephone._availableTasks to deliver when pressing F4.")]
    [SerializeField] private int _debugPhoneTaskIndex = 0;

    [SerializeField] private MainMenuController _mainMenuController;
    [SerializeField] private GameObject mainMenuScreen;

    public static DebugConsole Instance;

    private DebugTask _debugTask;
    
    void Awake()
    {
        Instance = this;
        if (cutsceneMode)
        {
            _mainMenuController.enabled = false;
            mainMenuScreen.SetActive(false);
        }
    }
    
    private void Start()
    {
        if (skipToBoothReady || skipToAfterShift || autoStart)
        {
            NetworkManager.Singleton.StartHost();
            LobbyManager.Instance.CreateLobby();
            GameManager.Instance.TryStartGame(true);

            if (autoStart)
                GameManager.Instance.OnGameStart += OnGameStartAutoStart;

            // Deferred skip logic runs in ShiftManager.OnNetworkSpawn once all NetworkObjects are ready.
            return;
        }

        if (skipMainMenu)
        {
            if (cutsceneMode)
            {
                UIController.Instance.ClosePlayerUI();
                return;
            }
            NetworkManager.Singleton.StartHost();
            LobbyManager.Instance.CreateLobby();
            GameManager.Instance.TryStartGame(true);
        }
        
        if (cutsceneMode)
        {
            UIController.Instance.ClosePlayerUI();
        }
        
        if (skipLobby)
        {
            ShiftManager.Instance.StartNewShift();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            GlobalHostVariables.Instance.AddMoney(1000);
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            SkipToDay(2);
        }

        // F4 — trigger an incoming telephone call with the configured task index.
        if (Input.GetKeyDown(KeyCode.F4))
        {
            TriggerDebugPhoneCall();
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            ShiftManager.Instance.EndShift();
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            BetweenShiftTaskManager.Instance.ForceCompleteAllTasks();
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            ShiftManager.Instance.EndIntroCutscene();
        }

        // F5 — inject a debug task into the guidebook task list.
        if (Input.GetKeyDown(KeyCode.F5))
        {
            AddDebugTask();
        }

        // F6 — mark the injected debug task as complete.
        if (Input.GetKeyDown(KeyCode.F6))
        {
            CompleteDebugTask();
        }

        // Hold + (equals key) to run at 3x timescale; release to restore normal speed.
        if (Input.GetKey(KeyCode.Equals) && !_isFastForwarding)
        {
            _isFastForwarding = true;
            Time.timeScale = FastTimescale;
        }
        else if (!Input.GetKey(KeyCode.Equals) && _isFastForwarding)
        {
            _isFastForwarding = false;
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// Called once by <see cref="GameManager.OnGameStart"/> after the lobby join sequence
    /// completes and the player is fully spawned. Starts the shift and unsubscribes immediately.
    /// </summary>
    private void OnGameStartAutoStart()
    {
        GameManager.Instance.OnGameStart -= OnGameStartAutoStart;
        ShiftManager.Instance.StartNewShift();
    }

    /// <summary>
    /// Triggers the telephone ring for testing. If tasks are configured, delivers the task at
    /// <see cref="_debugPhoneTaskIndex"/> via the full call flow. Otherwise falls back to
    /// <see cref="Telephone.DebugStartRing"/> so the audio and animation can be tested without
    /// any <see cref="PhoneTaskData"/> assets assigned.
    /// </summary>
    private void TriggerDebugPhoneCall()
    {
        if (Telephone.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] Telephone.Instance not found — is the phone spawned in the scene?");
            return;
        }

        Telephone.Instance.DebugStartRing();
        Debug.Log("[DebugConsole] Phone ring triggered (audio + animation test). Assign PhoneTaskData to _availableTasks for the full call flow).");
    }

    /// <summary>
    /// Skips booth-ready setup and immediately jumps CampaignManager to the given day.
    /// Runs SkipToBoothReady first if the game is not yet in the shift, then applies
    /// the target day on top. Server-only.
    /// </summary>
    private void SkipToDay(int targetDay)
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToDay: CampaignManager not available — start the game first.");
            return;
        }

        ShiftManager.Instance.SkipToBoothReady();
        CampaignManager.Instance.JumpToDay(targetDay);
    }

    /// <summary>Adds a one-off DebugTask to GuidebookTaskRegistry for guidebook testing.</summary>
    private void AddDebugTask()
    {
        if (GuidebookTaskRegistry.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] GuidebookTaskRegistry not available.");
            return;
        }

        if (_debugTask == null)
            _debugTask = gameObject.AddComponent<DebugTask>();

        GuidebookTaskRegistry.Instance.AddTask(_debugTask);
        Debug.Log("[DebugConsole] Debug task added to registry (F6 to complete).");
    }

    /// <summary>Marks the injected DebugTask as complete.</summary>
    private void CompleteDebugTask()
    {
        if (_debugTask == null)
        {
            Debug.LogWarning("[DebugConsole] No debug task to complete — press F5 first.");
            return;
        }

        _debugTask.Complete();
    }
}
