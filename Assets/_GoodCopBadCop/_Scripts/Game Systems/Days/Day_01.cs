using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Day 1 scripted opening sequence.
///
/// Flow:
///   1. Day activates — all stamps, drawer, and notebooks are immediately unlocked.
///      Mutation and biological notebooks are hidden until Day 2/3 introduce them.
///   2. Intro cutscene ends and the Day number pop-up plays → <see cref="ShiftManager.OnDayStart"/>
///      fires → <see cref="OnDayStarted"/> runs. A tutorial arrow points the player to the
///      Time Card Machine and clock-in is enabled server-side via <see cref="TimecardMachine.EnableClockIn"/>.
///   3. Once the player clocks in (<see cref="TimecardMachine.OnClockInServer"/>), the tutorial
///      arrow is dismissed and, after a short hold, the rolling shutter opens automatically and
///      is locked open via <see cref="ShutterController.ShutterLockedOpen"/>.
///   4. The shift starts automatically (no switch press required on Day 1).
///      <see cref="SuspectController.InterceptNextSuspectSpawn"/> is armed so the
///      first suspect slot calls <see cref="SuspectController.SpawnScriptedSuspect"/>
///      with Vlad's prefab.
///   5. Vlad walks to the booth window without handing over any documents.
///   6. Once Vlad arrives, a <see cref="ScriptedDialogue"/> sequence plays through
///      <see cref="ScriptedDialogueRunner"/>. The suspect camera is active for the
///      entire duration of the conversation.
/// </summary>
public class Day_01 : DayBase
{
    /// <summary>Singleton reference — set in Awake, cleared on destroy.</summary>
    public static Day_01 Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
    [Header("Day 1 — Fire Barrel")]
    [Tooltip("The yard fire barrel — lit for the entirety of Day 1, extinguished at the start of Day 2.")]
    [SerializeField] private FirePit _fireBarrel;

    [Header("Day 1 — Booth")]
    [Tooltip("The booth drawer the player can open freely on Day 1.")]
    [SerializeField] private Drawer _drawer;

    [Tooltip("The stack of folders in the drawer — locked until the folder tutorial step begins.")]
    [SerializeField] private StackOfFolders _stackOfFolders;

    [Tooltip("The green ink stamp station.")]
    [SerializeField] private InkStamp _greenStampSlot;

    [Tooltip("The yellow ink stamp station.")]
    [SerializeField] private InkStamp _yellowStampSlot;

    [Tooltip("The red ink stamp station.")]
    [SerializeField] private InkStamp _redStampSlot;

    [Tooltip("The Documentation exam notebook.")]
    [SerializeField] private ExamNotebook _examNotebook;

    [Header("Day 1 — Vlad Arrival")]
    [Tooltip("Vlad's SuspectCharacter prefab — spawned as the first visitor on Day 1 via SuspectController.")]
    [SerializeField] private SuspectCharacter _vladPrefab;

    [Header("Day 1 — Clock In Tutorial")]
    [Tooltip("The Time Card Machine the player must clock in on before the shutter opens and Vlad is summoned.")]
    [SerializeField] private TimecardMachine _timeCardMachine;

    [Tooltip("World-space tutorial arrow pointing at the Time Card Machine. Shown immediately after the " +
             "Day number pop-up plays and hidden the moment the player clocks in.")]
    [SerializeField] private GameObject _clockInTutorialArrow;

    [Header("Day 1 — Soldier")]
    [Tooltip("The Soldier's SuspectCharacter placed directly in the scene (not runtime-spawned). " +
             "IMPORTANT: Must stay ACTIVE in the scene at load time so NGO registers his in-scene " +
             "NetworkObject for every client. To keep him hidden until his sequence fires, enable " +
             "'_hiddenUntilRevealed' on his SuspectCharacter instead of deactivating the GameObject — " +
             "IntroduceSceneSuspect calls RevealVisuals() (replicated to all clients) and teleports him " +
             "to the spawn point, then he walks in like a normal suspect.")]
    [SerializeField] private SuspectCharacter _soldierCharacter;

    /// <summary>
    /// Exposes the scene-placed Soldier so <see cref="CampaignManager"/> can despawn him
    /// once any day other than Day 1 becomes active — he's only ever needed for this one
    /// scripted moment. See <see cref="CampaignManager.ApplyDay"/>.
    /// </summary>
    public SuspectCharacter SoldierCharacter => _soldierCharacter;

    [Tooltip("Scripted dialogue sequence to play once the Soldier sequence begins.")]
    [SerializeField] private ScriptedDialogue _soldierDialogue;

    [Tooltip("Seconds after the Soldier sequence is triggered before his dialogue begins.")]
    [SerializeField] private float _soldierDialogueStartDelay = 1.2f;

    [Tooltip("Megaphone scripted dialogue that plays immediately after the Alexei murder cutscene ends. " +
             "Typically a single instruction line, e.g. 'Use the lever to close the window shutter!'")]
    [SerializeField] private ScriptedDialogue _leverDialogue;

    [Tooltip("Megaphone scripted dialogue that plays after Alexei despawns — the closing remarks for the shift.")]
    [SerializeField] private ScriptedDialogue _postAlexeiDialogue;

    [Tooltip("The booth lever — animated to the open position when the shutter opens and locked " +
             "non-interactable until the megaphone instructs the player to use it.")]
    [SerializeField] private Lever _lever;

    [Tooltip("World-space tutorial arrow pointing at the booth lever. Shown once the lever megaphone " +
             "dialogue finishes and hidden the moment the player pulls the lever. Pre-placed in the " +
             "scene (like _clockInTutorialArrow) rather than spawned from the pooled TutorialMarker system, " +
             "since the pooled marker's hover offset was landing in the wrong spot for this object.")]
    [SerializeField] private Transform _leverTutorialArrow;

    [Tooltip("Custom stand position for the Soldier at the booth window. " +
             "Overrides SuspectController's default standPos for the soldier's walk-in.")]
    [SerializeField] private Transform _soldierBoothPos;

    [Header("Day 1 — Trash Task Tutorial")]
    [Tooltip("Base label shown in the objective list for the end-of-shift trash task. " +
             "The deposited/total count is appended automatically, e.g. 'Throw away trash 2/10'.")]
    [SerializeField] private string _taskThrowTrashText = "Throw away trash";

    [Tooltip("Objective label shown after the trash task is done, prompting the player to open the bunker door.")]
    [SerializeField] private string _taskOpenBunkerText = "Open the bunker";

    [Tooltip("Objective label shown after the trash and graffiti tasks are done, prompting the player " +
             "to clock out on the Time Card Machine before heading to the bunker.")]
    [SerializeField] private string _taskClockOutText = "Clock out for the day";

    [Tooltip("ShopItem.Name of the Documentation Exam pile — used to make it free during the tutorial.")]
    [SerializeField] private string _documentationExamItemName = "Documentation Exam";

    [Tooltip("The Documentation Exam shop item — locked until the documentation tutorial begins.")]
    [SerializeField] private ShopItem _documentationExamShopItem;

    [Tooltip("Number of documentation anomalies to force-activate on the random quarantine tutorial suspect (index 2) for the Day 1 tutorial.")]
    [FormerlySerializedAs("_ivanDocumentationAnomalyCount")]
    [SerializeField] private int _quarantineDocumentationAnomalyCount = 5;

    [Tooltip("Task text shown while the player needs to get a documentation checklist.")]
    [SerializeField] private string _taskGetChecklistText = "Get a documentation checklist from the drawer";

    [Tooltip("Task text shown while the player needs to check documentation anomalies and file the page.")]
    [SerializeField] private string _taskCheckDocumentationText = "Check documentation anomalies and add the page to the folder";

    [Tooltip("First megaphone scripted dialogue — plays when the player first picks up one of the quarantine tutorial suspect's documents.")]
    [FormerlySerializedAs("_ivanMegaphonePart1")]
    [SerializeField] private ScriptedDialogue _quarantineMegaphonePart1;

    [Tooltip("Second megaphone scripted dialogue — plays after the player picks up the documentation checklist.")]
    [FormerlySerializedAs("_ivanMegaphonePart2")]
    [SerializeField] private ScriptedDialogue _quarantineMegaphonePart2;

    [Tooltip("Seconds after the player clocks in before the shutter opens and Vlad is triggered.")]
    [SerializeField] private float _shutterOpenDelay = 7f;

    [Header("Day 1 — Vlad Dialogue")]
    [Tooltip("Scripted dialogue sequence to play once Vlad arrives at the booth window.")]
    [SerializeField] private ScriptedDialogue _vladDialogue;

    [Tooltip("Seconds after Vlad arrives at the window before his dialogue sequence begins. " +
             "Should exceed the 0.5 s rotation so he is fully facing the player.")]
    [SerializeField] private float _vladDialogueStartDelay = 1.2f;

    [Header("Day 1 — Vlad Paperwork Tutorial")]
    [Tooltip("Seconds after the scripted dialogue completes before Vlad's papers are placed on the desk.")]
    [SerializeField] private float _vladPaperworkDelay = 0.6f;

    [Tooltip("Seconds after the papers land before Vlad delivers his paperwork bark.")]
    [SerializeField] private float _vladPaperworkBarkDelay = 1.2f;

    [Tooltip("What Vlad says when he drops his documents.")]
    [TextArea(2, 4)]
    [SerializeField] private string _vladPaperworkBark =
        "Papers. ID card, application form — pick them up, have a look.";

    [Tooltip("What Vlad says once the player picks up both documents.")]
    [TextArea(2, 4)]
    [SerializeField] private string _vladFolderBark =
        "Good. Now grab a folder from that drawer — file them in.";

    [Tooltip("What Vlad says once the player places the folder on the desk.")]
    [TextArea(2, 4)]
    [SerializeField] private string _vladFolderPlacedBark =
        "File the documents into the folder.";

    [Tooltip("The PlacementBoard on the office desk — used to detect when the player places the folder down.")]
    [SerializeField] private PlacementBoard _deskPlacementBoard;

    [Header("Day 1 — Stamp Permission")]
    [Tooltip("What Vlad says after both documents are filed, granting permission to use the green stamp.")]
    [TextArea(2, 4)]
    [SerializeField] private string _vladStampPermissionBark =
        "Green stamp — use it to approve them. Go ahead.";

    [Tooltip("Seconds after both documents are filed before Vlad delivers the stamp permission bark.")]
    [SerializeField] private float _vladStampBarkDelay = 1f;

    [Header("Day 1 — Hand-off Instruction")]
    [Tooltip("What Vlad says after the folder is stamped, instructing the player to place it at the window.")]
    [TextArea(2, 4)]
    [SerializeField] private string _vladHandOffBark =
        "Now put the folder at the window. Deliver the verdict — let him through.";

    [Tooltip("Seconds after the stamp completes before Vlad delivers the hand-off bark.")]
    [SerializeField] private float _vladHandOffBarkDelay = 1f;

    [Tooltip("The PlacementBoard at the window hand-off point — detects when the player places the folder there.")]
    [SerializeField] private PlacementBoard _windowPlacementBoard;

    [Header("Day 1 — Closing Dialogue")]
    [Tooltip("Scripted dialogue that plays once the player places the folder at the window.")]
    [SerializeField] private ScriptedDialogue _vladClosingDialogue;

    [Tooltip("Seconds between the folder being placed at the window and the closing dialogue starting.")]
    [SerializeField] private float _vladClosingDialogueDelay = 0.8f;

    [Tooltip("Seconds after both documents are picked up before Vlad delivers the folder bark.")]
    [SerializeField] private float _vladFolderBarkDelay = 0.8f;

    [Header("Day 1 — Tutorial Task HUD")]
    [Tooltip("HUD task text shown while the player needs to grab a folder and place it on the desk.")]
    [SerializeField] private string _taskFolderDocs = "Grab a folder and place it on the desk";

    [Tooltip("HUD task text shown while the player needs to place the documents inside the folder.")]
    [SerializeField] private string _taskPlaceDocsText = "Place documents in folder";

    [Tooltip("HUD task text shown while the player needs to stamp the folder with the green stamp.")]
    [SerializeField] private string _taskStampFolder = "Stamp the folder with the green stamp";

