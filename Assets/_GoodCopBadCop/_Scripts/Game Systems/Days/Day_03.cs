using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 — gore/body-part yard cleanup + end-of-shift power outage.
///
/// At day start, spawns gore and body-part junk items across the yard's
/// <see cref="TakeOutTrashTask"/> spawn zones (instead of standard trash), triggering the
/// Take Out Trash task so players must bag up every piece of gore. Each gore piece also drops
/// a blood decal, which arms <see cref="CleanBloodTask"/> so players must mop up every
/// splatter with the <see cref="Mop"/> as a separate task.
///
/// Right after the last suspect for the day is processed (before clock-out):
///   1. The power cuts out via a fuse-required outage (<see cref="ElectricityController.PowerOffFuseRequired"/>),
///      spawning fuses at the power station (<see cref="FuseSpawner"/>) and blocking the
///      in-booth quick-fix panels (<see cref="ElectricPanelController"/>, <see cref="CircuitBox"/>)
///      until the fuse box is solved.
///   2. After a brief pause the phone starts ringing — an "Answer the phone" objective
///      appears in the <see cref="TutorialObjectiveList"/>.
///   3. When answered, that objective completes, a new "go fix the power outage at the
///      power station" objective appears alongside a "find the fuse box" objective,
///      <see cref="ScriptedDialogueRunner.PlayMegaphoneDialogue"/> delivers lines
///      instructing the players to go fix the circuit box at the power station, and a
///      <see cref="RepairPowerThreat"/> is registered so the task appears in the guidebook.
///   4. Interacting with the <see cref="FuseBoxPuzzleController"/> completes "find the fuse
///      box" and adds a live "find and input N fuses X/N" objective that updates as fuses
///      are inserted or extracted (via <see cref="FuseBoxPuzzleController.OnFuseCountChanged"/>).
///   5. Once all fuses are inserted, that objective completes and a final "use the power
///      switch to restore power" objective appears.
///   6. Flipping the <see cref="PowerSwitch"/> restores power, which completes both the
///      "fix the power outage" and "use the power switch" objectives and resolves the threat.
/// </summary>
public class Day_03 : DayBase
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    public static Day_03 Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeAll();
    }

    // -------------------------------------------------------------------------
    // Day-specific override
    // -------------------------------------------------------------------------

    /// <summary>Day 3 hosts the fuse-box puzzle, so an intentional fuse-required outage
    /// should not be force-cleared by <see cref="DayBase.SupportsFuseBoxRestore"/>.</summary>
    protected override bool SupportsFuseBoxRestore => true;

    // -------------------------------------------------------------------------
    // Inspector — Bunker Exit Stinger
    // -------------------------------------------------------------------------

    [Header("Day 3 — Bunker Exit Stinger")]
    [Tooltip("One-shot stinger played the first time a player opens the bunker door and " +
             "exits for Day 3. Played locally on every client via SFXController, matching " +
             "the pattern used by UIController's transition stinger.")]
    [SerializeField] private AudioClip _bunkerExitStinger;

    [SerializeField] private float _bunkerExitStingerVolume = 1f;

    private bool _bunkerExitStingerPlayed;

    // -------------------------------------------------------------------------
    // Inspector — Power Outage Sequence
    // -------------------------------------------------------------------------

    [Header("Day 3 — Power Outage Sequence")]
    [Tooltip("The ElectricityController that governs booth power. PowerOff() is called " +
             "right after the last suspect for the day is processed.")]
    [SerializeField] private ElectricityController _electricityController;

    [Tooltip("Seconds after the last suspect is processed before the lights cut out. " +
             "Keep at 0 for an immediate, causally-linked blackout.")]
    [SerializeField] private float _powerOutageDelay = 0f;

    [Tooltip("Seconds after the power cuts before the phone starts ringing.")]
    [SerializeField] private float _phoneRingDelay = 3f;

    [Tooltip("Seconds to wait after the player picks up the phone before the scripted " +
             "dialogue starts — allows the grab animation to complete.")]
    [SerializeField] private float _grabAnimDelay = 1.5f;

    // -------------------------------------------------------------------------
    // Inspector — Phone Dialogue
    // -------------------------------------------------------------------------

    [Header("Day 3 — Power Outage Phone Call")]
    [Tooltip("ScriptedDialogue sequence played via ScriptedDialogueRunner when the player " +
             "answers the power-outage phone call. Lines should direct the player to the " +
             "power station to reset the circuit box.")]
    [SerializeField] private ScriptedDialogue _powerOutageCallDialogue;

    // -------------------------------------------------------------------------
    // Inspector — Power Station Puzzle
    // -------------------------------------------------------------------------

    [Header("Day 3 — Power Station Puzzle")]
    [Tooltip("The fuse box at the power station. Drives the fuse-box objective chain " +
             "via its OnBoxInteracted / OnFuseCountChanged events.")]
    [SerializeField] private FuseBoxPuzzleController _fuseBoxController;

    // -------------------------------------------------------------------------
    // Inspector — Objectives
    // -------------------------------------------------------------------------

    [Header("Day 3 — Objectives")]
    [SerializeField] private string _answerPhoneObjectiveText = "Answer the phone";
    [SerializeField] private string _fixPowerObjectiveText = "Go fix the power outage at the power station";
    [SerializeField] private string _findFuseBoxObjectiveText = "Find the fuse box";
    [Tooltip("Format string for the live fuse-counter objective. {0} = total fuses, {1} = fuses inserted so far.")]
    [SerializeField] private string _fuseCountObjectiveFormat = "Find and input {0} fuses {1}/{0}";
    [SerializeField] private string _useSwitchObjectiveText = "Use the power switch to restore power";

    private TutorialObjectiveItem _answerPhoneObjective;
    private TutorialObjectiveItem _fixPowerObjective;
    private TutorialObjectiveItem _findFuseBoxObjective;
    private TutorialObjectiveItem _fuseCountObjective;
    private TutorialObjectiveItem _useSwitchObjective;
    private RepairPowerThreat _repairPowerThreat;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Arm the Clean Blood task BEFORE spawning gore so every blood decal spawned
        // alongside it this cycle gets registered (see CleanBloodTask.TriggerTask doc comment).
        CleanBloodTask.Instance?.TriggerTask();
        TakeOutTrashTask.Instance?.TriggerTask(useGorePrefabs: true);

        // Arms the Mutant Ocho / Vlad-corpse roof cutscene right as the player exits the
        // bunker for Day 3 — see OchoEatingVladCutscene for the full sequence.
        OchoEatingVladCutscene.Instance?.TriggerTask();

        // Plays a one-shot stinger the first time a player opens the bunker door and steps
        // outside for Day 3. Reset per-day so a re-activation (e.g. debug skip) can replay it.
        _bunkerExitStingerPlayed = false;
        BunkerDoorController.OnDoorOpened += OnBunkerDoorOpenedFirstTime;

        // Subscribe on ALL clients — these drive local UI (TutorialObjectiveList) and
        // task-registry state, so every client must react independently.
        Telephone.OnScriptedCallAnsweredAllClients += OnScriptedCallAnsweredAllClients;
        Telephone.OnRingStarted += OnPhoneRingStartedAllClients;

        if (_electricityController != null)
            _electricityController.OnPowerRestoredAllClients += OnElectricityPowerRestoredAllClients;

        if (_fuseBoxController != null)
        {
            _fuseBoxController.OnBoxInteracted += OnFuseBoxInteractedAllClients;
            _fuseBoxController.OnFuseCountChanged += OnFuseCountChangedAllClients;
        }

        // Only the server drives the power-outage trigger, right after the last suspect for
        // the day is processed — before clock-out is enabled.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            ShiftManager.OnLastSuspectProcessed += OnLastSuspectProcessedServer;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        UnsubscribeAll();
        StopAllCoroutines();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    private void UnsubscribeAll()
    {
        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpenedFirstTime;
        ShiftManager.OnLastSuspectProcessed -= OnLastSuspectProcessedServer;
        Telephone.OnScriptedCallAnsweredAllClients -= OnScriptedCallAnsweredAllClients;
        Telephone.OnRingStarted -= OnPhoneRingStartedAllClients;

        if (_electricityController != null)
            _electricityController.OnPowerRestoredAllClients -= OnElectricityPowerRestoredAllClients;

        if (_fuseBoxController != null)
        {
            _fuseBoxController.OnBoxInteracted -= OnFuseBoxInteractedAllClients;
            _fuseBoxController.OnFuseCountChanged -= OnFuseCountChangedAllClients;
        }
    }

    // -------------------------------------------------------------------------
    // Power outage sequence (server-only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on the server by <see cref="ShiftManager.OnLastSuspectProcessed"/> the instant
    /// the final suspect for the day walks away — before clock-out is enabled. Starts the
    /// power-outage sequence so the blackout feels like a direct consequence of finishing
    /// the shift's suspects.
    /// </summary>
    private void OnLastSuspectProcessedServer()
    {
        ShiftManager.OnLastSuspectProcessed -= OnLastSuspectProcessedServer;
        StartCoroutine(PowerOutageSequence());
    }

    private IEnumerator PowerOutageSequence()
    {
        yield return new WaitForSeconds(_powerOutageDelay);

        if (_electricityController != null)
        {
            // Fuse-required outage: cuts power, blocks the in-booth quick-fix panels, and
            // triggers FuseSpawner to scatter fuses at the power station.
            _electricityController.PowerOffFuseRequired();
        }
        else
        {
            Debug.LogWarning("[Day_03] _electricityController is not assigned — power outage skipped.");
        }

        yield return new WaitForSeconds(_phoneRingDelay);

        if (Telephone.Instance != null)
        {
            Telephone.Instance.TriggerScriptedCall(OnPowerOutageCallAnswered);
            Debug.Log("[Day_03] Power outage phone call triggered.");
        }
        else
        {
            Debug.LogWarning("[Day_03] Telephone.Instance is null — cannot ring phone.");
        }
    }

    /// <summary>
    /// Fired on the server the moment a player answers the ringing phone.
    /// Waits for the grab animation, then plays the scripted dialogue.
    /// </summary>
    private void OnPowerOutageCallAnswered()
    {
        StartCoroutine(PlayPowerOutageDialogue());
    }

    private IEnumerator PlayPowerOutageDialogue()
    {
        yield return new WaitForSeconds(_grabAnimDelay);

        if (_powerOutageCallDialogue == null)
        {
            Debug.LogWarning("[Day_03] _powerOutageCallDialogue is not assigned — skipping dialogue.");
            yield break;
        }

        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning("[Day_03] ScriptedDialogueRunner.Instance is null — skipping dialogue.");
            yield break;
        }

        // unlocked: true — player keeps free movement while the voice plays,
        // matching the feel of listening on a handset rather than a face-to-face conversation.
        ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(
            _powerOutageCallDialogue,
            onComplete: null,
            unlocked: true);

        Debug.Log("[Day_03] Power outage scripted dialogue started.");
    }

    // -------------------------------------------------------------------------
    // Bunker exit stinger — all clients
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on every client (via <see cref="BunkerDoorController.OnDoorOpened"/>) the moment
    /// the bunker door swings open. Since the door is force-closed at the start of every day
    /// (see <see cref="ShiftManager.InBetweenShiftSequence"/> / <see cref="BunkerDoorController.OnDayChanged"/>),
    /// the first invocation each Day 3 always corresponds to the player's first exit of the day.
    /// Unsubscribes immediately so later door-opens that day (e.g. going back in and out) stay silent.
    /// </summary>
    private void OnBunkerDoorOpenedFirstTime()
    {
        if (_bunkerExitStingerPlayed) return;
        _bunkerExitStingerPlayed = true;

        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpenedFirstTime;

        if (_bunkerExitStinger == null)
        {
            Debug.LogWarning("[Day_03] _bunkerExitStinger is not assigned — skipping stinger playback.");
            return;
        }

        SFXController.Instance?.Play(_bunkerExitStinger, _bunkerExitStingerVolume);
    }

    // -------------------------------------------------------------------------
    // Objective + task registration — all clients
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on ALL clients (via <see cref="Telephone.OnRingStarted"/>) the moment the
    /// scripted power-outage call starts ringing. Adds the "answer the phone" objective.
    /// </summary>
    private void OnPhoneRingStartedAllClients()
    {
        Telephone.OnRingStarted -= OnPhoneRingStartedAllClients;
        _answerPhoneObjective = TutorialObjectiveList.Instance?.AddObjective(_answerPhoneObjectiveText);
    }

    /// <summary>
    /// Fired on ALL clients when the scripted power-outage call is answered. Completes the
    /// "answer the phone" objective, adds the "fix the power outage" objective, and
    /// registers <see cref="RepairPowerThreat"/> in the local <see cref="TaskRegistry"/>
    /// so every player sees the task in their guidebook.
    /// </summary>
    private void OnScriptedCallAnsweredAllClients()
    {
        Telephone.OnScriptedCallAnsweredAllClients -= OnScriptedCallAnsweredAllClients;

        if (_answerPhoneObjective != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_answerPhoneObjective);
            _answerPhoneObjective = null;
        }

        _fixPowerObjective = TutorialObjectiveList.Instance?.AddObjective(_fixPowerObjectiveText);
        _findFuseBoxObjective = TutorialObjectiveList.Instance?.AddObjective(_findFuseBoxObjectiveText);

        _repairPowerThreat = new RepairPowerThreat();
        TaskRegistry.Instance?.AddThreat(_repairPowerThreat);
        Debug.Log("[Day_03] RepairPowerThreat registered in TaskRegistry.");
    }

    /// <summary>
    /// Fired on ALL clients (via <see cref="FuseBoxPuzzleController.OnBoxInteracted"/>) the
    /// first time a player opens the fuse box. Completes "find the fuse box" and adds the
    /// live fuse-counter objective.
    /// </summary>
    private void OnFuseBoxInteractedAllClients()
    {
        if (_findFuseBoxObjective == null) return;

        if (_fuseBoxController != null)
            _fuseBoxController.OnBoxInteracted -= OnFuseBoxInteractedAllClients;

        TutorialObjectiveList.Instance?.CompleteObjective(_findFuseBoxObjective);
        _findFuseBoxObjective = null;

        int total = _fuseBoxController != null ? _fuseBoxController.FuseSlotCount : 3;
        _fuseCountObjective = TutorialObjectiveList.Instance?.AddObjective(
            string.Format(_fuseCountObjectiveFormat, total, 0));
    }

    /// <summary>
    /// Fired on ALL clients (via <see cref="FuseBoxPuzzleController.OnFuseCountChanged"/>)
    /// whenever a fuse is inserted or extracted. Updates the live counter text, and once
    /// every slot is filled, completes it and adds the final "use the power switch" objective.
    /// </summary>
    private void OnFuseCountChangedAllClients(int filled, int total)
    {
        if (_fuseCountObjective == null) return;

        if (filled >= total)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_fuseCountObjective);
            _fuseCountObjective = null;
            _useSwitchObjective = TutorialObjectiveList.Instance?.AddObjective(_useSwitchObjectiveText);
        }
        else
        {
            TutorialObjectiveList.Instance?.UpdateObjective(
                _fuseCountObjective, string.Format(_fuseCountObjectiveFormat, total, filled));
        }
    }

    /// <summary>
    /// Fired on ALL clients (via <see cref="ElectricityController.OnPowerRestoredAllClients"/>)
    /// whenever power turns back on. Only acts if the "fix the power outage" objective is
    /// still active, so unrelated power-on events (e.g. a later day) are ignored.
    /// </summary>
    private void OnElectricityPowerRestoredAllClients()
    {
        if (_fixPowerObjective == null) return;

        TutorialObjectiveList.Instance?.CompleteObjective(_fixPowerObjective);
        _fixPowerObjective = null;

        if (_useSwitchObjective != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_useSwitchObjective);
            _useSwitchObjective = null;
        }

        _repairPowerThreat?.Resolve();
        _repairPowerThreat = null;

        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f);
        Debug.Log("[Day_03] Power restored at the power station — RepairPowerThreat resolved.");
    }

    // -------------------------------------------------------------------------
    // Fix Perimeter Fences Tutorial
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Debug
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces the power outage sequence to start immediately, bypassing the
    /// <see cref="ShiftManager.OnLastSuspectProcessed"/> gate. Server-only.
    /// Called by <see cref="DebugConsole"/> to test the sequence without running
    /// through the full suspect lineup.
    /// </summary>
    public void DebugTriggerPowerOutage()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        ShiftManager.OnLastSuspectProcessed -= OnLastSuspectProcessedServer;
        StopAllCoroutines();
        StartCoroutine(PowerOutageSequence());
        Debug.Log("[Day_03] DebugTriggerPowerOutage: power outage sequence forced.");
    }
}

