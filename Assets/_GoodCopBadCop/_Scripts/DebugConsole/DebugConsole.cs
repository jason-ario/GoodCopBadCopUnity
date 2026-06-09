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

    [Tooltip("Index into Telephone._availableTasks for the Go Hunting task. Triggered with F10.")]
    [SerializeField] private int _goHuntingPhoneTaskIndex = 1;

    [Header("Mutant Debug")]
    [Tooltip("Spawner used by O to force-spawn a guaranteed aggroed mutant.")]
    [SerializeField] private MutantSpawner _debugMutantSpawner;

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

        // F3 — force the next suspect to spawn as a mutant intruder.
        if (Input.GetKeyDown(KeyCode.F3))
        {
            ForceNextMutantIntruder();
        }

        // F4 — trigger an incoming telephone call with the configured task index.
        if (Input.GetKeyDown(KeyCode.F4))
        {
            TriggerDebugPhoneCall();
        }

        // F10 — trigger an incoming telephone call for the Go Hunting task.
        if (Input.GetKeyDown(KeyCode.F10))
        {
            TriggerGoHuntingCall();
        }

        if (Input.GetKeyDown(KeyCode.F8))
        {
            ShiftManager.Instance.EndShift();
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            BetweenShiftTaskManager.Instance.ForceCompleteAllTasks();
        }

        // Insert — queue the test day (Day_Test) as the destination of the next AdvanceDay call.
        if (Input.GetKeyDown(KeyCode.Insert))
        {
            ForceTestDay();
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

        // K — kill the local player.
        if (Input.GetKeyDown(KeyCode.K))
        {
            KillLocalPlayer();
        }

        // O — force-spawn a guaranteed aggroed mutant from the debug spawner.
        if (Input.GetKeyDown(KeyCode.O))
        {
            SpawnDebugAggroedMutant();
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
    /// Queues <see cref="Day_Test"/> as the destination of the next <see cref="CampaignManager.AdvanceDay"/> call.
    /// The current shift is unaffected; the override takes effect when the shift ends and the day advances.
    /// </summary>
    private void ForceTestDay()
    {
        CampaignManager.DebugNextDayOverride = Day_Test.TestDayNumber;
        Debug.Log($"[DebugConsole] Next day advance will load Day_Test (Day {Day_Test.TestDayNumber}) — Insert.");
    }

    /// <summary>
    /// Forces the next suspect slot to spawn as a mutant intruder regardless of configured spawn chance.
    /// Takes effect when the current suspect is dismissed and the next one is queued.
    /// </summary>
    private void ForceNextMutantIntruder()
    {
        SuspectController.ForceNextSuspectMutant = true;
        Debug.Log("[DebugConsole] Next suspect will be a mutant intruder (F3).");
    }

    /// <summary>
    /// Triggers the telephone ring for the task at <see cref="_debugPhoneTaskIndex"/>.
    /// Uses the full call flow so the task is registered in the guidebook when answered.
    /// </summary>
    private void TriggerDebugPhoneCall()
    {
        if (Telephone.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] Telephone.Instance not found — is the phone spawned in the scene?");
            return;
        }

        Telephone.Instance.TriggerCallSynced(_debugPhoneTaskIndex);
        Debug.Log($"[DebugConsole] Phone call triggered for task index {_debugPhoneTaskIndex} (F4).");
    }

    /// <summary>
    /// Triggers the telephone ring that delivers the Go Hunting task when answered.
    /// Task index is configured via <see cref="_goHuntingPhoneTaskIndex"/> in the Inspector.
    /// </summary>
    private void TriggerGoHuntingCall()
    {
        if (Telephone.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] Telephone.Instance not found — is the phone spawned in the scene?");
            return;
        }

        Telephone.Instance.TriggerCallSynced(_goHuntingPhoneTaskIndex);
        Debug.Log($"[DebugConsole] Go Hunting phone call triggered (F10).");
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

    /// <summary>Kills the local player for testing death and spectating.</summary>
    private void KillLocalPlayer()
    {
        if (PlayerInstance.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] PlayerInstance.Instance not found.");
            return;
        }

        if (PlayerInstance.Instance.PlayerHealth == null)
        {
            Debug.LogWarning("[DebugConsole] Local player does not have a PlayerHealth component.");
            return;
        }

        PlayerInstance.Instance.PlayerHealth.TakeDamage(999f);
        Debug.Log("[DebugConsole] Local player killed (K).");
    }

    /// <summary>
    /// Forces a single aggroed mutant to spawn from <see cref="_debugMutantSpawner"/>,
    /// bypassing the <see cref="MutantEnemyData.aggroChance"/> roll.
    /// </summary>
    private void SpawnDebugAggroedMutant()
    {
        if (_debugMutantSpawner == null)
        {
            Debug.LogWarning("[DebugConsole] _debugMutantSpawner not assigned — assign it in the Inspector (O).");
            return;
        }

        _debugMutantSpawner.ForceSpawnAggroed();
        Debug.Log("[DebugConsole] Aggroed mutant spawn triggered (O).");
}

    }
