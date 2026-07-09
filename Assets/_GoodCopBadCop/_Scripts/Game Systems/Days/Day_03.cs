using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 — booth mess cleanup + end-of-shift power outage.
///
/// Activates the booth mess left from the previous shift and triggers the
/// Clean Booth Mess task at day start so players must scrub all blood splatters
/// and bag all junk items.
///
/// Also plays a one-shot stinger the first time the booth door is opened on
/// this day, giving the players a dramatic reveal of the mess.
///
/// At the end of the shift, right after the last suspect is processed:
///   1. The power cuts out (via <see cref="ElectricityController"/>).
///   2. After a brief pause the phone starts ringing.
///   3. When answered, <see cref="ScriptedDialogueRunner.PlayMegaphoneDialogue"/>
///      delivers lines instructing the players to go fix the circuit box at the
///      power station.
///   4. A <see cref="RepairPowerThreat"/> is registered on all clients so the
///      task appears in the guidebook.
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
        TimecardMachine.OnClockOutServer -= OnClockOutServer;
        Telephone.OnScriptedCallAnsweredAllClients -= OnScriptedCallAnsweredAllClients;
    }

    // -------------------------------------------------------------------------
    // Inspector — Booth Mess
    // -------------------------------------------------------------------------

    [Header("Day 3 — Booth Mess")]
    [Tooltip("The Clean Booth Mess task — triggered at the start of Day 3. Server-only.")]
    [SerializeField] private CleanBoothMessTask _cleanBoothMessTask;

    // -------------------------------------------------------------------------
    // Inspector — Door Stinger
    // -------------------------------------------------------------------------

    [Header("Day 3 — Door Stinger")]
    [Tooltip("The booth door whose first open triggers the stinger.")]
    [SerializeField] private DoorController _boothDoor;

    [Tooltip("Stinger played on all clients the first time the booth door opens on Day 3.")]
    [SerializeField] private AudioClip _boothMessStinger;

    // -------------------------------------------------------------------------
    // Inspector — Power Outage Sequence
    // -------------------------------------------------------------------------

    [Header("Day 3 — Power Outage Sequence")]
    [Tooltip("The ElectricityController that governs booth power. PowerOff() is called " +
             "when the player punches the timecard machine.")]
    [SerializeField] private ElectricityController _electricityController;

    [Tooltip("Seconds after the timecard punch before the lights cut out. " +
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
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        _cleanBoothMessTask?.TriggerTask();

        if (_boothDoor != null && _boothMessStinger != null)
            _boothDoor.OnDoorOpened += OnBoothDoorFirstOpened;

        // Subscribe on ALL clients so each client's TaskRegistry receives the threat.
        Telephone.OnScriptedCallAnsweredAllClients += OnScriptedCallAnsweredAllClients;

        // Only the server drives the power-outage trigger.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            TimecardMachine.OnClockOutServer += OnClockOutServer;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        if (_boothDoor != null)
            _boothDoor.OnDoorOpened -= OnBoothDoorFirstOpened;

        TimecardMachine.OnClockOutServer -= OnClockOutServer;
        Telephone.OnScriptedCallAnsweredAllClients -= OnScriptedCallAnsweredAllClients;

        StopAllCoroutines();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Door stinger
    // -------------------------------------------------------------------------

    private void OnBoothDoorFirstOpened()
    {
        _boothDoor.OnDoorOpened -= OnBoothDoorFirstOpened;
        SFXController.Instance?.Play(_boothMessStinger);
    }

    // -------------------------------------------------------------------------
    // Power outage sequence (server-only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on the server by <see cref="TimecardMachine.OnClockOutServer"/> the instant
    /// the player punches the timecard. Starts the power-outage sequence so the blackout
    /// feels like a direct consequence of clocking out.
    /// </summary>
    private void OnClockOutServer()
    {
        TimecardMachine.OnClockOutServer -= OnClockOutServer;
        StartCoroutine(PowerOutageSequence());
    }

    private IEnumerator PowerOutageSequence()
    {
        yield return new WaitForSeconds(_powerOutageDelay);

        if (_electricityController != null)
            _electricityController.PowerOff();
        else
            Debug.LogWarning("[Day_03] _electricityController is not assigned — power outage skipped.");

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
    // Task registration — all clients
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on ALL clients when the scripted power-outage call is answered.
    /// Registers <see cref="RepairPowerThreat"/> in the local <see cref="TaskRegistry"/>
    /// so every player sees the task in their guidebook.
    /// </summary>
    private void OnScriptedCallAnsweredAllClients()
    {
        Telephone.OnScriptedCallAnsweredAllClients -= OnScriptedCallAnsweredAllClients;

        var threat = new RepairPowerThreat();
        TaskRegistry.Instance?.AddThreat(threat);
        Debug.Log("[Day_03] RepairPowerThreat registered in TaskRegistry.");
    }

    // -------------------------------------------------------------------------
    // Debug
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces the power outage sequence to start immediately, bypassing the
    /// <see cref="TimecardMachine.OnClockOutServer"/> gate. Server-only.
    /// Called by <see cref="DebugConsole"/> to test the sequence without running
    /// through the full suspect lineup.
    /// </summary>
    public void DebugTriggerPowerOutage()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        TimecardMachine.OnClockOutServer -= OnClockOutServer;
        StopAllCoroutines();
        StartCoroutine(PowerOutageSequence());
        Debug.Log("[Day_03] DebugTriggerPowerOutage: power outage sequence forced.");
    }
}
