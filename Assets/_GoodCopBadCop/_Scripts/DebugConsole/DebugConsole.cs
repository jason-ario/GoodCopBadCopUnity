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

    private const float FastTimescale = 3f;
    private bool _isFastForwarding;

    [Tooltip("Skips the main menu, all cutscenes, and spawns the player directly in the booth with the shift switch ready. Equivalent to skipping main menu + F10.")]
    public bool skipToBoothReady;

    [Tooltip("Skips straight to after the shift ends — triggers EndShift and auto-dismisses the report, landing in the night phase with tasks assigned.")]
    public bool skipToAfterShift;

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
        if (skipToBoothReady || skipToAfterShift)
        {
            NetworkManager.Singleton.StartHost();
            LobbyManager.Instance.CreateLobby();
            GameManager.Instance.TryStartGame(true);
            // SkipToBoothReady() / DebugSkipToAfterShift() are deferred to ShiftManager.OnNetworkSpawn
            // so all NetworkObjects are spawned and ClientRpcs are safe to send.
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

        if (Input.GetKeyDown(KeyCode.F8))
        {
            ShiftManager.Instance.EndShift();
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            BetweenShiftTaskManager.Instance.ForceCompleteAllTasks();
        }

        if (Input.GetKeyDown(KeyCode.F10))
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