    [Tooltip("HUD task text shown while the player needs to place the folder at the window slot.")]
    [SerializeField] private string _taskHandOffText = "Place folder at the window";

    [Header("Day 1 — Post-Vlad ATM Tasks")]
    [Tooltip("HUD task text shown while the player needs to collect the payout coupons from the ATM.")]
    [SerializeField] private string _taskCollectCouponsText = "Collect coupons at ATM";

    [Tooltip("HUD task text shown after all coupons are collected, prompting the player to press the " +
             "switch button to call the first real subject.")]
    [SerializeField] private string _taskPressButtonText = "Press button to call next subject";

    [Header("Day 1 — Tutorial Markers")]
    [Tooltip("World-space point above Vlad's document drop zone — marker shown during 'pick up docs' task.")]
    [SerializeField] private Transform _markerPickUpDocs;

    [Tooltip("World-space point above the booth drawer — marker shown during 'grab folder and place on desk' task.")]
    [SerializeField] private Transform _markerDrawer;

    [Tooltip("World-space point above the desk placement board — marker shown during 'place docs in folder' task.")]
    [SerializeField] private Transform _markerDeskBoard;

    [Tooltip("World-space point above the green stamp slot — marker shown during 'stamp the folder' task.")]
    [SerializeField] private Transform _markerGreenStamp;

    [Tooltip("World-space point above the window hand-off board — marker shown during 'place folder at window' task.")]
    [SerializeField] private Transform _markerWindowBoard;

    [Tooltip("World-space point above the ATM — marker shown during 'collect coupons' task.")]
    [SerializeField] private Transform _markerATM;

    [Tooltip("World-space point above the switch button — marker shown during 'press button to call next subject' task.")]
    [SerializeField] private Transform _markerSwitchButton;

    [Tooltip("World-space point above the documentation exam shop item — marker shown during 'get checklist' task.")]
    [SerializeField] private Transform _markerDocumentationExam;

    [Tooltip("World-space point above the exam notebook — marker shown during 'check documentation anomalies' task.")]
    [SerializeField] private Transform _markerExamNotebook;

    [Tooltip("World-space point above the yellow stamp slot — marker shown during 'grab quarantine stamp' task.")]
    [SerializeField] private Transform _markerYellowStamp;

    [Tooltip("World-space point above the bunker door — marker shown during 'Open the bunker' end-of-shift task.")]
    [SerializeField] private Transform _markerBunkerDoor;

    [Tooltip("World-space point above the bunk bed — marker shown after the bunker door opens, pointing the player to bed.")]
    [SerializeField] private Transform _markerBunkBed;

    [Tooltip("The bunker door's Interactable — highlighted while the 'Open the bunker' tutorial task is active.")]
    [SerializeField] private BunkerDoorInteractable _bunkerDoorInteractable;

    [Tooltip("The bunk bed's Interactable — highlighted after the bunker door opens, guiding the player to sleep.")]
    [SerializeField] private BunkBedInteractable _bunkBedInteractable;

    [Header("Day 1 — Quarantine Tutorial")]
    [Tooltip("Task text shown while the player needs to grab the quarantine stamp.")]
    [SerializeField] private string _taskGrabQuarantineStampText = "Grab the quarantine stamp";

    [Tooltip("Task text shown while the player needs to place the folder at the window after stamping.")]
    [SerializeField] private string _taskQuarantineHandOffText = "Place folder at the window";

