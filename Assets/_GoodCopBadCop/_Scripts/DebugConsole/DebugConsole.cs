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

    [Tooltip("Skips the main menu and spawns the player directly in the booth at the start of Day 1, " +
             "with Day 1 fully activated (Vlad's opening sequence fires automatically after the 7 s shutter delay).")]
    public bool skipToDay1Booth;

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

    [Header("Gate Debug")]
    [Tooltip("Start Shift Gate — forced into post-intro state by the F12 skip so interactions toggle it instead of opening the start-shift screen.")]
    [SerializeField] private GateStartShiftController _startShiftGate;

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
        if (skipToBoothReady || skipToAfterShift || autoStart || skipToDay1Booth)
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

        // F1 — prime the timecard machine for clock-out (simulates all suspects processed).
        if (Input.GetKeyDown(KeyCode.F1))
        {
            EnableClockOut();
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            SkipToDay(1);
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

        // H — deal a small hit to the local player to test the hit animation.
        if (Input.GetKeyDown(KeyCode.H))
        {
            HitLocalPlayer();
        }

        // O — force-spawn a guaranteed aggroed mutant from the debug spawner.
        if (Input.GetKeyDown(KeyCode.O))
        {
            SpawnDebugAggroedMutant();
        }

        // F11 — force the Alexei scripted event on the next suspect arrival.
        if (Input.GetKeyDown(KeyCode.F11))
        {
            ForceAlexeiSequenceOnNextSuspect();
        }

        // F12 — skip to the end of Day 1 with the timecard machine primed for clock-out.
        // The shift is started, all suspects are bypassed, and the player must walk to
        // the machine to clock out, then to bed to begin Day 2.
        if (Input.GetKeyDown(KeyCode.F12))
        {
            SkipToEndOfDay1();
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
    /// Primes the timecard machine for clock-out as if all suspects had been processed.
    /// The shift does not end until the player physically interacts with the machine.
    /// Server only — no-op on clients.
    /// </summary>
    private void EnableClockOut()
    {
        if (ShiftManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] ShiftManager not available — start the game first.");
            return;
        }

        ShiftManager.Instance.DebugEnableClockOut();
        Debug.Log("[DebugConsole] Timecard machine primed for clock-out (F1).");
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

    /// <summary>Adds a one-off DebugTask to TaskRegistry for task testing.</summary>
    private void AddDebugTask()
    {
        if (TaskRegistry.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] TaskRegistry not available.");
            return;
        }

        if (_debugTask == null)
            _debugTask = gameObject.AddComponent<DebugTask>();

        TaskRegistry.Instance.AddThreat(_debugTask);
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

    /// <summary>Deals a small amount of damage to the local player to test the hit animation.</summary>
    private void HitLocalPlayer()
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

        const float DebugHitDamage = 10f;
        PlayerInstance.Instance.PlayerHealth.TakeDamage(DebugHitDamage);
        Debug.Log($"[DebugConsole] Local player hit for {DebugHitDamage} damage (H).");
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
    /// Skips Day 1 directly to the Soldier's slot (suspect index 4), bypassing Vlad,
    /// the random civilian, the doc-anomaly suspect, and Ivan. Calls <see cref="SkipToDay"/>
    /// to activate Day 1, then waits a frame for <see cref="Day_01"/> to subscribe its
    /// events before arming the Soldier intercept and triggering the next spawn slot.
    /// </summary>
    private void SkipToSoldierSequence()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToSoldierSequence: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(1);
        StartCoroutine(ArmSoldierAfterDelay());
    }

    private IEnumerator ArmSoldierAfterDelay()
    {
        // Wait one frame for Day_01 to activate, subscribe its events, and set its Instance.
        yield return null;

        if (Day_01.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] ArmSoldierAfterDelay: Day_01.Instance not found after SkipToDay(1).");
            yield break;
        }

        // JumpToDay fires OnDayChanged which resets _introComplete on the gate.
        // Force it back to true so interactions toggle the gate instead of opening the start-shift screen.
        _startShiftGate?.ForceIntroComplete();

        // Abort the 7 s shutter delay, open the shutter, arm the Soldier intercept.
        Day_01.Instance.DebugSkipToSoldierSlot();

        // Start the shift BEFORE setting the debug index. TryStartShift delivers a ClientRpc
        // that runs OpenWindowSequence, whose first statement synchronously calls
        // ResetSuspects() (resetting suspectIndex to -1). We yield one frame so that RPC is
        // processed and the reset completes BEFORE we write suspectIndex = 3, otherwise the
        // reset overwrites our value and NextSuspect() spawns slot 0 instead of slot 4,
        // causing OnSoldierArrivedAtWindow to bail on its index check.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(0f, 0f);
        ShiftManager.Instance?.TryStartShift();

        yield return null; // Let OpenWindowSequence's ResetSuspects() run first.

        // Set suspectIndex to 3 so the next NextSuspect() increments to 4 (Soldier's slot).
        SuspectController.Instance?.DebugSetSuspectIndex(3);

        // Trigger the next suspect slot immediately — this fires the Soldier intercept.
        SuspectController.Instance?.NextSuspect();

        Debug.Log("[DebugConsole] Skipped to Soldier sequence (F12).");
    }
    /// bypassing normal character spawning and playing the mocking sequence directly.
    /// Useful for testing the soldier event without running through all preceding suspects.
    /// </summary>
    private void ForceAlexeiSequenceOnNextSuspect()
    {
        if (SoldierMockingController.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SoldierMockingController.Instance not found — is the soldier event object in the scene?");
            return;
        }

        SuspectController.InterceptNextSuspectSpawn = () => SoldierMockingController.Instance.BeginSequence();
        Debug.Log("[DebugConsole] Soldier mocking event will intercept the next suspect spawn slot (F11).");
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
