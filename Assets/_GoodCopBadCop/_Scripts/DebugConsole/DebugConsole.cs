using System;
using System.Collections;
using GoodCopBadCop.Effects;
using UnityEngine;
using UnityEngine.Events;


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

    [Header("Full Mutant Debug")]
    [Tooltip("SuspectData for the Butcher. Assign in Inspector. Used by the B hack.")]
    [SerializeField] private SuspectData _butcherSuspectData;

    [Tooltip("ElectricityController scene object. Assign in Inspector. The B hack calls PowerOn() to guarantee lights are on at Day 4.")]
    [SerializeField] private ElectricityController _electricityController;

    [Header("Power Station Debug")]
    [Tooltip("Spawn point at the power station. Assign 'Player Spawn Pos - At Power Station' from the scene. Used by the P key and the cheat console button.")]
    [SerializeField] private Transform _powerStationSpawnPoint;

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
    
    private async void Start()
    {
        if (skipToBoothReady || skipToAfterShift || autoStart || skipToDay1Booth)
        {
            if (!await LobbyManager.Instance.CreateLobby())
                return;

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
            if (!await LobbyManager.Instance.CreateLobby())
                return;

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

        // F12 is handled by CheatConsoleUI — it opens the overlay cheat menu.

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
    /// Executes <paramref name="onReady"/> immediately if the game is already running,
    /// otherwise bootstraps a host session and defers the callback until
    /// <see cref="GameManager.OnGameStart"/> fires and the player is fully spawned.
    /// </summary>
    public async void EnsureGameStartedThen(Action onReady)
    {
        if (GameManager.Instance.HasGameStarted)
        {
            onReady();
            return;
        }

        if (!await LobbyManager.Instance.CreateLobby())
            return;

        GameManager.Instance.TryStartGame(true);

        UnityAction handler = null;
        handler = () =>
        {
            GameManager.Instance.OnGameStart -= handler;
            onReady();
        };
        GameManager.Instance.OnGameStart += handler;
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
    /// For any day other than Day 1, ink stamps and the folder stack are unlocked after
    /// one frame so that Day 1's tutorial locks do not carry over into later-day debug skips.
    /// </summary>
    public void SkipToDay(int targetDay)
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToDay: CampaignManager not available — start the game first.");
            return;
        }

        ShiftManager.SuppressFanfare = true;
        ShiftManager.Instance.SkipToBoothReady();
        CampaignManager.Instance.JumpToDay(targetDay);

        if (targetDay != 1)
            StartCoroutine(UnlockTutorialGatedItemsAfterDelay());
    }

    /// <summary>
    /// Waits one frame (to allow the day's DayActivated to complete), then forces all
    /// InkStamp slots and StackOfFolders instances interactable. Prevents Day 1's
    /// tutorial locks from persisting when skipping to a later day.
    /// </summary>
    private IEnumerator UnlockTutorialGatedItemsAfterDelay()
    {
        yield return null;

        foreach (var stamp in FindObjectsByType<InkStamp>(FindObjectsSortMode.None))
            stamp.SetSlotInteractable(true);

        foreach (var stack in FindObjectsByType<StackOfFolders>(FindObjectsSortMode.None))
            stack.SetInteractable(true);

        Debug.Log("[DebugConsole] Ink stamps and folder stack unlocked for non-Day-1 skip.");
    }

    /// <summary>
    /// Starts an ordinary, fully playable Day 4 shift in the booth. Day 4 has no tutorial
    /// gates or mandatory opening event, and it restores the power before the shift begins.
    /// Intended as the general-purpose sandbox entry from the F12 cheat console.
    /// </summary>
    public void StartFreePlay()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] StartFreePlay: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(4);
        StartCoroutine(StartFreePlayAfterDayLoad());
    }

    private IEnumerator StartFreePlayAfterDayLoad()
    {
        // Let CampaignManager activate Day 4 and apply its normal setup (including power on).
        yield return null;

        FindFirstObjectByType<ToolsLocker>()?.DebugForceUnlock();
        ShiftManager.Instance?.TryStartShift();
        Debug.Log("[DebugConsole] Free Play started — Day 4 shift is running, the tool locker is unlocked, and regular suspects will arrive.");
    }

    /// <summary>
    /// Starts an ordinary, fully playable Day 5 shift in the booth with all stamps unlocked
    /// and the tool locker open. Mirrors StartFreePlay but targets Day 5.
    /// </summary>
    public void StartFreePlayDay5()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] StartFreePlayDay5: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(5);
        StartCoroutine(StartFreePlayDay5AfterDayLoad());
    }

    private IEnumerator StartFreePlayDay5AfterDayLoad()
    {
        // Let CampaignManager activate Day 5 and apply its normal setup.
        // UnlockTutorialGatedItemsAfterDelay (launched by SkipToDay) also runs this frame.
        yield return null;

        FindFirstObjectByType<ToolsLocker>()?.DebugForceUnlock();
        ShiftManager.Instance?.TryStartShift();
        Debug.Log("[DebugConsole] Free Play Day 5 started — shift is running, tool locker unlocked, stamps interactable.");
    }

    /// <summary>
    /// Starts a fully playable Day 1 shift with no tutorials, no Alexei cutscene, and
    /// no scripted opening sequence. All tutorial-gated items are unlocked up-front and
    /// regular suspects arrive through the normal queue.
    /// </summary>
    public void StartFreePlayDay1()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] StartFreePlayDay1: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(1);
        StartCoroutine(StartFreePlayDay1AfterDelay());
    }

    private IEnumerator StartFreePlayDay1AfterDelay()
    {
        // Wait one frame for Day_01 to activate and subscribe its events.
        yield return null;

        if (Day_01.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] StartFreePlayDay1: Day_01.Instance not found after SkipToDay(1).");
            yield break;
        }

        // Force the gate into post-intro state so interactions toggle it correctly.
        _startShiftGate?.ForceIntroComplete();

        // Bypass opening sequence, clear Alexei intercept, unlock tutorial-gated items.
        Day_01.Instance.DebugFreePlaySetup();

        // SkipToDay(1) does not call UnlockInkStampsAfterDelay — do it explicitly here.
        foreach (var stamp in FindObjectsByType<InkStamp>(FindObjectsSortMode.None))
            stamp.SetSlotInteractable(true);

        FindFirstObjectByType<ToolsLocker>()?.DebugForceUnlock();
        ShiftManager.Instance?.TryStartShift();

        Debug.Log("[DebugConsole] Free Play Day 1 started — all tutorial gates bypassed, regular suspects will arrive.");
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
        PlayerInstance.Instance.PlayerHealth.TakeDamage(DebugHitDamage, EffectKeys.DefaultPlayerDamage);
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

        PlayerInstance.Instance.PlayerHealth.TakeDamage(999f, EffectKeys.PlayerDeath);
        Debug.Log("[DebugConsole] Local player killed (K).");
    }

    /// <summary>
    /// Skips to Day 1 in the booth with the shift started and the next suspect slot
    /// intercepted to spawn the soldier, triggering the Alexei cutscene sequence.
    /// The soldier arrives after a short delay once the shift is running.
    /// </summary>
    public void SkipToSoldierSlot()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToSoldierSlot: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(1);
        StartCoroutine(SkipToSoldierSlotAfterDelay());
    }

    private IEnumerator SkipToSoldierSlotAfterDelay()
    {
        // Wait one frame for Day_01 to activate and subscribe its events.
        yield return null;

        if (Day_01.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToSoldierSlot: Day_01.Instance not found after SkipToDay(1).");
            yield break;
        }

        // Put the gate in post-intro state so interactions toggle it correctly.
        _startShiftGate?.ForceIntroComplete();

        // Set up booth state (shutter open/locked, lever) and arm the soldier intercept on the
        // next suspect slot. Unlike SkipToEndOfDay1, we intentionally leave the intercept set
        // so the soldier actually spawns.
        Day_01.Instance.DebugSkipToSoldierSlot();

        // Short first-arrival window so the soldier appears a couple of seconds after loading in.
        const float SoldierArrivalDelay = 2f;
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(SoldierArrivalDelay, SoldierArrivalDelay);
        ShiftManager.Instance?.TryStartShift();

        Debug.Log("[DebugConsole] Skipped to soldier slot — soldier will arrive in ~2 s.");
    }

    /// <summary>
    /// Skips to Day 1 in the booth with the shift already started, the booth door unlocked and
    /// opened, and the end-of-shift trash and graffiti tutorial tasks triggered — mirroring
    /// <see cref="AlexeiController.TriggerEndOfShiftSetup"/>, the same production code path used
    /// after the real Alexei sequence. No suspects will arrive. The timecard machine only primes
    /// for clock-out once the player actually finishes both tutorial tasks.
    /// </summary>
    public void SkipToEndOfDay1()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToEndOfDay1: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(1);
        StartCoroutine(SkipToEndOfDay1AfterDelay());
    }

    private IEnumerator SkipToEndOfDay1AfterDelay()
    {
        // Wait one frame for Day_01 to activate and subscribe its events.
        yield return null;

        if (Day_01.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToEndOfDay1: Day_01.Instance not found after SkipToDay(1).");
            yield break;
        }

        // Ensure the gate is in post-intro state so interactions toggle it correctly.
        _startShiftGate?.ForceIntroComplete();

        // Suppress Day1OpeningSequence (Vlad / civilians / quarantine suspect) and open the shutter and lever.
        // This sets _debugSkipActive on Day_01 so OnDayStarted skips the opening coroutine.
        Day_01.Instance.DebugSkipToSoldierSlot();

        // Clear the soldier intercept — no suspects are running in this skip.
        SuspectController.InterceptNextSuspectSpawn = null;

        // Start the shift so shiftStarted = true (prevents bed from being usable before clock-out).
        // Use a large first-arrival interval so no suspect can arrive before the player clocks out.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(9999f, 9999f);
        ShiftManager.Instance?.TryStartShift();

        // Wait two frames for the shift ClientRpc and shiftStarted NetworkVariable to propagate.
        yield return null;
        yield return null;

        // Run the real end-of-shift setup: unlocks/opens the booth door, triggers the trash and
        // graffiti tutorial tasks (highlighting trash items), and marks suspects complete so the
        // timecard machine primes once both tasks are actually finished.
        if (AlexeiController.Instance != null)
            AlexeiController.Instance.TriggerEndOfShiftSetup();
        else
            Debug.LogWarning("[DebugConsole] SkipToEndOfDay1: AlexeiController.Instance not found — door and tutorial tasks not triggered.");

        Debug.Log("[DebugConsole] Skipped to end of Day 1 — booth door opened, trash and graffiti tutorial tasks triggered.");
    }
    /// <summary>
    /// Skips straight to the start of Day 2 with the player positioned inside the bunker,
    /// matching the natural end-of-InBetweenShiftSequence spawn before the player walks out
    /// to begin their shift. JumpToDay is called first so the day NetworkVariable propagates
    /// to all clients before PlayShiftStartFanfare fires (mirrors the SkipToStartOfDay3 /
    /// SkipToOutsideBunker pattern).
    /// </summary>
    public void SkipToStartOfDay2()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToStartOfDay2: CampaignManager not available — start the game first.");
            return;
        }

        CampaignManager.Instance.JumpToDay(2);
        ShiftManager.Instance.SkipToInsideBunker();
    }

    /// <summary>
    /// Skips to Day 2 in the booth with the opening Vlad sequence suppressed and the shift
    /// immediately ended, dropping the player straight into the post-shift Vlad out-back cutscene.
    /// </summary>
    public void SkipToEndOfDay2()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToEndOfDay2: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(2);
        StartCoroutine(SkipToEndOfDay2AfterDelay());
    }

    private IEnumerator SkipToEndOfDay2AfterDelay()
    {
        // Wait one frame for Day_02 to activate and subscribe its events.
        yield return null;

        if (Day_02.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToEndOfDay2: Day_02.Instance not found after SkipToDay(2).");
            yield break;
        }

        // Put the start-shift gate in post-intro state so interactions toggle it correctly.
        _startShiftGate?.ForceIntroComplete();

        // Suppress the opening Vlad sequence and unlock the tool locker.
        Day_02.Instance.DebugSkipOpening();

        // Start the shift with a huge first-arrival window — no suspects will arrive.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(9999f, 9999f);
        ShiftManager.Instance?.TryStartShift();

        // Wait two frames for the shift ClientRpc and shiftStarted NetworkVariable to propagate.
        yield return null;
        yield return null;

        // End the shift immediately — this fires ShiftEnded() on Day_02 and begins
        // PostShiftSetupSequence() (megaphone bark → Vlad spawns out back).
        ShiftManager.Instance?.EndShift();

        Debug.Log("[DebugConsole] Skipped to end of Day 2 — post-shift Vlad cutscene starting.");
    }

    /// <summary>
    /// Skips to Day 2 with the opening Vlad sequence suppressed, arms Ocho's booth encounter
    /// (fake ID, antagonizing dialogue, verdict rejection, blackout) as the very next suspect,
    /// and auto-summons him a couple of seconds after the shift starts — bypassing the mutation
    /// and kill tutorials and the switch button entirely.
    /// </summary>
    public void SkipToOchoBoothEncounter()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToOchoBoothEncounter: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(2);
        StartCoroutine(SkipToOchoBoothEncounterAfterDelay());
    }

    private IEnumerator SkipToOchoBoothEncounterAfterDelay()
    {
        // Wait one frame for Day_02 to activate and subscribe its events.
        yield return null;

        if (Day_02.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToOchoBoothEncounter: Day_02.Instance not found after SkipToDay(2).");
            yield break;
        }

        _startShiftGate?.ForceIntroComplete();

        // Suppress the opening Vlad sequence (tool locker unlock etc.) and arm/auto-summon Ocho.
        Day_02.Instance.DebugSkipOpening();
        Day_02.Instance.DebugSkipToOchoBoothEncounter();

        const float OchoArrivalDelay = 2f;
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(OchoArrivalDelay, OchoArrivalDelay);
        ShiftManager.Instance?.TryStartShift();

        Debug.Log("[DebugConsole] Skipped to Ocho booth encounter — he will arrive in ~2 s.");
    }

    /// <summary>
    /// Skips to the start of Day 3 with the player positioned in front of the bunker,
    /// matching the natural wake-up spawn before the player walks in to begin their shift.
    /// JumpToDay is called first so the day NetworkVariable propagates to all clients
    /// before PlayShiftStartFanfare fires — this ensures the correct sky colour and
    /// Day 3 title card are shown (mirrors the SkipToDay / SkipToBoothReady pattern).
    /// </summary>
    public void SkipToStartOfDay3()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToStartOfDay3: CampaignManager not available — start the game first.");
            return;
        }

        CampaignManager.Instance.JumpToDay(3);
        ShiftManager.Instance.SkipToOutsideBunker();
    }

    /// <summary>
    /// Skips to Day 3 with the players placed in booth positions and the shift immediately
    /// started, then forces the power-outage sequence — the lights cut (fuse required), the
    /// phone rings, picking it up plays the scripted dialogue and registers the Repair Power
    /// task, and the local player is teleported to the power station with the tool locker
    /// force-unlocked for easy fuse-box testing.
    /// </summary>
    public void SkipToDay3PowerOutage()
    {
        if (CampaignManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToDay3PowerOutage: CampaignManager not available — start the game first.");
            return;
        }

        SkipToDay(3);
        StartCoroutine(SkipToDay3PowerOutageAfterDelay());
    }

    private IEnumerator SkipToDay3PowerOutageAfterDelay()
    {
        // Wait one frame for Day_03 to activate and subscribe its events.
        yield return null;

        if (Day_03.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToDay3PowerOutage: Day_03.Instance not found after SkipToDay(3).");
            yield break;
        }

        _startShiftGate?.ForceIntroComplete();
        FindFirstObjectByType<ToolsLocker>()?.DebugForceUnlock();

        // Start the shift with a large first-arrival window — no suspects will arrive.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(9999f, 9999f);
        ShiftManager.Instance?.TryStartShift();

        // Wait two frames for the shift ClientRpc and shiftStarted NetworkVariable to propagate.
        yield return null;
        yield return null;

        // Force the power outage sequence immediately (bypasses the normal last-suspect gate).
        Day_03.Instance.DebugTriggerPowerOutage();

        // Teleport to the power station so the fuse-box puzzle is immediately reachable.
        if (_powerStationSpawnPoint != null && PlayerInstance.Instance != null)
            PlayerInstance.Instance.SetPosition(_powerStationSpawnPoint);

        Debug.Log("[DebugConsole] Skipped to Day 3 — power outage sequence starting (fuse required).");
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

    /// <summary>
    /// Immediately force-triggers a mutant breach via <see cref="MutantBreachManager"/>,
    /// bypassing day gating, the once-per-day limit, and the random scheduling delay.
    /// Used by the F12 cheat console's "Trigger Mutant Breach" button.
    /// </summary>
    public void DebugForceMutantBreach()
    {
        if (MutantBreachManager.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] MutantBreachManager.Instance not found — is the manager in the scene and has the game started?");
            return;
        }

        MutantBreachManager.Instance.DebugForceTriggerBreach();
        Debug.Log("[DebugConsole] Mutant breach force-triggered via cheat console.");
    }

    /// <summary>
    /// Immediately shows the Thanks For Playing screen as if Day 7 just ended.
    /// Marks the campaign as complete so the shift sequence cannot resume afterward.
    /// </summary>
    public void SkipToEndOfDemo()
    {
        if (UIController.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SkipToEndOfDemo: UIController not available — start the game first.");
            return;
        }

        if (CampaignManager.Instance != null)
            CampaignManager.Instance.DebugForceCampaignComplete();

        UIController.Instance.ShowThanksForPlayingScreen();
        Debug.Log("[DebugConsole] Skipped to end of demo — Thanks For Playing screen shown.");
    }

    /// <summary>
    /// B — skips to Day 4 booth-ready, forces the Butcher's infection score to the fully-mutated
    /// threshold, locks the lineup to the Butcher only, and starts the shift with a short
    /// first-arrival window so he appears at the booth window in ~2 seconds.
    ///
    /// <see cref="SuspectController.ForceNextSuspectAsFullMutant"/> is set so the full-mutant
    /// mesh swap and <see cref="SuspectCharacter.BeginMutantBehavior"/> fire even if
    /// <see cref="SuspectData.fullMutantDialogue"/> is not yet wired up.
    /// </summary>
    private void ForceFullMutantButcher()
    {
        if (_butcherSuspectData == null)
        {
            Debug.LogWarning("[DebugConsole] ForceFullMutantButcher: _butcherSuspectData not assigned in Inspector (B).");
            return;
        }

        if (!GameManager.Instance.HasGameStarted)
        {
            EnsureGameStartedThen(ForceFullMutantButcher);
            return;
        }

        StartCoroutine(ForceFullMutantButcherRoutine());
    }

    private IEnumerator ForceFullMutantButcherRoutine()
    {
        // 1. Spike the Butcher's infection score to the fully-mutated threshold.
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(_butcherSuspectData);
        if (record != null)
        {
            record.infectionScore = AnomalyController.FULLY_MUTATED_THRESHOLD;
            Debug.Log($"[DebugConsole] Butcher infection score set to {AnomalyController.FULLY_MUTATED_THRESHOLD}.");
        }
        else
        {
            Debug.LogWarning("[DebugConsole] ForceFullMutantButcher: could not find SuspectRecord for the Butcher.");
        }

        // 2. Override the shift lineup to contain only the Butcher — self-clears after population.
        var manager = DailySuspectManager.Instance;
        if (manager != null)
        {
            manager.PopulateSuspectOverride = () =>
            {
                manager.PopulateSuspectOverride = null;
                manager.shiftSuspects.Add(_butcherSuspectData);
            };
        }

        // 3. Bypass the fullMutantDialogue null-check so the full-mutant path fires
        //    even if the dialogue hasn't been wired up on the SuspectData asset yet.
        SuspectController.ForceNextSuspectAsFullMutant = true;

        // 4. Skip to Day 4 with the player placed in the booth.
        SkipToDay(4);

        // 5. Wait one frame for Day 4 to activate.
        yield return null;

        // 6. Start the shift — Butcher arrives at the window in ~2 s.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(2f, 2f);
        ShiftManager.Instance?.TryStartShift();

        // 7. Wait an extra frame for TryStartShift RPCs to propagate before turning power on.
        yield return null;

        // 8. Ensure power is on — _isPowerOn is a NetworkVariable that persists across day
        //    skips, so any prior power outage (e.g. Day 3) would leave it false.
        //    Fall back to FindAnyObjectByType if the Inspector field was not assigned.
        ElectricityController ec = _electricityController != null
            ? _electricityController
            : FindAnyObjectByType<ElectricityController>();

        if (ec != null)
            ec.PowerOn();
        else
            Debug.LogWarning("[DebugConsole] ForceFullMutantButcher: ElectricityController not found — power state unchanged. Assign _electricityController in the Inspector.");

        Debug.Log("[DebugConsole] Full Mutant Butcher hack active — he arrives in ~2 s (B).");
    }

    /// <summary>
    /// Bootstraps the game if needed, skips to Day 3 with the shift running,
    /// then teleports the local player to the power station spawn point.
    /// Assign <see cref="_powerStationSpawnPoint"/> in the Inspector to the
    /// "Player Spawn Pos - At Power Station" scene object.
    /// </summary>
    public void TeleportToPowerStation()
    {
        if (_powerStationSpawnPoint == null)
        {
            Debug.LogWarning("[DebugConsole] TeleportToPowerStation: _powerStationSpawnPoint not assigned — assign it in the Inspector (P).");
            return;
        }

        EnsureGameStartedThen(() =>
        {
            SkipToDay(3);
            StartCoroutine(TeleportToPowerStationAfterDelay());
        });
    }

    private IEnumerator TeleportToPowerStationAfterDelay()
    {
        // Wait one frame for Day_03 to activate and subscribe its events.
        yield return null;

        _startShiftGate?.ForceIntroComplete();

        // Start the shift with a large first-arrival window so no suspects arrive immediately.
        ShiftManager.OverrideFirstArrivalInterval = new Vector2(9999f, 9999f);
        ShiftManager.Instance?.TryStartShift();

        // Wait two frames for the shift ClientRpc and shiftStarted NetworkVariable to propagate.
        yield return null;
        yield return null;

        if (PlayerInstance.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] TeleportToPowerStation: PlayerInstance.Instance not found after game start.");
            yield break;
        }

        PlayerInstance.Instance.SetPosition(_powerStationSpawnPoint);
        Debug.Log($"[DebugConsole] Teleported local player to power station at {_powerStationSpawnPoint.position} (P).");
    }

    /// <summary>
    /// Forces the booth window glass into the fully smashed state on the local client.
    /// Useful for testing the glass repair purchase flow without waiting for a mutant visit.
    /// </summary>
    public void SmashGlass()
    {
        if (BreakableGlassController.Instance == null)
        {
            Debug.LogWarning("[DebugConsole] SmashGlass: BreakableGlassController.Instance not found — is the Main scene loaded?");
            return;
        }

        BreakableGlassController.Instance.ForceSmash();
        Debug.Log("[DebugConsole] Glass force-smashed.");
    }

}