    [Header("Other Day Notebooks — Hidden During Day 1")]
    [Tooltip("The Mutation Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    // Guards against OnDayStarted running more than once if OnDayStart fires twice.
    private bool _dayStartedFired = false;

    // Set by DebugSkipToSoldierSlot to prevent Day1OpeningSequence from running
    // when TryStartShift subsequently fires OnDayStart.
    private bool _debugSkipActive = false;

    // Tracks which of Vlad's two documents have been picked up so far.
    private PickableObject _vladIDCard;
    private PickableObject _vladAppForm;
    private int _docsPickedUp;

    // Active quarantine documentation tutorial tasks.
    private TutorialObjectiveItem _taskGetChecklist;
    private TutorialObjectiveItem _taskCheckDocumentation;

    // The suspect captured when index 2 arrives — used to verify that a later
    // OnPaperworkSpawned event actually belongs to this suspect before wiring the
    // documentation tutorial's pickup triggers. See OnQuarantineSuspectPaperworkSpawned.
    private SuspectCharacter _quarantineTargetSuspect;

    // Tracks which of the quarantine tutorial suspect's two documents have been spawned —
    // used to wire pickup triggers.
    private PickableObject _quarantineDoc1;
    private PickableObject _quarantineDoc2;

    // Tracks how many of Vlad's documents have been filed into a folder.
    private int _docsFiledCount;

    // Active tutorial task entries — created when each step begins, completed when done.
    // Note: the initial "pick up Vlad's documents" step is intentionally NOT added to the
    // objective list (see ShowVladPickUpTask) — only the world-space marker guides it.
    private TutorialObjectiveItem _taskFolder;
    private TutorialObjectiveItem _taskFile;
    private TutorialObjectiveItem _taskStamp;
    private TutorialObjectiveItem _taskHandOff;

    // Active quarantine tutorial tasks (index-2 suspect: stamp + hand-off sequence).
    private TutorialObjectiveItem _taskQuarantineStamp;
    private TutorialObjectiveItem _taskQuarantineHandOff;

    // Post-Vlad ATM / button tasks shown after his closing dialogue.
    private TutorialObjectiveItem _taskCollectCoupons;
    private TutorialObjectiveItem _taskPressButton;

    // End-of-shift trash task shown after the Alexei sequence.
    private TutorialObjectiveItem _taskThrowTrash;
    private TutorialObjectiveItem _taskClockOut;
    private TutorialObjectiveItem _taskOpenBunker;

    // End-of-shift graffiti task, shown alongside the trash task (see OnTrashTaskReadySync).
    // Owned here (rather than inside CleanGraffitiTask) so this day script controls exactly
    // when the row appears/disappears in the shared TutorialObjectiveList.
    private TutorialObjectiveItem _taskCleanGraffiti;

    // Both the trash and graffiti objectives are added to the same shared
    // TutorialObjectiveList around the same time (see OnTrashTaskReadySync).
    // These flags gate HideAndClear so the whole list isn't cleared out from
    // under a still-in-progress task when the other one finishes first.
    private bool _trashTaskDone;
    private bool _graffitiTaskDone;

    // "Process N subjects" counter task — shown after Vlad's tutorial sequence ends.
    private const int SubjectsToProcess = 5;
    private int _subjectProcessedCount;
    private TutorialObjectiveItem _taskSubjectCount;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        Debug.Log($"[Day_01] DayActivated — IsServer={NetworkManager.Singleton?.IsServer ?? false}  IsClient={NetworkManager.Singleton?.IsClient ?? false}");

        // Light the yard fire barrel for the entirety of Day 1. Extinguished when Day 2 starts.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _fireBarrel?.Ignite();

        _dayStartedFired = false;
        _debugSkipActive = false;

        // Reset the clock-in tutorial arrow to hidden — OnDayStarted shows it once the
        // Day number pop-up plays.
        ShowClockInArrow(false);

        // Drawer is unlocked so the player can grab a folder during the tutorial.
        _drawer?.SetLocked(false);

        // Stack of folders is locked until the folder tutorial step begins (after both
        // Vlad documents are picked up). Prevents premature folder grabs.
        _stackOfFolders?.SetInteractable(false);

        // Documentation exam pile is locked until the documentation tutorial begins.
        _documentationExamShopItem?.SetAvailable(false);

        // Stamps are locked until Vlad grants permission after the document filing tutorial.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);
        _examNotebook?.SetInteractableNetworked(true);

        // Hide notebooks that aren't introduced until later days.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        ShiftManager.Instance.OnDayStart += OnDayStarted;
        SuspectController.OnSuspectArrived += OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived += OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived += OnDocAnomalySuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived += OnStampsRestoredAtWindow;
        SuspectController.OnPaperworkSpawned += OnVladPaperworkSpawned;
        FolderController.OnDocumentAdded += OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped += OnTutorialFolderStamped;
        FolderController.OnFolderHandedOff += OnVladFolderHandedOff_OneShot; // fires once after Vlad's deferred verdict
        SuspectEncounterManager.OnFirstEncounterDialogueComplete += OnSuspectFirstEncounterComplete;

        // Clock-in tutorial: hide the arrow on every client the instant the punch lands.
        TimecardMachine.OnClockInAllClients += OnClockInAllClientsLocal;

        // Subscribe to TutorialTaskSync events so all task transitions broadcast to every client.
        TutorialTaskSync.OnVladDocsBothPickedUpAllClients            += OnVladDocsBothPickedUpSync;
        TutorialTaskSync.OnQuarantineDocumentTutorialStartedAllClients += OnQuarantineTutorialStartedSync;
        TutorialTaskSync.OnExamNotebookPickedUpAllClients             += OnExamPickedUpSync;
        TutorialTaskSync.OnExamPageFiledAllClients                    += OnExamPageFiledSync;
        TutorialTaskSync.OnPressButtonReadyAllClients                 += OnPressButtonReadySync;
        TutorialTaskSync.OnFolderPlacedOnDeskAllClients               += OnFolderPlacedOnDeskSync;
        TutorialTaskSync.OnFolderHandedToVladAllClients               += OnFolderHandedToVladSync;
        TutorialTaskSync.OnTrashTaskReadyAllClients                   += OnTrashTaskReadySync;

        // Reset server-side tutorial counters when the day starts.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            TutorialTaskSync.Instance?.ResetServerState();

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced += OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced += OnFolderHandedToVlad;

        // Block the HandOffPoint from immediately processing the verdict on Day 1 so the
        // closing cutscene can play first. The verdict is delivered in OnClosingDialogueComplete.
        HandOffPoint.BlockVerdict = true;

        // Suspects follow each other immediately on Day 1 — no 30–90 s idle gaps.
        ShiftManager.OverrideSuspectArrivalInterval = new Vector2(0f, 0f);

        // Suppress all incoming phone calls for the entire Day 1 session so nothing
        // interrupts the scripted sequences.
        Telephone.BlockAllCalls = true;

        Debug.Log("[Day_01] DayActivated.");
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        // Restore notebooks so Day 2+ can manage them normally.
        _mutationNotebook?.SetVisible(true);
        _biologicalNotebook?.SetVisible(true);

        // Release the shutter lock so subsequent days can control it freely.
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;

        // Restore normal suspect arrival timing for subsequent days.
        ShiftManager.OverrideSuspectArrivalInterval = null;

        // Re-enable telephone calls.
        Telephone.BlockAllCalls = false;

        ShiftManager.OnNextSuspectReadyForBell -= AutoSummonVlad;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        TimecardMachine.OnClockInServer -= OnPlayerClockedIn;
        TimecardMachine.OnClockInAllClients -= OnClockInAllClientsLocal;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;

        SuspectController.OnSuspectArrived -= OnStampsRestoredAtWindow;
        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnQuarantineSuspectPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;
        FolderController.OnFolderEquipped -= OnQuarantinePickupTrigger;
        FolderController.OnFolderHandedOff -= OnVladFolderHandedOff_OneShot;
        FolderController.OnFolderHandedOff -= OnSubjectProcessed;
        FolderController.OnAnyFolderStamped -= OnQuarantineFolderStamped;
        FolderController.OnFolderHandedOff  -= OnQuarantineFolderHandedOff;
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnQuarantineExamPickedUpLocal;
        ExamNotebook.OnAnyNotebookPageFiled    -= OnQuarantinePageFiledLocal;
        SuspectEncounterManager.OnFirstEncounterDialogueComplete -= OnSuspectFirstEncounterComplete;

        TutorialTaskSync.OnVladDocsBothPickedUpAllClients              -= OnVladDocsBothPickedUpSync;
        TutorialTaskSync.OnQuarantineDocumentTutorialStartedAllClients -= OnQuarantineTutorialStartedSync;
        TutorialTaskSync.OnExamNotebookPickedUpAllClients              -= OnExamPickedUpSync;
        TutorialTaskSync.OnExamPageFiledAllClients                     -= OnExamPageFiledSync;
        TutorialTaskSync.OnPressButtonReadyAllClients                  -= OnPressButtonReadySync;

        // Post-Vlad ATM / button sequence cleanup.
        ShiftManager.OnNextSuspectReadyForBell -= OnVladVerdictReadyIntercept;
        CouponPickup.OnAnyPickedUp             -= OnCouponPickedUp;
        SwitchButton.OnPressed                 -= OnVladButtonPressed;
        TutorialMarkerManager.Instance?.UnmarkAll();

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        TutorialTaskSync.OnFolderPlacedOnDeskAllClients -= OnFolderPlacedOnDeskSync;
        TutorialTaskSync.OnFolderHandedToVladAllClients -= OnFolderHandedToVladSync;
        TutorialTaskSync.OnTrashTaskReadyAllClients     -= OnTrashTaskReadySync;

        TakeOutTrashTask.OnProgressChanged   -= OnTrashProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited -= OnTrashTaskComplete;
        if (CleanGraffitiTask.Instance != null)
            CleanGraffitiTask.Instance.OnDailyTaskCompleted -= OnGraffitiTaskComplete;
        CleanGraffitiTask.OnProgressChanged -= OnGraffitiProgressChanged;

        TimecardMachine.OnClockOutAllClients -= OnClockedOutForBunker;
        BunkerDoorController.OnDoorOpened    -= OnBunkerDoorOpened;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();
        UnsubscribeQuarantineDocumentPickupEvents();

        StopAllCoroutines();

        Debug.Log("[Day_01] DayDeactivated.");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;

        ShiftManager.OverrideSuspectArrivalInterval = null;
        Telephone.BlockAllCalls = false;

        ShiftManager.OnNextSuspectReadyForBell -= AutoSummonVlad;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        TimecardMachine.OnClockInServer -= OnPlayerClockedIn;
        TimecardMachine.OnClockInAllClients -= OnClockInAllClientsLocal;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;

        SuspectController.OnSuspectArrived -= OnStampsRestoredAtWindow;
        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnQuarantineSuspectPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;
        FolderController.OnFolderEquipped -= OnQuarantinePickupTrigger;
        FolderController.OnFolderHandedOff -= OnVladFolderHandedOff_OneShot;
        FolderController.OnFolderHandedOff -= OnSubjectProcessed;
        FolderController.OnAnyFolderStamped -= OnQuarantineFolderStamped;
        FolderController.OnFolderHandedOff  -= OnQuarantineFolderHandedOff;
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnQuarantineExamPickedUpLocal;
        ExamNotebook.OnAnyNotebookPageFiled    -= OnQuarantinePageFiledLocal;
        SuspectEncounterManager.OnFirstEncounterDialogueComplete -= OnSuspectFirstEncounterComplete;

        TutorialTaskSync.OnVladDocsBothPickedUpAllClients              -= OnVladDocsBothPickedUpSync;
        TutorialTaskSync.OnQuarantineDocumentTutorialStartedAllClients -= OnQuarantineTutorialStartedSync;
        TutorialTaskSync.OnExamNotebookPickedUpAllClients              -= OnExamPickedUpSync;
        TutorialTaskSync.OnExamPageFiledAllClients                     -= OnExamPageFiledSync;
        TutorialTaskSync.OnPressButtonReadyAllClients                  -= OnPressButtonReadySync;

        // Post-Vlad ATM / button sequence cleanup.
        ShiftManager.OnNextSuspectReadyForBell -= OnVladVerdictReadyIntercept;
        CouponPickup.OnAnyPickedUp             -= OnCouponPickedUp;
        SwitchButton.OnPressed                 -= OnVladButtonPressed;

        // Trash/graffiti task tutorial cleanup (safe to call even if tasks never started).
        TakeOutTrashTask.OnProgressChanged   -= OnTrashProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited -= OnTrashTaskComplete;
        if (CleanGraffitiTask.Instance != null)
            CleanGraffitiTask.Instance.OnDailyTaskCompleted -= OnGraffitiTaskComplete;
        CleanGraffitiTask.OnProgressChanged -= OnGraffitiProgressChanged;
        BunkerDoorController.OnDoorOpened    -= OnBunkerDoorOpened;
        BunkBedInteractable.OnSleepConfirmed -= OnGoToBedSleepConfirmed;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        TutorialTaskSync.OnFolderPlacedOnDeskAllClients -= OnFolderPlacedOnDeskSync;
        TutorialTaskSync.OnFolderHandedToVladAllClients -= OnFolderHandedToVladSync;
        TutorialTaskSync.OnTrashTaskReadyAllClients     -= OnTrashTaskReadySync;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();
        UnsubscribeQuarantineDocumentPickupEvents();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Day 1 Opening Sequence
    // -------------------------------------------------------------------------

    private void OnDayStarted()
    {
        if (this == null) return;
        if (_dayStartedFired) return;
        _dayStartedFired = true;

        // Guide the player to the Time Card Machine immediately after the Day number
        // pop-up plays. Runs on every client — the arrow carries no NetworkObject, so it's
        // shown/hidden locally in response to events that are already broadcast to all clients.
        ShowClockInArrow(true);

        if (!NetworkManager.Singleton.IsServer) return;

        // DayActivated runs before NGO spawns scene NetworkObjects on the debug-skip path,
        // so stamp calls there are silently ignored. Re-apply the correct locked state here
        // now that all NetworkBehaviours are guaranteed to be spawned.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);

        // When a debug skip is active, the opening sequence (Vlad / civilians / quarantine suspect) is
        // intentionally bypassed — DebugSkipToSoldierSlot handles the shift start itself.
        if (_debugSkipActive)
        {
            Debug.Log("[Day_01] OnDayStarted: debug skip active — skipping Day1OpeningSequence.");
            ShowClockInArrow(false);
            return;
        }

        // Arm the server-side reaction to the player's clock-in punch, then enable the
        // interaction on the Time Card Machine. Day1OpeningSequence now fires once the
        // player actually clocks in, rather than after a fixed delay from day start.
        TimecardMachine.OnClockInServer += OnPlayerClockedIn;
        _timeCardMachine?.EnableClockIn();
    }

    /// <summary>
    /// Fired on the server by <see cref="TimecardMachine.OnClockInServer"/> the instant the
    /// player punches in. Self-unsubscribes, then kicks off <see cref="Day1OpeningSequence"/>.
    /// </summary>
    private void OnPlayerClockedIn()
    {
        TimecardMachine.OnClockInServer -= OnPlayerClockedIn;

        if (this == null) return;
        if (!NetworkManager.Singleton.IsServer) return;
        if (_debugSkipActive) return;

        StartCoroutine(Day1OpeningSequence());
    }

    /// <summary>
    /// Fired on ALL clients by <see cref="TimecardMachine.OnClockInAllClients"/> the instant the
    /// clock-in punch lands. Purely visual — dismisses the tutorial arrow locally.
    /// </summary>
    private void OnClockInAllClientsLocal() => ShowClockInArrow(false);

    /// <summary>Shows or hides the world-space arrow pointing at the Time Card Machine.</summary>
    private void ShowClockInArrow(bool show)
    {
        if (_clockInTutorialArrow != null)
            _clockInTutorialArrow.SetActive(show);
    }

    /// <summary>
    /// Server-side coroutine, started once the player clocks in. Waits <see cref="_shutterOpenDelay"/>
    /// seconds, then:
    ///   - Opens and locks the rolling shutter.
    ///   - Arms the Vlad intercept on the first suspect spawn slot (no paperwork, no entry line).
    ///   - Auto-starts the shift (bypassing the switch button for this scripted day).
    ///   - Subscribes to <see cref="ShiftManager.OnNextSuspectReadyForBell"/> once so Vlad
    ///     is summoned automatically when the shift is ready — bypassing the bell mechanic
    ///     for this scripted tutorial character only.
    /// </summary>
    private IEnumerator Day1OpeningSequence()
    {
        yield return new WaitForSeconds(_shutterOpenDelay);

        // Open and lock the shutter — it must stay open while Vlad is at the window.
        ShutterController.Instance.OpenShutter();
        ShutterController.Instance.ShutterLockedOpen = true;

        // Animate the lever arm to the up/open position so it matches the shutter state,
        // and lock it non-interactable until the megaphone instructs the player to use it.
        _lever?.AnimateOpenServerSide(1f);
        _lever?.SetInteractable(false);

        // Arm the Vlad intercept so the first suspect slot sends him to the window.
        // ForceNextSuspectNoPaperwork suppresses document hand-off for this appearance only.
        // ForceNextSuspectSkipEntryDialogue suppresses the generic entry line so the scripted
        // sequence via ScriptedDialogueRunner takes full control of the conversation.
        SuspectController.ForceNextSuspectNoPaperwork = true;
        SuspectController.ForceNextSuspectSkipEntryDialogue = true;
        SuspectController.InterceptNextSuspectSpawn = () => SuspectController.Instance.SpawnScriptedSuspect(_vladPrefab);

        // Vlad is a tutorial character — bypass the bell mechanic for his slot only.
        // When the shift signals the first suspect is ready, auto-summon him immediately.
        ShiftManager.OnNextSuspectReadyForBell += AutoSummonVlad;

        // Start the shift automatically — no switch press required on Day 1.
        ShiftManager.Instance.TryStartShift();

        Debug.Log("[Day_01] Shutter opened and Vlad intercept armed — shift auto-started.");
    }

    /// <summary>
    /// Auto-summons the first suspect (Vlad) without requiring the player to ring the bell.
    /// Subscribed once to <see cref="ShiftManager.OnNextSuspectReadyForBell"/> at the start
    /// of the Day 1 opening sequence and self-unsubscribes after one use.
    /// </summary>
    private void AutoSummonVlad()
    {
        ShiftManager.OnNextSuspectReadyForBell -= AutoSummonVlad;
        SuspectController.Instance?.NextSuspect();
    }

    // -------------------------------------------------------------------------
    // Vlad Scripted Dialogue
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Checks if it is the first suspect
    /// (index 0 = Vlad), then — server-only — starts the scripted dialogue sequence after
    /// a short delay that allows Vlad to finish his arrival rotation.
    /// </summary>
    private void OnVladArrivedAtWindow(int index)
    {
        if (index != 0) return; // Only react to the very first suspect on Day 1.

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;

        if (!NetworkManager.Singleton.IsServer) return;

        if (_vladDialogue == null)
        {
            Debug.LogWarning("[Day_01] _vladDialogue is not assigned — skipping scripted dialogue.");
            return;
        }

        StartCoroutine(WaitAndStartVladDialogue());
    }

    private IEnumerator WaitAndStartVladDialogue()
    {
        // Allow Vlad to finish his 0.5 s rotation and settle at the window.
        yield return new WaitForSeconds(_vladDialogueStartDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad == null)
        {
            Debug.LogWarning("[Day_01] CurrentSuspect is null when trying to start Vlad's scripted dialogue.");
            yield break;
        }

        ScriptedDialogueRunner.Instance.PlayDialogue(vlad, _vladDialogue, OnVladDialogueComplete);
    }

    /// <summary>Called on the server once Vlad's scripted dialogue finishes.</summary>
    private void OnVladDialogueComplete()
    {
        Debug.Log("[Day_01] Vlad scripted dialogue complete — spawning paperwork tutorial.");
        StartCoroutine(VladPaperworkTutorialRoutine());
    }

    /// <summary>
    /// Server-side coroutine. Plays Vlad's "Give" animation on all clients, then after
    /// a 0.5 s beat drops his papers on the desk. After a further pause he delivers his
    /// paperwork bark and the first HUD task appears.
    /// </summary>
    private IEnumerator VladPaperworkTutorialRoutine()
    {
        yield return new WaitForSeconds(_vladPaperworkDelay);

        // Play the give animation on all clients so Vlad visibly hands over the papers.
        SuspectController.Instance?.TriggerCurrentSuspectAnimationClientRpc("Give");

        // Short beat so the animation has a moment to start before the docs appear.
        yield return new WaitForSeconds(0.5f);

        _docsPickedUp = 0;
        _docsFiledCount = 0;
        SuspectController.Instance?.SpawnPaperwork();
        Debug.Log("[Day_01] Vlad's paperwork spawned.");

        yield return new WaitForSeconds(_vladPaperworkBarkDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladPaperworkBark))
            vlad.Speaking.Say(_vladPaperworkBark);
    }

    // -------------------------------------------------------------------------
    // Tutorial — Document Pickup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when Vlad's paperwork spawns. Hooks OnPickedUpEvent on both
    /// documents so we can track when the local player picks them up. Self-unsubscribes —
    /// no further suspects are tracked through this event; the quarantine tutorial suspect's
    /// own paperwork is instead wired directly in <see cref="OnDocAnomalySuspectArrivedAtWindow"/>
    /// via <see cref="OnQuarantineSuspectPaperworkSpawned"/>.
    /// </summary>
    private void OnVladPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        Debug.Log($"[Day_01] OnVladPaperworkSpawned — card={card}, appForm={appForm}  IsServer={NetworkManager.Singleton?.IsServer ?? false}");

        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        _vladIDCard = card;
        _vladAppForm = appForm;

        // Wire pickup events immediately so a fast player never misses a document pickup.
        if (card != null)
            card.OnPickedUpEvent += OnVladDocumentPickedUp;

        if (appForm != null)
            appForm.OnPickedUpEvent += OnVladDocumentPickedUp;

        // Show the pick-up task immediately, then show the overlay independently so
        // the objective list doesn't wait for the player to dismiss the tutorial.
        ShowVladPickUpTask();
        TutorialOverlay.Instance?.ShowHandlingItemsTutorial();
    }

    /// <summary>
    /// Marks the pick-up-documents step. Called as the <see cref="TutorialOverlay"/>
    /// close callback from <see cref="OnVladPaperworkSpawned"/> so it appears after the player
    /// has seen the handling-items tutorial.
    /// Deliberately does NOT add a <see cref="TutorialObjectiveList"/> entry — this step is
    /// only guided by the world-space marker. It's still tracked internally: pickup events on
    /// both documents (see <see cref="OnVladDocumentPickedUp"/>) report to the server via
    /// <see cref="TutorialTaskSync"/>, which fires <see cref="OnVladDocsBothPickedUpSync"/> on
    /// all clients once both are picked up, advancing to the folder task.
    /// </summary>
    private void ShowVladPickUpTask()
    {
        if (TutorialMarkerManager.Instance != null && _markerPickUpDocs != null)
            TutorialMarkerManager.Instance.Mark(_markerPickUpDocs);
    }

    private void OnVladDocumentPickedUp()
    {
        // Report every pickup to the server so it can count globally across all clients.
        TutorialTaskSync.Instance?.ReportVladDocPickedUpServerRpc();

        _docsPickedUp++;
        if (_docsPickedUp >= 2)
            UnsubscribeDocumentPickupEvents();
    }

    private IEnumerator VladFolderBarkRoutine()
    {
        yield return new WaitForSeconds(_vladFolderBarkDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladFolderBark))
            vlad.Speaking.Say(_vladFolderBark);
    }

    private void UnsubscribeDocumentPickupEvents()
    {
        if (_vladIDCard != null)
        {
            _vladIDCard.OnPickedUpEvent -= OnVladDocumentPickedUp;
            _vladIDCard = null;
        }

        if (_vladAppForm != null)
        {
            _vladAppForm.OnPickedUpEvent -= OnVladDocumentPickedUp;
            _vladAppForm = null;
        }
    }

    // -------------------------------------------------------------------------
    // Quarantine Tutorial Suspect Paperwork & Tutorial (index 2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients whenever ANY suspect's paperwork spawns while we're waiting for
    /// the quarantine tutorial suspect (index 2). Verifies the event actually belongs to
    /// <see cref="_quarantineTargetSuspect"/> — the suspect captured in
    /// <see cref="OnDocAnomalySuspectArrivedAtWindow"/> — before wiring pickup triggers.
    /// This guards against ever attaching the documentation tutorial to the wrong suspect:
    /// if this suspect's own paperwork never spawns (e.g. their first-encounter intro dialogue
    /// stalls) and a LATER suspect's paperwork event arrives instead, we drop the subscription
    /// entirely rather than hijacking the tutorial onto that later suspect.
    /// IMPORTANT: the tutorial trigger must wait for the player to actually pick up this
    /// suspect's documents/folder (see <see cref="OnQuarantinePickupTrigger()"/>) — starting the
    /// megaphone dialogue any earlier collides with and interrupts this suspect's own
    /// first-encounter intro monologue (played via <see cref="SuspectEncounterManager"/>),
    /// which stops that monologue from ever finishing and stops it from ever reaching
    /// <see cref="SuspectCharacter.GivePaperwork"/> — i.e. the suspect never hands over
    /// their documents at all.
    /// </summary>
    private void OnQuarantineSuspectPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        SuspectController.OnPaperworkSpawned -= OnQuarantineSuspectPaperworkSpawned;

        if (SuspectController.Instance?.CurrentSuspect != _quarantineTargetSuspect)
        {
            Debug.LogWarning("[Day_01] OnQuarantineSuspectPaperworkSpawned: paperwork belongs to a " +
                              "different suspect than the index-2 quarantine target — the target's own " +
                              "paperwork never spawned. Skipping the documentation tutorial for this day.");
            _quarantineTargetSuspect = null;
            return;
        }

        _quarantineTargetSuspect = null;
        _quarantineDoc1 = card;
        _quarantineDoc2 = appForm;

        if (card != null)    card.OnPickedUpEvent    += OnQuarantinePickupTrigger;
        if (appForm != null) appForm.OnPickedUpEvent  += OnQuarantinePickupTrigger;
        FolderController.OnFolderEquipped += OnQuarantinePickupTrigger;
    }

    /// <summary>
    /// Fires when the player picks up one of the quarantine tutorial suspect's documents or
    /// any folder. Kicks off the documentation tutorial — subscribes for single-fire then shows
    /// the first tutorial task and starts the megaphone bark sequence on the server.
    /// Signature matches both <see cref="PickableObject.OnPickedUpEvent"/> (Action)
    /// and <see cref="FolderController.OnFolderEquipped"/> (Action&lt;FolderController&gt;)
    /// via separate adapter overloads below.
    /// </summary>
    private void StartQuarantineDocumentationTutorial()
    {
        // Single-fire — remove all triggers immediately on this client.
        UnsubscribeQuarantineDocumentPickupEvents();

        // Notify the server; it will broadcast the task start to all clients exactly once.
        TutorialTaskSync.Instance?.ReportQuarantineTutorialTriggerServerRpc();
    }

    // Parameterless adapter — used by PickableObject.OnPickedUpEvent.
    private void OnQuarantinePickupTrigger() => StartQuarantineDocumentationTutorial();

    // FolderController.OnFolderEquipped passes the instance — we ignore it.
    private void OnQuarantinePickupTrigger(FolderController _) => StartQuarantineDocumentationTutorial();

    private void UnsubscribeQuarantineDocumentPickupEvents()
    {
        if (_quarantineDoc1 != null) { _quarantineDoc1.OnPickedUpEvent -= OnQuarantinePickupTrigger; _quarantineDoc1 = null; }
        if (_quarantineDoc2 != null) { _quarantineDoc2.OnPickedUpEvent -= OnQuarantinePickupTrigger; _quarantineDoc2 = null; }
        FolderController.OnFolderEquipped -= OnQuarantinePickupTrigger;
    }

    /// <summary>
    /// Fires on the local client when they pick up an exam notebook during the tutorial.
    /// Reports to the server; <see cref="TutorialTaskSync"/> broadcasts the task swap to all clients.
    /// </summary>
    private void OnQuarantineExamPickedUpLocal()
    {
        TutorialTaskSync.Instance?.ReportExamPickedUpServerRpc();
    }

    /// <summary>
    /// Fires on the local client when an exam notebook page is filed during the tutorial.
    /// Reports to the server; <see cref="TutorialTaskSync"/> broadcasts the task removal to all clients.
    /// </summary>
    private void OnQuarantinePageFiledLocal()
    {
        TutorialTaskSync.Instance?.ReportExamPageFiledServerRpc();
    }

    /// <summary>
    /// Server-only coroutine. Plays the documentation tutorial megaphone dialogue sequences:
    ///   — Part 1: two lines explaining the paperwork discrepancy (clickable subtitles).
    ///   — Makes the documentation exam free and waits for the player to pick it up.
    ///   — Part 2: four follow-up lines explaining documentation anomalies.
    /// Both parts are played through <see cref="ScriptedDialogueRunner.PlayMegaphoneDialogue"/>
    /// so the player clicks to advance each line exactly as with character dialogue.
    /// </summary>
    private IEnumerator QuarantineDocumentationBarkRoutine()
    {
        var runner = ScriptedDialogueRunner.Instance;
        var mgr = MegaphoneDialogueManager.Instance;
        if (runner == null) yield break;

        // ── Part 1: 2 intro lines ─────────────────────────────────────────────
        if (_quarantineMegaphonePart1 != null)
        {
            bool part1Done = false;
            runner.PlayMegaphoneDialogue(_quarantineMegaphonePart1, () => part1Done = true);
            yield return new WaitUntil(() => part1Done);
        }
        else
        {
            Debug.LogWarning("[Day_01] _quarantineMegaphonePart1 is not assigned — skipping first megaphone sequence.");
        }

        // Make the documentation exam free for this pass only.
        if (mgr != null)
            mgr.SetShopItemPriceOverrideSynced(_documentationExamItemName, 0);

        // Wait for the player to pick up the exam (flag set on all clients via ExamNotebook.OnPickedUp).
        yield return new WaitUntil(() => ExamNotebook.AnyExamNotebookPickedUp);

        // Restore the price now that the freebie has been claimed.
        if (mgr != null)
            mgr.ClearShopItemPriceOverrideSynced(_documentationExamItemName);

        yield return new WaitForSeconds(0.5f);

        // ── Part 2: 4 follow-up lines ─────────────────────────────────────────
        if (_quarantineMegaphonePart2 != null)
        {
            bool part2Done = false;
            runner.PlayMegaphoneDialogue(_quarantineMegaphonePart2, () => part2Done = true);
            yield return new WaitUntil(() => part2Done);
        }
        else
        {
            Debug.LogWarning("[Day_01] _quarantineMegaphonePart2 is not assigned — skipping second megaphone sequence.");
        }
    }

    // -------------------------------------------------------------------------
    // Tutorial task sync callbacks — fire on ALL clients via TutorialTaskSync RPCs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients once the server confirms both Vlad documents were picked up.
    /// Adds the visible "file documents" task to the objective list — the pick-up step
    /// itself was never shown there (see <see cref="ShowVladPickUpTask"/>) — and starts the
    /// folder bark on the server so it plays exactly once.
    /// </summary>
    private void OnVladDocsBothPickedUpSync()
    {
        TutorialTaskSync.OnVladDocsBothPickedUpAllClients -= OnVladDocsBothPickedUpSync;

        if (TutorialMarkerManager.Instance != null)
        {
            if (_markerPickUpDocs != null) TutorialMarkerManager.Instance.Unmark(_markerPickUpDocs);
            if (_markerDrawer != null)     TutorialMarkerManager.Instance.Mark(_markerDrawer);
        }

        // Unlock the stack of folders now that the player is guided to grab one.
        _stackOfFolders?.SetInteractable(true);

        _taskFolder = TutorialObjectiveList.Instance?.AddObjective(_taskFolderDocs);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            StartCoroutine(VladFolderBarkRoutine());
    }

    /// <summary>
    /// Fires on all clients when the quarantine tutorial suspect's documentation tutorial begins.
    /// Adds the checklist task, wires exam pickup/filed events locally, and starts
    /// the megaphone bark routine on the server.
    /// </summary>
    private void OnQuarantineTutorialStartedSync()
    {
        TutorialTaskSync.OnQuarantineDocumentTutorialStartedAllClients -= OnQuarantineTutorialStartedSync;

        // Clean up any remaining quarantine document pickup subscriptions on all clients.
        UnsubscribeQuarantineDocumentPickupEvents();

        // Unlock the documentation exam shop item now that the player is guided to use it.
        _documentationExamShopItem?.SetAvailable(true);

        _taskGetChecklist = TutorialObjectiveList.Instance?.AddObjective(_taskGetChecklistText);
        if (TutorialMarkerManager.Instance != null && _markerDocumentationExam != null)
            TutorialMarkerManager.Instance.Mark(_markerDocumentationExam);

        // Reset flags and subscribe completion handlers on every client so whoever
        // interacts next reports back via TutorialTaskSync RPCs.
        ExamNotebook.AnyExamNotebookPickedUp = false;
        ExamNotebook.OnAnyExamNotebookPickedUp += OnQuarantineExamPickedUpLocal;
        ExamNotebook.AnyPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnQuarantinePageFiledLocal;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            StartCoroutine(QuarantineDocumentationBarkRoutine());
    }

    /// <summary>
    /// Fires on all clients when any player picks up an exam notebook during the tutorial.
    /// Swaps the "get checklist" task to the "check anomalies" task.
    /// </summary>
    private void OnExamPickedUpSync()
    {
        TutorialTaskSync.OnExamNotebookPickedUpAllClients -= OnExamPickedUpSync;
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnQuarantineExamPickedUpLocal;

        if (_taskGetChecklist != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskGetChecklist);
            _taskGetChecklist = null;
        }

        if (TutorialMarkerManager.Instance != null)
        {
            if (_markerDocumentationExam != null) TutorialMarkerManager.Instance.Unmark(_markerDocumentationExam);
            if (_markerExamNotebook != null)      TutorialMarkerManager.Instance.Mark(_markerExamNotebook);
        }

        _taskCheckDocumentation = TutorialObjectiveList.Instance?.AddObjective(_taskCheckDocumentationText);
    }

    /// <summary>
    /// Fires on all clients when any exam notebook page is filed during the tutorial.
    /// Removes the "check anomalies" task and continues into the quarantine tutorial sequence.
    /// </summary>
    private void OnExamPageFiledSync()
    {
        TutorialTaskSync.OnExamPageFiledAllClients -= OnExamPageFiledSync;
        ExamNotebook.OnAnyNotebookPageFiled -= OnQuarantinePageFiledLocal;

        if (_taskCheckDocumentation != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskCheckDocumentation);
            _taskCheckDocumentation = null;
        }

        if (TutorialMarkerManager.Instance != null && _markerExamNotebook != null)
            TutorialMarkerManager.Instance.Unmark(_markerExamNotebook);

        // Continue to the quarantine stamp step — do not hide the list yet.
        StartQuarantineTutorialStep();
    }

    // -------------------------------------------------------------------------
    // Quarantine Tutorial Sequence (index-2 suspect)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Begins the quarantine-stamp phase of the tutorial. Called on all clients immediately
    /// after the exam page is filed. Adds the "grab quarantine stamp" task, shows the
    /// marker above the yellow stamp slot, and subscribes a one-shot stamp handler.
    /// </summary>
    private void StartQuarantineTutorialStep()
    {
        _taskQuarantineStamp = TutorialObjectiveList.Instance?.AddObjective(_taskGrabQuarantineStampText);

        if (TutorialMarkerManager.Instance != null && _markerYellowStamp != null)
            TutorialMarkerManager.Instance.Mark(_markerYellowStamp);

        FolderController.OnAnyFolderStamped += OnQuarantineFolderStamped;
    }

    /// <summary>
    /// Fires on all clients when any folder is stamped after the quarantine tutorial step began.
    /// Completes the stamp task, removes the arrow, and shows the "place folder at window" task.
    /// </summary>
    private void OnQuarantineFolderStamped()
    {
        FolderController.OnAnyFolderStamped -= OnQuarantineFolderStamped;

        if (_taskQuarantineStamp != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskQuarantineStamp);
            _taskQuarantineStamp = null;
        }

        if (TutorialMarkerManager.Instance != null && _markerYellowStamp != null)
            TutorialMarkerManager.Instance.Unmark(_markerYellowStamp);

        _taskQuarantineHandOff = TutorialObjectiveList.Instance?.AddObjective(_taskQuarantineHandOffText);

        FolderController.OnFolderHandedOff += OnQuarantineFolderHandedOff;
    }

    /// <summary>
    /// Fires on all clients when the folder is handed off at the window (quarantine verdict).
    /// Completes the final quarantine tutorial task, hides the list, then shows the accuracy-
    /// payout tutorial overlay. Once the player closes it the subject counter reappears.
    /// </summary>
    private void OnQuarantineFolderHandedOff()
    {
        FolderController.OnFolderHandedOff -= OnQuarantineFolderHandedOff;

        if (_taskQuarantineHandOff != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskQuarantineHandOff);
            _taskQuarantineHandOff = null;
        }

        // Hide the task list, then show the accuracy-payout overlay.
        // ReshowSubjectCounter runs after the player closes the overlay.
        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f, onComplete: ShowAccuracyPayoutOverlay);

        Debug.Log("[Day_01] Quarantine tutorial complete — folder handed off.");
    }

    /// <summary>
    /// Shows the accuracy-payout tutorial overlay after the quarantine task list finishes
    /// clearing. The subject counter task is re-added immediately (independently of the overlay)
    /// so the two systems don't block each other.
    /// </summary>
    private void ShowAccuracyPayoutOverlay()
    {
        // Re-show the subject counter right away — the overlay is a separate system.
        ReshowSubjectCounter();

        // Show the overlay independently; its onComplete is not used to gate the task list.
        TutorialOverlay.Instance?.ShowAccuracyPayoutTutorial();
    }

    /// <summary>
    /// Called after the quarantine tutorial list is cleared to restore the "Process N subjects"
    /// counter task. The counter is re-created using the current progress so the player can
    /// continue tracking their shift quota without seeing a blank list.
    /// </summary>
    private void ReshowSubjectCounter()
    {
        if (_subjectProcessedCount >= SubjectsToProcess) return;
        if (_taskSubjectCount != null) return; // Still alive — no need to recreate.

        _taskSubjectCount = TutorialObjectiveList.Instance?.AddObjective(GetSubjectCountText());
    }

    // -------------------------------------------------------------------------
    // Tutorial — Folder placed on desk
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires when any item is placed on the desk placement board — but only on whichever
    /// client actually performed the drop, since <see cref="PlacementBoard.OnItemPlaced"/> is
    /// a purely local event. Validates the item and reports it to the server via
    /// <see cref="TutorialTaskSync"/> so the resulting task/marker transition and Vlad bark are
    /// broadcast identically to every connected client in <see cref="OnFolderPlacedOnDeskSync"/>.
    /// </summary>
    private void OnFolderPlacedOnDesk(PickableObject item)
    {
        if (item == null || item.GetComponent<FolderController>() == null) return;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        TutorialTaskSync.Instance?.ReportFolderPlacedOnDeskServerRpc();
    }

    /// <summary>
    /// Fires on all clients once the server confirms the tutorial folder was placed on the
    /// desk board (relayed via <see cref="TutorialTaskSync.OnFolderPlacedOnDeskAllClients"/>).
    /// Completes the placement task, swaps the arrow from the drawer to the desk board, and
    /// shows the "file documents" task. The bark is only fired from the server since
    /// <see cref="SpeakingInteraction.Say"/> already broadcasts itself to every client.
    /// </summary>
    private void OnFolderPlacedOnDeskSync()
    {
        TutorialTaskSync.OnFolderPlacedOnDeskAllClients -= OnFolderPlacedOnDeskSync;

        if (_taskFolder != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskFolder);
            _taskFolder = null;
        }

        if (TutorialMarkerManager.Instance != null)
        {
            if (_markerDrawer != null)    TutorialMarkerManager.Instance.Unmark(_markerDrawer);
            if (_markerDeskBoard != null) TutorialMarkerManager.Instance.Mark(_markerDeskBoard);
        }

        _taskFile = TutorialObjectiveList.Instance?.AddObjective(_taskPlaceDocsText);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
            if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladFolderPlacedBark))
                vlad.Speaking.Say(_vladFolderPlacedBark);
        }
    }

    // -------------------------------------------------------------------------
    // Tutorial — Document Filing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires locally whenever any document is added to a folder. Counts only Vlad's two
    /// core documents (IDCard + ApplicationLetter); evidence or exam pages are ignored.
    /// </summary>
    private void OnDocumentFiledInFolder(PickableObject doc)
    {
        if (doc == null) return;
        bool isIdCard = doc is IDCard;
        bool isAppForm = doc is ApplicationLetter;
        if (!isIdCard && !isAppForm) return;

        _docsFiledCount++;
        if (_docsFiledCount < 2) return;

        // Both core documents filed — complete task 3, add stamp task, and unsubscribe.
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;

        if (_taskFile != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskFile);
            _taskFile = null;
        }

        if (TutorialMarkerManager.Instance != null)
        {
            if (_markerDeskBoard != null)  TutorialMarkerManager.Instance.Unmark(_markerDeskBoard);
            if (_markerGreenStamp != null) TutorialMarkerManager.Instance.Mark(_markerGreenStamp);
        }

        _taskStamp = TutorialObjectiveList.Instance?.AddObjective(_taskStampFolder);

        StartCoroutine(VladStampPermissionRoutine());
        Debug.Log("[Day_01] Document filing tutorial complete — starting stamp permission step.");
    }

    // -------------------------------------------------------------------------
    // Tutorial — Stamp permission
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits briefly after both documents are filed, then has Vlad grant permission
    /// to use the green stamp and unlocks it on all clients.
    /// The bark is only fired from the server to avoid duplicates, since
    /// <see cref="OnDocumentFiledInFolder"/> fires on every client.
    /// </summary>
    private IEnumerator VladStampPermissionRoutine()
    {
        yield return new WaitForSeconds(_vladStampBarkDelay);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
            if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladStampPermissionBark))
                vlad.Speaking.Say(_vladStampPermissionBark);
        }

        _greenStampSlot?.SetSlotInteractable(true);
    }

    // -------------------------------------------------------------------------
    // Tutorial — Hand-off instruction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any folder is stamped.
    /// Unsubscribes immediately so only the first stamp during Day 1 triggers this.
    /// </summary>
    private void OnTutorialFolderStamped()
    {
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;

        if (_taskStamp != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskStamp);
            _taskStamp = null;
        }

        if (TutorialMarkerManager.Instance != null)
        {
            if (_markerGreenStamp != null)  TutorialMarkerManager.Instance.Unmark(_markerGreenStamp);
            if (_markerWindowBoard != null) TutorialMarkerManager.Instance.Mark(_markerWindowBoard);
        }

        _taskHandOff = TutorialObjectiveList.Instance?.AddObjective(_taskHandOffText);

        StartCoroutine(VladHandOffBarkRoutine());
    }

    private IEnumerator VladHandOffBarkRoutine()
    {
        yield return new WaitForSeconds(_vladHandOffBarkDelay);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
            if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladHandOffBark))
                vlad.Speaking.Say(_vladHandOffBark);
        }
    }

    // -------------------------------------------------------------------------
    // Tutorial — Folder placed at window (hand-off to Vlad)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires when an item is placed on the window hand-off PlacementBoard — but only on
    /// whichever client actually performed the drop, since <see cref="PlacementBoard.OnItemPlaced"/>
    /// is a purely local event. Validates it is a folder and reports it (with a network
    /// reference so the server can resolve and lock it) via <see cref="TutorialTaskSync"/>.
    /// </summary>
    private void OnFolderHandedToVlad(PickableObject item)
    {
        if (item == null || item.GetComponent<FolderController>() == null) return;

        // Unsubscribe before any async work to guarantee single-fire.
        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        NetworkObject folderNetObj = item.GetComponent<NetworkObject>();
        if (folderNetObj != null)
            TutorialTaskSync.Instance?.ReportFolderHandedToVladServerRpc(folderNetObj);
    }

    /// <summary>
    /// Fires on all clients once the server confirms the stamped folder was placed at the
    /// window (relayed via <see cref="TutorialTaskSync.OnFolderHandedToVladAllClients"/>).
    /// Completes the hand-off task and dismisses the objective list on every client, then,
    /// server-only, locks the folder and starts the closing scripted dialogue after a short
    /// delay — this now runs regardless of which client physically placed the folder.
    /// </summary>
    private void OnFolderHandedToVladSync(NetworkObjectReference folderRef)
    {
        TutorialTaskSync.OnFolderHandedToVladAllClients -= OnFolderHandedToVladSync;

        if (_taskHandOff != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskHandOff);
            _taskHandOff = null;
        }

        if (TutorialMarkerManager.Instance != null && _markerWindowBoard != null)
            TutorialMarkerManager.Instance.Unmark(_markerWindowBoard);

        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f);

        if (!NetworkManager.Singleton.IsServer) return;

        if (!folderRef.TryGet(out NetworkObject folderNetObj))
        {
            Debug.LogWarning("[Day_01] OnFolderHandedToVladSync: could not resolve folder NetworkObject.");
            return;
        }

        PickableObject item = folderNetObj.GetComponent<PickableObject>();

        // Lock the folder immediately so the player cannot pick it back up during the cutscene.
        item?.SetInteractableNetworked(false);

        if (_vladClosingDialogue == null)
        {
            Debug.LogWarning("[Day_01] _vladClosingDialogue is not assigned — skipping closing dialogue.");
            return;
        }

        StartCoroutine(StartClosingDialogue());
    }

    // -------------------------------------------------------------------------
    // Subject counter task — shown after the player collects coupons and presses the button
    // -------------------------------------------------------------------------

    /// <summary>
    /// One-shot handler subscribed in <see cref="DayActivated"/>.
    /// Fires on ALL clients when <see cref="FolderController.OnFolderHandedOff"/> broadcasts
    /// Vlad's deferred verdict via its NetworkVariable. Unsubscribes immediately, then shows
    /// the "Collect coupons at ATM" task on every client and subscribes the per-coupon pickup
    /// counter. The switch button is kept dark server-side via <see cref="OnVladVerdictReadyIntercept"/>
    /// until all coupons have been picked up.
    /// </summary>
    private void OnVladFolderHandedOff_OneShot()
    {
        FolderController.OnFolderHandedOff -= OnVladFolderHandedOff_OneShot;

        _taskCollectCoupons = TutorialObjectiveList.Instance?.AddObjective(_taskCollectCouponsText);

        if (TutorialMarkerManager.Instance != null && _markerATM != null)
            TutorialMarkerManager.Instance.Mark(_markerATM);

        CouponPickup.OnAnyPickedUp += OnCouponPickedUp;

        Debug.Log("[Day_01] Vlad verdict broadcast received — showing 'Collect coupons' task.");
    }

    /// <summary>
    /// Server-only. Fires when <see cref="ShiftManager.SetNextSuspectReady"/> emits
    /// <see cref="ShiftManager.OnNextSuspectReadyForBell"/> after Vlad's deferred verdict.
    /// Clears <see cref="ShiftManager.NextSuspectReadyForBell"/> synchronously so that
    /// <see cref="SwitchButton"/>'s deferred-one-frame coroutine sees <c>false</c> and
    /// does not light up the button. The button is re-armed in <see cref="CheckAllCouponsCollectedDeferred"/>
    /// once every dispensed coupon has been picked up.
    /// </summary>
    private void OnVladVerdictReadyIntercept()
    {
        ShiftManager.OnNextSuspectReadyForBell -= OnVladVerdictReadyIntercept;
        // Clearing the flag here guarantees SwitchButton.SetReadyIfStillPending (which
        // yields one frame before checking) will find false and not light up the button.
        ShiftManager.NextSuspectReadyForBell = false;
        Debug.Log("[Day_01] Switch button blocked — waiting for ATM coupon collection.");
    }

    /// <summary>
    /// Fires on all clients whenever any <see cref="CouponPickup"/> is collected
    /// (via <see cref="CouponPickup.OnAnyPickedUp"/>). Defers one frame so NGO can
    /// process the NetworkObject despawn and decrement <see cref="CouponPickup.ActiveCount"/>,
    /// then advances the task if no coupons remain.
    /// </summary>
    private void OnCouponPickedUp() => StartCoroutine(CheckAllCouponsCollectedDeferred());

    private IEnumerator CheckAllCouponsCollectedDeferred()
    {
        // One frame for the NetworkObject despawn to propagate and decrement ActiveCount.
        yield return null;

        // Guard against multiple parallel coroutines completing the task twice.
        if (_taskCollectCoupons == null) yield break;

        if (CouponPickup.ActiveCount > 0) yield break;

        // All coupons collected — complete the coupon task and unmark the ATM arrow.
        CouponPickup.OnAnyPickedUp -= OnCouponPickedUp;

        if (TutorialMarkerManager.Instance != null && _markerATM != null)
            TutorialMarkerManager.Instance.Unmark(_markerATM);

        TutorialObjectiveList.Instance?.CompleteObjective(_taskCollectCoupons);
        _taskCollectCoupons = null;

        // Server-only: immediately allow the player to call the next suspect — no further
        // dialogue or clock-in gating in between.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            TutorialTaskSync.Instance?.BroadcastPressButtonReadyServer();

        Debug.Log("[Day_01] All coupons collected — press-button task armed immediately.");
    }

    /// <summary>
    /// Fires on ALL clients via <see cref="TutorialTaskSync.OnPressButtonReadyAllClients"/>
    /// once all ATM coupons have been collected. Shows the "Press button" task,
    /// arms the switch button marker, and re-arms the switch on the server.
    /// </summary>
    private void OnPressButtonReadySync()
    {
        TutorialTaskSync.OnPressButtonReadyAllClients -= OnPressButtonReadySync;

        _taskPressButton = TutorialObjectiveList.Instance?.AddObjective(_taskPressButtonText);

        if (TutorialMarkerManager.Instance != null && _markerSwitchButton != null)
            TutorialMarkerManager.Instance.Mark(_markerSwitchButton);

        // Subscribe to catch the button press before re-arming so we never miss it.
        SwitchButton.OnPressed += OnVladButtonPressed;

        // Re-arm the switch: DeliverDeferredVerdict cleared the flag via the intercept,
        // so call SetNextSuspectReady again on the server to light up the button.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            ShiftManager.Instance?.SetNextSuspectReady();

        Debug.Log("[Day_01] Coupons collected — 'Press button' task shown, switch button armed.");
    }

    /// <summary>
    /// Fires on ALL clients (via <see cref="SwitchButton.OnPressed"/> ClientRpc) when the
    /// player presses the switch button after Vlad's tutorial. Completes the button task
    /// and dismisses the objective list. The subject counter is shown immediately after the
    /// list finishes clearing via the <see cref="TutorialObjectiveList.HideAndClear"/> callback.
    /// </summary>
    private void OnVladButtonPressed()
    {
        SwitchButton.OnPressed -= OnVladButtonPressed;

        if (TutorialMarkerManager.Instance != null && _markerSwitchButton != null)
            TutorialMarkerManager.Instance.Unmark(_markerSwitchButton);

        if (_taskPressButton != null)
        {
            TutorialObjectiveList.Instance?.CompleteObjective(_taskPressButton);
            _taskPressButton = null;
        }

        // Pass ShowSubjectCounterTask as the onComplete callback so the counter appears
        // the moment the list finishes its close animation — no separate timer needed.
        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f, onComplete: ShowSubjectCounterTask);

        Debug.Log("[Day_01] Button pressed — ATM task list dismissed, subject counter will appear after close animation.");
    }

    /// <summary>
    /// Called once the delay has elapsed. Shows the "Process N subjects" counter
    /// task and subscribes to subsequent folder hand-offs to update it.
    /// </summary>
    private void ShowSubjectCounterTask()
    {
        _subjectProcessedCount = 0;
        _taskSubjectCount = TutorialObjectiveList.Instance?.AddObjective(GetSubjectCountText());
        FolderController.OnFolderHandedOff += OnSubjectProcessed;
    }

    /// <summary>
    /// Fires on the local client whenever any folder is handed off at the window.
    /// Skips Vlad's deferred verdict (he remains suspect index 0 when his hand-off resolves).
    /// Increments the counter and hides the list once all subjects are processed.
    /// </summary>
    private void OnSubjectProcessed()
    {
        // Vlad's deferred verdict fires while suspectIndex is still 0.
        // Only count subjects from index 1 onward (real post-tutorial suspects).
        if (SuspectController.Instance != null && SuspectController.Instance.SuspectIndex < 1)
            return;

        _subjectProcessedCount++;
        _taskSubjectCount?.SetText(GetSubjectCountText());

        if (_subjectProcessedCount >= SubjectsToProcess)
        {
            FolderController.OnFolderHandedOff -= OnSubjectProcessed;
            TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f);
            _taskSubjectCount = null;
        }
    }

    private string GetSubjectCountText() =>
        $"Process {SubjectsToProcess} subjects {_subjectProcessedCount}/{SubjectsToProcess}";

    private IEnumerator StartClosingDialogue()
    {
        yield return new WaitForSeconds(_vladClosingDialogueDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad == null)
        {
            Debug.LogWarning("[Day_01] CurrentSuspect is null when trying to start closing dialogue.");
            // Still need to unblock the verdict so the day can continue.
            DeliverDeferredVerdict();
            yield break;
        }

        ScriptedDialogueRunner.Instance.PlayDialogue(vlad, _vladClosingDialogue, OnClosingDialogueComplete);
        Debug.Log("[Day_01] Closing dialogue started.");
    }

    /// <summary>
    /// Called on the server once the closing scripted dialogue finishes.
    /// Suppresses Vlad's exit line and lets a random clean civilian walk through as
    /// the first real subject (index 1). Subscribers to the random slot overrides
    /// have been removed — all post-Vlad suspects are randomly generated.
    /// </summary>
    private void OnClosingDialogueComplete()
    {
        // Silence Vlad's generic exit bark — his story ends with "Don't fuck it up."
        SuspectController.ForceNextSuspectSkipExitDialogue = true;

        // Index 1 is always a random clean civilian — no forced-spawn intercept needed.
        SuspectController.ForceNextSuspectClean = true;
        Debug.Log("[Day_01] Closing dialogue complete — slot 1 is random clean civilian.");

        // Server only: intercept the "next suspect ready" signal emitted by DeliverDeferredVerdict
        // so the switch button stays dark until the player collects their ATM payout coupons.
        // The intercept clears NextSuspectReadyForBell in the same frame, preventing SwitchButton's
        // deferred-one-frame coroutine from seeing the flag as true and lighting up the button.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            ShiftManager.OnNextSuspectReadyForBell += OnVladVerdictReadyIntercept;

        DeliverDeferredVerdict();
    }

    // -------------------------------------------------------------------------
    // Random Suspect (index 1) — clean civilian between Vlad and the tutorial suspects
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 1 (the random clean
    /// civilian following Vlad). Unsubscribes and does nothing else — the next spawn
    /// (index 2) is always a random suspect that receives documentation anomalies via
    /// <see cref="OnDocAnomalySuspectArrivedAtWindow"/>.
    /// </summary>
    private void OnRandomSuspectArrivedAtWindow(int index)
    {
        if (index != 1) return;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;

        Debug.Log("[Day_01] Slot 1 (index 1) arrived — slot 2 will be a random doc-anomaly suspect.");
    }

    // -------------------------------------------------------------------------
    // Documentation Anomaly Suspect (index 2) — teaches the exam notebook tutorial
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 2 (a random suspect
    /// that carries documentation anomalies). Server-side:
    ///   — Forces documentation anomalies onto the current suspect so the player has something
    ///     to find with the exam notebook checklist.
    /// Captures the arriving <see cref="SuspectCharacter"/> and subscribes
    /// <see cref="OnQuarantineSuspectPaperworkSpawned"/> so the documentation tutorial wires up
    /// to this exact suspect's own paperwork/pickup — never any other suspect's — once it
    /// spawns. The tutorial itself only starts once the player picks up a document (see
    /// <see cref="OnQuarantinePickupTrigger()"/>): starting it any earlier would collide with
    /// this suspect's own first-encounter intro monologue and prevent them from ever handing
    /// over their documents.
    /// Unsubscribes itself so it only fires once per day.
    /// </summary>
    private void OnDocAnomalySuspectArrivedAtWindow(int index)
    {
        if (index != 2) return;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;

        // Always apply documentation anomalies to the index-2 suspect so the player
        // learns the exam-notebook workflow on every run.
        // Swap stamps on all clients: this suspect requires quarantine, not a pass.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(true);

        // Capture the target suspect and wait for their own paperwork to spawn.
        _quarantineTargetSuspect = SuspectController.Instance?.CurrentSuspect;
        SuspectController.OnPaperworkSpawned += OnQuarantineSuspectPaperworkSpawned;

        if (NetworkManager.Singleton.IsServer)
        {
            SuspectController.Instance.CurrentSuspect?
                .InitializeWithDocumentationAnomalies(_quarantineDocumentationAnomalyCount);
        }
    }

    // -------------------------------------------------------------------------
    // Suspect Index 3 — random suspect; green stamp unlocked, Soldier armed
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 3 (a random suspect).
    /// Unlocks the green stamp (re-enabling it after the quarantine tutorial locked it for
    /// index 2) and arms the Soldier sequence for after this suspect is processed.
    /// </summary>
    private void OnStampsRestoredAtWindow(int index)
    {
        if (index != 3) return;

        SuspectController.OnSuspectArrived -= OnStampsRestoredAtWindow;

        // Quarantine tutorial (index 2) locked the green stamp. Unlock it now so the
        // player can use both green and yellow stamps when processing index 3.
        _greenStampSlot?.SetSlotInteractable(true);

        if (!NetworkManager.Singleton.IsServer) return;

        // Arm the Soldier scene sequence: fires when SuspectController would spawn the
        // next suspect (i.e. after index 3 has been processed and left the window).
        ArmSoldierSequence();
    }

    /// <summary>
    /// Fires on the server when any suspect's first-encounter intro dialogue completes.
    /// On Day 1 this is used to react to the current suspect's intro finishing (the general
    /// system handles their dialogue and paperwork; Day_01 only needs to know when it's done
    /// for any follow-up logic beyond what the encounter manager provides).
    /// </summary>
    private void OnSuspectFirstEncounterComplete(SuspectData data)
    {
        if (data == null) return;

        // Currently no additional Day 1 logic is needed after a suspect's intro beyond what
        // SuspectEncounterManager already handles (paperwork spawn + soldier arming).
        // This hook exists as an extension point for future use.
        Debug.Log($"[Day_01] First-encounter dialogue complete for '{data.name}'.");
    }

    /// <summary>
    /// Arms <see cref="SuspectController.InterceptNextSuspectSpawn"/> with the Soldier scene
    /// sequence. Safe to call multiple times; a no-op if <see cref="_soldierCharacter"/> is unassigned.
    /// </summary>
    private void ArmSoldierSequence()
    {
        if (_soldierCharacter == null) return;

        SuspectController.InterceptNextSuspectSpawn = () =>
        {
            StartCoroutine(ActivateAndStartSoldierDialogue());
        };

        Debug.Log("[Day_01] Soldier scene sequence armed.");
    }

    // -------------------------------------------------------------------------
    // Soldier Scene Sequence (triggered after the post-quarantine suspect leaves)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-side coroutine. Suppresses the Soldier's entry line, subscribes to
    /// <see cref="SuspectController.OnSuspectArrived"/> for his arrival, then calls
    /// <see cref="SuspectController.IntroduceSceneSuspect"/> to teleport him to the
    /// spawn point and kick off the standard DOTween walk-in. Dialogue starts once
    /// <see cref="OnSoldierArrivedAtWindow"/> fires (index 4), matching the Vlad
    /// pattern exactly.
    /// </summary>
    private IEnumerator ActivateAndStartSoldierDialogue()
    {
        if (_soldierCharacter == null)
        {
            Debug.LogWarning("[Day_01] _soldierCharacter is not assigned — skipping Soldier sequence.");
            yield break;
        }

        if (_soldierDialogue == null)
        {
            Debug.LogWarning("[Day_01] _soldierDialogue is not assigned — skipping Soldier sequence.");
            yield break;
        }

        // Suppress generic entry bark — the scripted dialogue owns his introduction.
        SuspectController.ForceNextSuspectSkipEntryDialogue = true;

        // The Soldier is purely scripted — he never hands over documents.
        SuspectController.ForceNextSuspectNoPaperwork = true;

        // Subscribe before teleporting so the arrival event is never missed.
        SuspectController.OnSuspectArrived += OnSoldierArrivedAtWindow;

        // Override the default standPos so the soldier walks to the cutscene-specific position.
        if (_soldierBoothPos != null)
            SuspectController.NextSuspectStandPosOverride = _soldierBoothPos;

        // Teleport to spawn point and begin the walk-in. OnSuspectArrived fires when he
        // reaches standPos, at which point OnSoldierArrivedAtWindow starts his dialogue.
        SuspectController.Instance.IntroduceSceneSuspect(_soldierCharacter);

        Debug.Log("[Day_01] Soldier walk-in initiated — awaiting arrival event (index 4).");
        yield break;
    }

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 4 (the Soldier).
    /// Server-only: starts the scripted dialogue after the settle delay.
    /// </summary>
    private void OnSoldierArrivedAtWindow(int index)
    {
        // In normal play the soldier is always suspect index 4 (right after the
        // post-quarantine suspect at index 3). When _debugSkipActive is true he is the only suspect spawned
        // (index 0), so accept any index on the debug-skip path to avoid the guard
        // blocking his dialogue.
        if (!_debugSkipActive && index != 4) return;

        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;

        if (!NetworkManager.Singleton.IsServer) return;

        StartCoroutine(WaitAndStartSoldierDialogue());
        Debug.Log($"[Day_01] Soldier (index {index}) arrived — starting dialogue after settle delay.");
    }

    private IEnumerator WaitAndStartSoldierDialogue()
    {
        yield return new WaitForSeconds(_soldierDialogueStartDelay);

        if (_soldierCharacter == null)
        {
            Debug.LogWarning("[Day_01] _soldierCharacter is null when trying to start Soldier dialogue.");
            yield break;
        }

        ScriptedDialogueRunner.Instance.PlayDialogue(_soldierCharacter, _soldierDialogue,
            OnSoldierDialogueComplete, deferExit: true);
        Debug.Log("[Day_01] Soldier scripted dialogue started.");
    }

    /// <summary>
    /// Called on the server once the Soldier's scripted dialogue finishes.
    /// Switches to the Look Up camera, plays the Alexei murder cutscene, then
    /// plays a megaphone instruction to the player (which also exits scripted mode).
    /// </summary>
    private void OnSoldierDialogueComplete()
    {
        Debug.Log("[Day_01] Soldier dialogue complete — starting mutant attack sequence.");
        StartCoroutine(MutantAttackSequence());
    }

    private IEnumerator MutantAttackSequence()
    {
        // Deactivate the SuspectCam and any override camera while keeping scripted mode active
        // so the player stays locked throughout the cutscene and megaphone lever dialogue.
        ScriptedDialogueRunner.Instance.ClearCamerasKeepMode();

        if (AlexeiController.Instance == null)
        {
            Debug.LogWarning("[Day_01] MutantAttackSequence: AlexeiController.Instance not found — skipping cutscene.");
        }
        else
        {
            // Register the idle callback before the cutscene plays so the Timeline SignalReceiver
            // can fire TriggerMutantEntrance mid-cutscene without needing to pass parameters.
            bool mutantIdle = false;
            AlexeiController.Instance.OnMutantIdleCallback = () => mutantIdle = true;
            AlexeiController.Instance.OnAlexeiSequenceDone = () =>
            {
                Debug.Log("[Day_01] OnAlexeiSequenceDone fired.");
                if (_postAlexeiDialogue != null)
                    ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(_postAlexeiDialogue, () =>
                    {
                        Debug.Log("[Day_01] Post-Alexei dialogue onComplete — calling TriggerEndOfShiftSetup.");
                        AlexeiController.Instance?.TriggerEndOfShiftSetup();
                        TutorialTaskSync.Instance?.BroadcastTrashTaskReadyServer();
                    });
                else
                {
                    Debug.LogWarning("[Day_01] _postAlexeiDialogue is not assigned — triggering end-of-shift setup immediately.");
                    AlexeiController.Instance?.TriggerEndOfShiftSetup();
                    TutorialTaskSync.Instance?.BroadcastTrashTaskReadyServer();
                }
            };

            // Start the cutscene — the Timeline signal mid-playback calls TriggerMutantEntrance.
            // The cutscene runs independently; the mutant entrance timing drives the sequence.
            AlexeiController.Instance.BeginSequence();

            // Dialogue fires _fallDuration + _idleAfterLandSeconds after the signal spawns Alexei
            // (target: 3 s). The cutscene may still be playing at this point — that's intentional.
            yield return new WaitUntil(() => mutantIdle);
        }

        Debug.Log("[Day_01] Mutant idling at booth — playing lever megaphone dialogue.");

        // Deactivate the shaky booth cam now that the cinematic phase is over.
        // This must happen before ExitScriptedMode so the cam is gone by the time
        // the player regains camera control.
        AlexeiController.Instance?.EndCinematicPhase();

        // Release the player before the lever dialogue so they can move to the lever
        // and use their own camera while listening to the instruction.
        ScriptedDialogueRunner.Instance.ExitScriptedMode();

        // Play the lever instruction as an unlocked megaphone line — the player stays
        // free to move while listening. Yield until the dialogue is dismissed before
        // activating the attack so Alexei only starts climbing after the player has been told.
        if (_leverDialogue != null)
        {
            bool leverDialogueDone = false;
            ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(
                _leverDialogue,
                () => leverDialogueDone = true,
                unlocked: true);
            yield return new WaitUntil(() => leverDialogueDone);
        }
        else
        {
            Debug.LogWarning("[Day_01] _leverDialogue is not assigned — activating attack behaviour immediately.");
        }

        // Unlock the lever and shutter immediately once the dialogue ends so the
        // player can act straight away — before Alexei's approach delay expires.
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;
        _lever?.SetInteractable(true);

        // Show a tutorial arrow on the lever and dismiss it once the player pulls it.
        ShowLeverMarker(true);
        StartCoroutine(HideLeverMarkerOnUse());

        // Give the player a moment to react before Alexei begins climbing.
        if (AlexeiController.Instance != null)
            yield return new WaitForSeconds(AlexeiController.Instance.BehaviourActivationDelay);

        AlexeiController.Instance?.ActivateAttackBehaviour();
    }

    // -------------------------------------------------------------------------
    // Networked Marker Helpers
    // -------------------------------------------------------------------------

    private void ShowLeverMarker(bool show)
    {
        if (_leverTutorialArrow == null) return;
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_leverTutorialArrow, show);
    }

    private IEnumerator HideLeverMarkerOnUse()
    {
        // The lever starts in the up (open) position. Wait until the player pulls
        // it down to close the shutter — that's the "used" moment.
        yield return new WaitUntil(() => _lever == null || !_lever.IsUp);
        ShowLeverMarker(false);
    }

    // -------------------------------------------------------------------------
    // Trash Task Tutorial
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on ALL clients via <see cref="TutorialTaskSync.OnTrashTaskReadyAllClients"/>.
    /// Adds the "Throw away trash X/Total" objective and subscribes to live progress
    /// updates. Previously this ran only wherever the scripted cutscene callback executed
    /// (the server), so the objective never appeared for remote clients — routing it
    /// through the TutorialTaskSync broadcast fixes that for every connected player.
    /// Also shows the trash and graffiti tutorial overlay screens back-to-back — this is
    /// the same moment both end-of-shift tasks are first triggered (see
    /// <see cref="AlexeiController.EndOfShiftSetupSequence"/>).
    /// </summary>
    private void OnTrashTaskReadySync()
    {
        TutorialTaskSync.OnTrashTaskReadyAllClients -= OnTrashTaskReadySync;

        if (TakeOutTrashTask.Instance == null) return;

        _trashTaskDone    = false;
        _graffitiTaskDone = false;

        _taskThrowTrash = TutorialObjectiveList.Instance?.AddObjective(
            GetTrashTaskText(TakeOutTrashTask.Instance.DepositedCount,
                             TakeOutTrashTask.Instance.TotalCount));

        TakeOutTrashTask.OnProgressChanged    += OnTrashProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited  += OnTrashTaskComplete;

        // The graffiti objective row is owned here (not by CleanGraffitiTask itself) so we
        // control exactly when it appears — right alongside the trash task, matching the
        // moment CleanGraffitiTask.TriggerDailyTask() was called in AlexeiController's
        // end-of-shift setup sequence.
        if (CleanGraffitiTask.Instance != null)
        {
            _taskCleanGraffiti = TutorialObjectiveList.Instance?.AddObjective(
                CleanGraffitiTask.Instance.GetTutorialObjectiveText());
            CleanGraffitiTask.OnProgressChanged            += OnGraffitiProgressChanged;
            CleanGraffitiTask.Instance.OnDailyTaskCompleted += OnGraffitiTaskComplete;
        }
        else
        {
            _graffitiTaskDone = true;
        }

        TutorialOverlay.Instance?.ShowTrashTutorial(
            () => TutorialOverlay.Instance?.ShowGraffitiTutorial());
    }

    private void OnGraffitiProgressChanged()
    {
        if (CleanGraffitiTask.Instance == null) return;
        _taskCleanGraffiti?.SetText(CleanGraffitiTask.Instance.GetTutorialObjectiveText());
    }

    private void OnTrashProgressChanged()
    {
        if (TakeOutTrashTask.Instance == null) return;
        _taskThrowTrash?.SetText(
            GetTrashTaskText(TakeOutTrashTask.Instance.DepositedCount,
                             TakeOutTrashTask.Instance.TotalCount));
    }

    private void OnTrashTaskComplete()
    {
        TakeOutTrashTask.OnProgressChanged   -= OnTrashProgressChanged;
        TakeOutTrashTask.OnAllItemsDeposited -= OnTrashTaskComplete;

        TutorialObjectiveList.Instance?.CompleteObjective(_taskThrowTrash);
        _taskThrowTrash = null;

        _trashTaskDone = true;
        TryFinishTrashAndGraffitiTutorials();
    }

    private void OnGraffitiTaskComplete()
    {
        if (CleanGraffitiTask.Instance != null)
            CleanGraffitiTask.Instance.OnDailyTaskCompleted -= OnGraffitiTaskComplete;
        CleanGraffitiTask.OnProgressChanged -= OnGraffitiProgressChanged;

        TutorialObjectiveList.Instance?.CompleteObjective(_taskCleanGraffiti);
        _taskCleanGraffiti = null;

        _graffitiTaskDone = true;
        TryFinishTrashAndGraffitiTutorials();
    }

    /// <summary>
    /// Only clears the shared objective list and advances to the clock-out step once BOTH
    /// the trash and graffiti end-of-shift tasks have been completed — whichever finishes
    /// second triggers this. Prevents the list (and the still in-progress task's row) from
    /// being wiped out early.
    /// </summary>
    private void TryFinishTrashAndGraffitiTutorials()
    {
        if (!_trashTaskDone || !_graffitiTaskDone) return;

        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f, onComplete: ShowClockOutTask);
    }

    private string GetTrashTaskText(int deposited, int total) =>
        total > 0
            ? $"{_taskThrowTrashText} {deposited}/{total}"
            : _taskThrowTrashText;

    // ── Clock Out ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the "Clock out" objective and points the tutorial arrow at, and highlights, the
    /// Time Card Machine. Called as the onComplete of HideAndClear once the trash and graffiti
    /// tasks both finish. The bunker sequence is deliberately withheld until the player clocks
    /// out — see <see cref="OnClockedOutForBunker"/>.
    /// </summary>
    private void ShowClockOutTask()
    {
        _taskClockOut = TutorialObjectiveList.Instance?.AddObjective(_taskClockOutText);

        // Reuse the same arrow/machine from the morning clock-in tutorial.
        ShowClockInArrow(true);
        _timeCardMachine?.Highlight(true);

        TimecardMachine.OnClockOutAllClients += OnClockedOutForBunker;
    }

    /// <summary>
    /// Fires on all clients via <see cref="TimecardMachine.OnClockOutAllClients"/> the instant
    /// the player punches out. Dismisses the clock-out tutorial, then advances to the
    /// "open the bunker" step.
    /// </summary>
    private void OnClockedOutForBunker()
    {
        TimecardMachine.OnClockOutAllClients -= OnClockedOutForBunker;

        ShowClockInArrow(false);
        _timeCardMachine?.Highlight(false);

        TutorialObjectiveList.Instance?.CompleteObjective(_taskClockOut);
        _taskClockOut = null;

        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f, onComplete: ShowOpenBunkerTask);
    }

    // ── Open Bunker ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the "Open the bunker" objective and a world-space arrow above the bunker door.
    /// Called as the onComplete of HideAndClear once the player clocks out.
    /// </summary>
    private void ShowOpenBunkerTask()
    {
        _taskOpenBunker = TutorialObjectiveList.Instance?.AddObjective(_taskOpenBunkerText);

        if (TutorialMarkerManager.Instance != null && _markerBunkerDoor != null)
            TutorialMarkerManager.Instance.Mark(_markerBunkerDoor);

        _bunkerDoorInteractable?.Highlight(true);

        BunkerDoorController.OnDoorOpened += OnBunkerDoorOpened;
    }

    private void OnBunkerDoorOpened()
    {
        BunkerDoorController.OnDoorOpened -= OnBunkerDoorOpened;

        if (TutorialMarkerManager.Instance != null && _markerBunkerDoor != null)
            TutorialMarkerManager.Instance.Unmark(_markerBunkerDoor);

        _bunkerDoorInteractable?.Highlight(false);

        TutorialObjectiveList.Instance?.CompleteObjective(_taskOpenBunker);
        _taskOpenBunker = null;

        TutorialObjectiveList.Instance?.HideAndClear(preHideDelay: 1.5f);

        ShowGoToBedMarker();
    }

    // ── Go to Bed ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a world-space arrow above the bunk bed and highlights it, pointing the player
    /// toward the bed without adding a checklist objective. Called immediately after the
    /// bunker door opens.
    /// </summary>
    private void ShowGoToBedMarker()
    {
        if (TutorialMarkerManager.Instance != null && _markerBunkBed != null)
            TutorialMarkerManager.Instance.Mark(_markerBunkBed);

        _bunkBedInteractable?.Highlight(true);

        BunkBedInteractable.OnSleepConfirmed += OnGoToBedSleepConfirmed;
    }

    private void OnGoToBedSleepConfirmed()
    {
        BunkBedInteractable.OnSleepConfirmed -= OnGoToBedSleepConfirmed;

        if (TutorialMarkerManager.Instance != null && _markerBunkBed != null)
            TutorialMarkerManager.Instance.Unmark(_markerBunkBed);

        _bunkBedInteractable?.Highlight(false);

        // Safety net: the objective list should already be hidden by OnBunkerDoorOpened, but
        // if any upstream tutorial step failed to clear it (e.g. a completion notification
        // that didn't reach this client), force it closed now so it never carries over into
        // the night phase or the next day. Safe to call when already hidden.
        TutorialObjectiveList.Instance?.HideAndClear();
    }

    // -------------------------------------------------------------------------
    // Debug Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Debug-only server method. Aborts the normal Day 1 opening sequence and jumps
    /// directly to the Soldier's slot, bypassing Vlad, the random civilian, the
    /// documentation-anomaly suspect, and the post-quarantine suspect entirely. Intended to
    /// be called by <see cref="DebugConsole"/> so the Soldier sequence can be tested in isolation.
    ///
    /// Side effects:
    ///   — Stops all running coroutines on this component (cancels the 7 s shutter delay).
    ///   — Opens and locks the shutter immediately.
    ///   — Clears <see cref="HandOffPoint.BlockVerdict"/> (Vlad's verdict will never arrive).
    ///   — Arms <see cref="SuspectController.InterceptNextSuspectSpawn"/> with the Soldier
    ///     scene sequence so the very next <see cref="SuspectController.NextSuspect"/> call
    ///     triggers his walk-in.
    /// </summary>
    public void DebugSkipToSoldierSlot()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Prevent OnDayStarted from launching Day1OpeningSequence when TryStartShift
        // fires OnDayStart — the entire opening sequence (Vlad, civilians, quarantine suspect) is skipped.
        _debugSkipActive = true;

        // Cancel any pending Day 1 coroutines so the 7s delay can't re-arm Vlad's intercept.
        StopAllCoroutines();

        // The clock-in tutorial is bypassed too — dismiss the arrow and cancel the pending
        // server-side clock-in reaction so a late punch can't re-trigger Day1OpeningSequence.
        ShowClockInArrow(false);
        TimecardMachine.OnClockInServer -= OnPlayerClockedIn;

        // Unsubscribe all early-day arrival handlers so none of them fire when the soldier
        // arrives. Without this, OnVladArrivedAtWindow fires for index 0 and plays
        // _vladDialogue on the soldier character.
        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnStampsRestoredAtWindow;

        // Open and lock the shutter so the Soldier can walk up to the window.
        ShutterController.Instance?.OpenShutter();
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = true;

        // Animate the lever to the open position and lock it — mirrors Day1OpeningSequence
        // since the opening sequence is bypassed on the debug-skip path.
        _lever?.AnimateOpenServerSide(0.3f);
        _lever?.SetInteractable(false);

        // Vlad's closing dialogue will never play, so unblock the verdict.
        HandOffPoint.BlockVerdict = false;

        if (_soldierCharacter == null || _soldierDialogue == null)
        {
            Debug.LogWarning("[Day_01] DebugSkipToSoldierSlot: _soldierCharacter or _soldierDialogue not assigned — cannot arm Soldier intercept.");
            return;
        }

        SuspectController.InterceptNextSuspectSpawn = () =>
        {
            StartCoroutine(ActivateAndStartSoldierDialogue());
        };

        Debug.Log("[Day_01] DebugSkipToSoldierSlot: Soldier intercept armed. Call SuspectController.NextSuspect() to trigger.");
    }

    /// <summary>
    /// Sets up Day 1 for free play: bypasses the entire opening sequence (Vlad, civilians,
    /// quarantine suspect), clears the Alexei intercept so normal suspects arrive, and unlocks all
    /// tutorial-gated items (stamps, folder stack, doc exam shop item).
    /// Call <c>ShiftManager.Instance.TryStartShift()</c> after this to begin the shift.
    /// </summary>
    public void DebugFreePlaySetup()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // DebugSkipToSoldierSlot handles: _debugSkipActive flag, stopping coroutines,
        // unsubscribing all scripted arrival handlers, opening/locking the shutter and
        // lever, and clearing HandOffPoint.BlockVerdict.
        DebugSkipToSoldierSlot();

        // Free play — no soldier. Clear the intercept so the normal suspect queue runs.
        SuspectController.InterceptNextSuspectSpawn = null;

        // Unlock everything that DayActivated locked behind tutorial gates.
        _stackOfFolders?.SetInteractable(true);
        _documentationExamShopItem?.SetAvailable(true);
        _greenStampSlot?.SetSlotInteractable(true);
        _yellowStampSlot?.SetSlotInteractable(true);
        _redStampSlot?.SetSlotInteractable(true);

        Debug.Log("[Day_01] DebugFreePlaySetup: Day 1 configured for free play — tutorial gates bypassed, normal suspects will arrive.");
    }

    // -------------------------------------------------------------------------
    // Deferred Verdict
    // -------------------------------------------------------------------------

    private void DeliverDeferredVerdict()
    {
        FolderController pending = HandOffPoint.PendingVerdictFolder;
        HandOffPoint.ClearPendingVerdict();

        if (pending != null && SuspectController.Instance != null)
            SuspectController.Instance.DeliverVerdict(pending);
    }
}

// end of file