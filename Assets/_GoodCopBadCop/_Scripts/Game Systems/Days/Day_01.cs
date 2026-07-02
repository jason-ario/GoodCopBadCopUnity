using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 1 scripted opening sequence.
///
/// Flow:
///   1. Day activates — all stamps, drawer, and notebooks are immediately unlocked.
///      Mutation and biological notebooks are hidden until Day 2/3 introduce them.
///   2. Intro cutscene ends → <see cref="ShiftManager.OnDayStart"/> fires →
///      <see cref="OnDayStarted"/> runs server-side.
///   3. After a 7-second hold, the rolling shutter opens automatically and is
///      locked open via <see cref="ShutterController.ShutterLockedOpen"/>.
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
    [Header("Day 1 — Booth")]
    [Tooltip("The booth drawer the player can open freely on Day 1.")]
    [SerializeField] private Drawer _drawer;

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

    [Header("Day 1 — Lineup Override (Slots 1–3)")]
    [Tooltip("2nd suspect (index 1). When assigned, this character is force-spawned instead of a random clean " +
             "civilian and SuspectEncounterManager plays their intro dialogue. Leave null for default random behavior.")]
    [SerializeField] private SuspectCharacter _slot1Prefab;

    [Tooltip("3rd suspect (index 2). When assigned, this character is force-spawned instead of the random " +
             "documentation-anomaly suspect and the exam-notebook tutorial is skipped. Leave null for default " +
             "tutorial behavior.")]
    [SerializeField] private SuspectCharacter _slot2Prefab;

    [Tooltip("4th suspect (index 3). Assign any SuspectCharacter prefab — SuspectEncounterManager will " +
             "play their intro dialogue automatically. Leave null to skip this slot.")]
    [SerializeField] private SuspectCharacter _slot3Prefab;

    [Header("Day 1 — Soldier")]
    [Tooltip("The Soldier's SuspectCharacter placed directly in the scene (not runtime-spawned). " +
             "IMPORTANT: Must be ACTIVE in the scene at load time so NGO auto-spawns his NetworkObject. " +
             "Position him off-screen (e.g. underground) — IntroduceSceneSuspect teleports him to " +
             "the spawn point when his sequence fires, then he walks in like a normal suspect.")]
    [SerializeField] private SuspectCharacter _soldierCharacter;

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

    [Tooltip("Custom stand position for the Soldier at the booth window. " +
             "Overrides SuspectController's default standPos for the soldier's walk-in.")]
    [SerializeField] private Transform _soldierBoothPos;

    [Header("Day 1 — Ivan Documentation Tutorial")]
    [Tooltip("ShopItem.Name of the Documentation Exam pile — used to make it free during the tutorial.")]
    [SerializeField] private string _documentationExamItemName = "Documentation Exam";

    [Tooltip("Number of documentation anomalies to force-activate on Ivan for the Day 1 tutorial.")]
    [SerializeField] private int _ivanDocumentationAnomalyCount = 2;

    [Tooltip("Task text shown while the player needs to get a documentation checklist.")]
    [SerializeField] private string _taskGetChecklistText = "Get a documentation checklist from the drawer";

    [Tooltip("Task text shown while the player needs to check documentation anomalies and file the page.")]
    [SerializeField] private string _taskCheckDocumentationText = "Check documentation anomalies and add the page to the folder";

    [Tooltip("First megaphone scripted dialogue — plays when the player first picks up one of Ivan's documents.")]
    [SerializeField] private ScriptedDialogue _ivanMegaphonePart1;

    [Tooltip("Second megaphone scripted dialogue — plays after the player picks up the documentation checklist.")]
    [SerializeField] private ScriptedDialogue _ivanMegaphonePart2;

    [Tooltip("Seconds after OnDayStart fires before the shutter opens and Vlad is triggered.")]
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
    [Tooltip("HUD task text shown while the player needs to pick up the documents.")]
    [SerializeField] private string _taskPickUpDocs = "Pick up Vlad's ID card and application";

    [Tooltip("HUD task text shown while the player needs to file the documents in a folder.")]
    [SerializeField] private string _taskFileDocs = "Get a folder from the drawer and file the documents";

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

    // Tracks which of Ivan's two documents have been spawned — used to wire pickup triggers.
    private PickableObject _ivanDoc1;
    private PickableObject _ivanDoc2;

    // Active Ivan documentation tutorial tasks.
    private TutorialTask _taskGetChecklist;
    private TutorialTask _taskCheckDocumentation;

    // Tracks how many of Vlad's documents have been filed into a folder.
    private int _docsFiledCount;

    // Active tutorial task entries — created when each step begins, removed when done.
    private TutorialTask _taskPickUp;
    private TutorialTask _taskFile;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        _dayStartedFired = false;
        _debugSkipActive = false;

        // Drawer is unlocked so the player can grab a folder during the tutorial.
        _drawer?.SetLocked(false);

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
        SuspectController.OnSuspectArrived += OnIvanArrivedAtWindow;
        SuspectController.OnPaperworkSpawned += OnVladPaperworkSpawned; // advances through random → doc anomaly → Ivan
        FolderController.OnDocumentAdded += OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped += OnTutorialFolderStamped;
        SuspectEncounterManager.OnFirstEncounterDialogueComplete += OnSuspectFirstEncounterComplete;

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

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnIvanArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnRandomSuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnDocAnomalySuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnIvanPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;
        FolderController.OnFolderEquipped -= OnIvanPickupTrigger;
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnIvanExamPickedUp;
        ExamNotebook.OnAnyNotebookPageFiled -= OnIvanPageFiled;
        SuspectEncounterManager.OnFirstEncounterDialogueComplete -= OnSuspectFirstEncounterComplete;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();
        UnsubscribeIvanDocumentPickupEvents();

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

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnIvanArrivedAtWindow;
        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnRandomSuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnDocAnomalySuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned -= OnIvanPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;
        FolderController.OnFolderEquipped -= OnIvanPickupTrigger;
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnIvanExamPickedUp;
        ExamNotebook.OnAnyNotebookPageFiled -= OnIvanPageFiled;
        SuspectEncounterManager.OnFirstEncounterDialogueComplete -= OnSuspectFirstEncounterComplete;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();
        UnsubscribeIvanDocumentPickupEvents();
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
        if (!NetworkManager.Singleton.IsServer) return;
        if (_dayStartedFired) return;
        _dayStartedFired = true;

        // DayActivated runs before NGO spawns scene NetworkObjects on the debug-skip path,
        // so stamp calls there are silently ignored. Re-apply the correct locked state here
        // now that all NetworkBehaviours are guaranteed to be spawned.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);

        // When a debug skip is active, the opening sequence (Vlad / civilians / Ivan) is
        // intentionally bypassed — DebugSkipToSoldierSlot handles the shift start itself.
        if (_debugSkipActive)
        {
            Debug.Log("[Day_01] OnDayStarted: debug skip active — skipping Day1OpeningSequence.");
            return;
        }

        StartCoroutine(Day1OpeningSequence());
    }

    /// <summary>
    /// Server-side coroutine. Waits <see cref="_shutterOpenDelay"/> seconds, then:
    ///   - Opens and locks the rolling shutter.
    ///   - Arms the Vlad intercept on the first suspect spawn slot (no paperwork, no entry line).
    ///   - Auto-starts the shift (bypassing the switch button for this scripted day).
    ///   - Overrides the first suspect arrival interval to fire immediately so Vlad
    ///     begins walking as soon as the shift's window sequence completes.
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

        // Fire immediately once the shift's opening window sequence finishes.
        ShiftManager.OverrideFirstArrivalInterval = new UnityEngine.Vector2(0f, 0f);

        // Start the shift automatically — no switch press required on Day 1.
        ShiftManager.Instance.TryStartShift();

        Debug.Log("[Day_01] Shutter opened and Vlad intercept armed — shift auto-started.");
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
    /// documents so we can track when the local player picks them up.
    /// Immediately swaps the subscription so the random suspect's paperwork (spawned after
    /// Vlad leaves) is handled by <see cref="OnRandomSuspectPaperworkSpawned"/>, which in
    /// turn swaps to <see cref="OnIvanPaperworkSpawned"/> so only Ivan (suspect index 2)
    /// triggers the documentation tutorial.
    /// </summary>
    private void OnVladPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        // Advance the chain: random suspect's paperwork goes to the next handler, not Ivan's.
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        SuspectController.OnPaperworkSpawned += OnRandomSuspectPaperworkSpawned;
        _vladIDCard = card;
        _vladAppForm = appForm;

        if (card != null)
            card.OnPickedUpEvent += OnVladDocumentPickedUp;

        if (appForm != null)
            appForm.OnPickedUpEvent += OnVladDocumentPickedUp;

        // Add task 1 to the HUD task list. OnPaperworkSpawned fires on all clients
        // via NotifyPaperworkSpawnedClientRpc so no extra RPC is needed.
        _taskPickUp = new TutorialTask(_taskPickUpDocs);
        GuidebookTaskRegistry.Instance.AddThreat(_taskPickUp);
    }

    /// <summary>
    /// Fires on all clients when the random (second) suspect's paperwork spawns.
    /// Advances the handler chain so the third suspect's (doc-anomaly) paperwork
    /// triggers the documentation tutorial.
    /// </summary>
    private void OnRandomSuspectPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        SuspectController.OnPaperworkSpawned -= OnRandomSuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned += OnDocAnomalySuspectPaperworkSpawned;
    }

    private void OnVladDocumentPickedUp()
    {
        _docsPickedUp++;
        if (_docsPickedUp < 2) return;

        // Both documents in hand — clean up listeners, trigger next step.
        UnsubscribeDocumentPickupEvents();
        StartCoroutine(VladFolderBarkRoutine());
    }

    private IEnumerator VladFolderBarkRoutine()
    {
        yield return new WaitForSeconds(_vladFolderBarkDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladFolderBark))
            vlad.Speaking.Say(_vladFolderBark);

        // Swap task 1 → task 2 locally (only the player who picked up both docs needs this).
        if (_taskPickUp != null)
            GuidebookTaskRegistry.Instance.RemoveThreat(_taskPickUp);

        _taskFile = new TutorialTask(_taskFileDocs);
        GuidebookTaskRegistry.Instance.AddThreat(_taskFile);
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

    private void UnsubscribeIvanDocumentPickupEvents()
    {
        if (_ivanDoc1 != null) { _ivanDoc1.OnPickedUpEvent -= OnIvanPickupTrigger; _ivanDoc1 = null; }
        if (_ivanDoc2 != null) { _ivanDoc2.OnPickedUpEvent -= OnIvanPickupTrigger; _ivanDoc2 = null; }
        FolderController.OnFolderEquipped -= OnIvanPickupTrigger;
    }

    // -------------------------------------------------------------------------
    // Documentation Anomaly Suspect Paperwork & Tutorial (index 2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when the documentation-anomaly suspect's paperwork spawns
    /// (suspect index 2). Hooks pickup triggers on both documents and any folder equip so
    /// that the first pickup starts the megaphone tutorial sequence.
    /// Advances the chain to <see cref="OnIvanPaperworkSpawned"/> for Ivan's turn.
    /// </summary>
    private void OnDocAnomalySuspectPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        SuspectController.OnPaperworkSpawned -= OnDocAnomalySuspectPaperworkSpawned;
        SuspectController.OnPaperworkSpawned += OnIvanPaperworkSpawned;

        // Wire documentation tutorial triggers for any index-2 suspect, whether they come
        // from a specific prefab or are a random civilian. The megaphone plays after the
        // player picks up their paperwork, which always follows their intro conversation.
        _ivanDoc1 = card;
        _ivanDoc2 = appForm;

        if (card != null)    card.OnPickedUpEvent    += OnIvanPickupTrigger;
        if (appForm != null) appForm.OnPickedUpEvent  += OnIvanPickupTrigger;
        FolderController.OnFolderEquipped += OnIvanPickupTrigger;
    }

    // -------------------------------------------------------------------------
    // Ivan Paperwork (index 3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when Ivan's paperwork spawns (suspect index 3).
    /// The documentation tutorial was already completed on suspect index 2 — no pickup
    /// triggers are needed here. Simply cleans up the subscription.
    /// </summary>
    private void OnIvanPaperworkSpawned(IDCard card, PickableObject appForm)
    {
        SuspectController.OnPaperworkSpawned -= OnIvanPaperworkSpawned;
    }

    /// <summary>
    /// Fires when the player picks up one of Ivan's documents or any folder.
    /// Kicks off the documentation tutorial — subscribes for single-fire then shows the
    /// first tutorial task and starts the megaphone bark sequence on the server.
    /// Signature matches both <see cref="PickableObject.OnPickedUpEvent"/> (Action)
    /// and <see cref="FolderController.OnFolderEquipped"/> (Action&lt;FolderController&gt;)
    /// via separate adapter overloads below.
    /// </summary>
    private void StartIvanDocumentationTutorial()
    {
        // Single-fire — remove all triggers immediately.
        UnsubscribeIvanDocumentPickupEvents();

        // Task 1: shown on all clients right away.
        _taskGetChecklist = new TutorialTask(_taskGetChecklistText);
        GuidebookTaskRegistry.Instance.AddThreat(_taskGetChecklist);

        // Subscribe completion handlers on all clients.
        ExamNotebook.AnyExamNotebookPickedUp = false;
        ExamNotebook.OnAnyExamNotebookPickedUp += OnIvanExamPickedUp;
        ExamNotebook.AnyPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnIvanPageFiled;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            StartCoroutine(IvanDocumentationBarkRoutine());
    }

    // Parameterless adapter — used by PickableObject.OnPickedUpEvent.
    private void OnIvanPickupTrigger() => StartIvanDocumentationTutorial();

    // FolderController.OnFolderEquipped passes the instance — we ignore it.
    private void OnIvanPickupTrigger(FolderController _) => StartIvanDocumentationTutorial();

    /// <summary>
    /// Fires on all clients when the player picks up any exam notebook.
    /// Completes the "get checklist" task and activates the "check anomalies" task.
    /// </summary>
    private void OnIvanExamPickedUp()
    {
        ExamNotebook.OnAnyExamNotebookPickedUp -= OnIvanExamPickedUp;

        if (_taskGetChecklist != null)
        {
            GuidebookTaskRegistry.Instance.RemoveThreat(_taskGetChecklist);
            _taskGetChecklist = null;
        }

        _taskCheckDocumentation = new TutorialTask(_taskCheckDocumentationText);
        GuidebookTaskRegistry.Instance.AddThreat(_taskCheckDocumentation);
    }

    /// <summary>
    /// Fires on all clients when any exam notebook page is filed into a folder.
    /// Completes the "check anomalies" task.
    /// </summary>
    private void OnIvanPageFiled()
    {
        ExamNotebook.OnAnyNotebookPageFiled -= OnIvanPageFiled;

        if (_taskCheckDocumentation != null)
        {
            GuidebookTaskRegistry.Instance.RemoveThreat(_taskCheckDocumentation);
            _taskCheckDocumentation = null;
        }
    }

    /// <summary>
    /// Server-only coroutine. Plays the documentation tutorial megaphone dialogue sequences:
    ///   — Part 1: two lines explaining the paperwork discrepancy (clickable subtitles).
    ///   — Makes the documentation exam free and waits for the player to pick it up.
    ///   — Part 2: four follow-up lines explaining documentation anomalies.
    /// Both parts are played through <see cref="ScriptedDialogueRunner.PlayMegaphoneDialogue"/>
    /// so the player clicks to advance each line exactly as with character dialogue.
    /// </summary>
    private IEnumerator IvanDocumentationBarkRoutine()
    {
        var runner = ScriptedDialogueRunner.Instance;
        var mgr = MegaphoneDialogueManager.Instance;
        if (runner == null) yield break;

        // ── Part 1: 2 intro lines ─────────────────────────────────────────────
        if (_ivanMegaphonePart1 != null)
        {
            bool part1Done = false;
            runner.PlayMegaphoneDialogue(_ivanMegaphonePart1, () => part1Done = true);
            yield return new WaitUntil(() => part1Done);
        }
        else
        {
            Debug.LogWarning("[Day_01] _ivanMegaphonePart1 is not assigned — skipping first megaphone sequence.");
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
        if (_ivanMegaphonePart2 != null)
        {
            bool part2Done = false;
            runner.PlayMegaphoneDialogue(_ivanMegaphonePart2, () => part2Done = true);
            yield return new WaitUntil(() => part2Done);
        }
        else
        {
            Debug.LogWarning("[Day_01] _ivanMegaphonePart2 is not assigned — skipping second megaphone sequence.");
        }
    }

    // -------------------------------------------------------------------------
    // Tutorial — Folder placed on desk
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires when any item is placed on the desk placement board.
    /// Checks the item is a folder, then has Vlad prompt the player to file documents.
    /// Unsubscribes after first valid placement so the bark only fires once.
    /// </summary>
    private void OnFolderPlacedOnDesk(PickableObject item)
    {
        if (item == null || item.GetComponent<FolderController>() == null) return;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad?.Speaking != null && !string.IsNullOrEmpty(_vladFolderPlacedBark))
            vlad.Speaking.Say(_vladFolderPlacedBark);
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

        // Both core documents filed — remove task 2 and unsubscribe.
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;

        if (_taskFile != null)
            GuidebookTaskRegistry.Instance.RemoveThreat(_taskFile);

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
    /// Fires when an item is placed on the window hand-off PlacementBoard.
    /// Checks it is a folder and, server-side, starts the closing scripted dialogue after a
    /// short delay. Unsubscribes immediately so the sequence can only trigger once.
    /// </summary>
    private void OnFolderHandedToVlad(PickableObject item)
    {
        if (item == null || item.GetComponent<FolderController>() == null) return;

        // Unsubscribe before any async work to guarantee single-fire.
        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        if (!NetworkManager.Singleton.IsServer) return;

        // Lock the folder immediately so the player cannot pick it back up during the cutscene.
        item.SetInteractableNetworked(false);

        if (_vladClosingDialogue == null)
        {
            Debug.LogWarning("[Day_01] _vladClosingDialogue is not assigned — skipping closing dialogue.");
            return;
        }

        StartCoroutine(StartClosingDialogue());
    }

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
    /// Suppresses Vlad's exit line, then either force-spawns the configured slot 1 character
    /// or lets a random clean civilian walk through naturally (index 1).
    /// </summary>
    private void OnClosingDialogueComplete()
    {
        // Silence Vlad's generic exit bark — his story ends with "Don't fuck it up."
        SuspectController.ForceNextSuspectSkipExitDialogue = true;

        if (_slot1Prefab != null)
        {
            // Force-spawn the configured slot 1 character — SuspectEncounterManager will
            // intercept SayEntryDialogue and play their intro dialogue automatically.
            SuspectController.InterceptNextSuspectSpawn = () =>
                SuspectController.Instance.SpawnScriptedSuspect(_slot1Prefab);
            Debug.Log($"[Day_01] Closing dialogue complete — slot 1 intercept armed for '{_slot1Prefab.name}'.");
        }
        else
        {
            // Default: next spawn slot is a random civilian with no anomalies.
            SuspectController.ForceNextSuspectClean = true;
            Debug.Log("[Day_01] Closing dialogue complete — slot 1 is random clean civilian.");
        }

        DeliverDeferredVerdict();
    }

    // -------------------------------------------------------------------------
    // Random Suspect (index 1) — clean civilian between Vlad and the tutorial suspects
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 1 (the random clean
    /// civilian following Vlad). Simply unsubscribes; no intercept is needed because the
    /// next spawn (index 2) should be a random suspect that receives documentation anomalies
    /// when they arrive via <see cref="OnDocAnomalySuspectArrivedAtWindow"/>.
    /// </summary>
    private void OnRandomSuspectArrivedAtWindow(int index)
    {
        if (index != 1) return;
        SuspectController.OnSuspectArrived -= OnRandomSuspectArrivedAtWindow;

        if (!NetworkManager.Singleton.IsServer) return;

        if (_slot2Prefab != null)
        {
            // Arm the slot 2 intercept so the next spawn delivers the configured character.
            SuspectController.InterceptNextSuspectSpawn = () =>
                SuspectController.Instance.SpawnScriptedSuspect(_slot2Prefab);
            Debug.Log($"[Day_01] Slot 1 (index 1) arrived — slot 2 intercept armed for '{_slot2Prefab.name}'.");
        }
        else
        {
            Debug.Log("[Day_01] Slot 1 (index 1) arrived — slot 2 is random doc-anomaly suspect.");
        }
    }

    // -------------------------------------------------------------------------
    // Documentation Anomaly Suspect (index 2) — teaches the exam notebook tutorial
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 2 (the random suspect
    /// that carries a documentation anomaly). Server-side:
    ///   — Forces documentation anomalies onto the current suspect so the player has something
    ///     to find with the exam notebook checklist.
    ///   — Arms the Ivan intercept so the very next spawn (index 3) delivers Ivan.
    /// Unsubscribes itself so it only fires once per day.
    /// </summary>
    private void OnDocAnomalySuspectArrivedAtWindow(int index)
    {
        if (index != 2) return;
        SuspectController.OnSuspectArrived -= OnDocAnomalySuspectArrivedAtWindow;

        // Always apply documentation anomalies to the index-2 suspect, regardless of whether
        // they come from a specific prefab or are a random civilian. The megaphone tutorial
        // must fire after their intro conversation completes so the player learns the exam-
        // notebook workflow on every run.
        // Swap stamps on all clients: this suspect requires quarantine, not a pass.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(true);

        if (NetworkManager.Singleton.IsServer)
        {
            SuspectController.Instance.CurrentSuspect?
                .InitializeWithDocumentationAnomalies(_ivanDocumentationAnomalyCount);
        }

        if (!NetworkManager.Singleton.IsServer) return;

        // Arm slot 3 if a prefab is configured — SuspectEncounterManager handles their intro.
        if (_slot3Prefab != null)
        {
            SuspectController.InterceptNextSuspectSpawn = () =>
                SuspectController.Instance.SpawnScriptedSuspect(_slot3Prefab);
            Debug.Log($"[Day_01] Slot 2 (index 2) arrived — slot 3 intercept armed for '{_slot3Prefab.name}'.");
        }
    }

    // -------------------------------------------------------------------------
    // Ivan Scripted Dialogue (index 3) — handled by SuspectEncounterManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Reacts to index 3 (Ivan).
    /// Unlocks the green stamp (re-enabling it after the quarantine tutorial locked it) and
    /// arms the Soldier scene sequence for after Ivan is processed.
    /// Ivan's intro dialogue and paperwork are handled automatically by
    /// <see cref="SuspectEncounterManager"/> via his <see cref="SuspectData.introDialogue"/>.
    /// </summary>
    private void OnIvanArrivedAtWindow(int index)
    {
        if (index != 3) return;

        SuspectController.OnSuspectArrived -= OnIvanArrivedAtWindow;

        // Quarantine tutorial (index 2) locked the green stamp. Unlock it now so the
        // player can use both green and yellow stamps when processing Ivan.
        _greenStampSlot?.SetSlotInteractable(true);

        if (!NetworkManager.Singleton.IsServer) return;

        // Arm the Soldier scene sequence: fires when SuspectController would spawn the next
        // suspect (i.e. after Ivan has been processed and left the window).
        if (_soldierCharacter != null)
        {
            SuspectController.InterceptNextSuspectSpawn = () =>
            {
                StartCoroutine(ActivateAndStartSoldierDialogue());
            };

            Debug.Log("[Day_01] Ivan (index 3) arrived — Soldier scene sequence armed.");
        }
    }

    /// <summary>
    /// Fires on the server when any suspect's first-encounter intro dialogue completes.
    /// On Day 1 this is used to react to Ivan's intro finishing (the general system
    /// handles his dialogue and paperwork; Day_01 only needs to know when he is done
    /// for any follow-up logic beyond what the encounter manager provides).
    /// </summary>
    private void OnSuspectFirstEncounterComplete(SuspectData data)
    {
        if (data == null) return;

        // Currently no additional Day 1 logic is needed after Ivan's intro beyond what
        // SuspectEncounterManager already handles (paperwork spawn + soldier arming).
        // This hook exists as an extension point for future use.
        Debug.Log($"[Day_01] First-encounter dialogue complete for '{data.name}'.");
    }

    // -------------------------------------------------------------------------
    // Soldier Scene Sequence (triggered after Ivan leaves)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-side coroutine. Suppresses the Soldier's entry line, subscribes to
    /// <see cref="SuspectController.OnSuspectArrived"/> for his arrival, then calls
    /// <see cref="SuspectController.IntroduceSceneSuspect"/> to teleport him to the
    /// spawn point and kick off the standard DOTween walk-in. Dialogue starts once
    /// <see cref="OnSoldierArrivedAtWindow"/> fires (index 4), matching the Vlad/Ivan
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
        if (index != 4) return;

        SuspectController.OnSuspectArrived -= OnSoldierArrivedAtWindow;

        if (!NetworkManager.Singleton.IsServer) return;

        StartCoroutine(WaitAndStartSoldierDialogue());
        Debug.Log("[Day_01] Soldier (index 4) arrived — starting dialogue after settle delay.");
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
        // Deactivate any override camera so the default At Booth Cam shows during the cutscene.
        ScriptedDialogueRunner.Instance.SwitchCamera(string.Empty);

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
                if (_postAlexeiDialogue != null)
                    ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(_postAlexeiDialogue, () =>
                    {
                        if (ShiftManager.Instance != null)
                            ShiftManager.Instance.EndShift();
                        else
                            Debug.LogWarning("[Day_01] ShiftManager.Instance not found — cannot end shift.");
                    });
                else
                {
                    Debug.LogWarning("[Day_01] _postAlexeiDialogue is not assigned — ending shift immediately.");
                    if (ShiftManager.Instance != null)
                        ShiftManager.Instance.EndShift();
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

        // PlayMegaphoneDialogue re-enters scripted mode (safe no-op if already in it),
        // plays the instruction line, and exits scripted mode when the player advances.
        // The lever is unlocked and the shutter lock released once the player has been told to use it.
        if (_leverDialogue != null)
            ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(_leverDialogue, () =>
            {
                if (ShutterController.Instance != null)
                    ShutterController.Instance.ShutterLockedOpen = false;
                _lever?.SetInteractable(true);
            });
        else
        {
            Debug.LogWarning("[Day_01] _leverDialogue is not assigned — exiting scripted mode manually.");
            ScriptedDialogueRunner.Instance.ExitScriptedMode();
            if (ShutterController.Instance != null)
                ShutterController.Instance.ShutterLockedOpen = false;
            _lever?.SetInteractable(true);
        }
    }

    // -------------------------------------------------------------------------
    // Debug Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Debug-only server method. Aborts the normal Day 1 opening sequence and jumps
    /// directly to the Soldier's slot, bypassing Vlad, the random civilian, the
    /// documentation-anomaly suspect, and Ivan entirely. Intended to be called by
    /// <see cref="DebugConsole"/> so the Soldier sequence can be tested in isolation.
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
        // fires OnDayStart — the entire opening sequence (Vlad, civilians, Ivan) is skipped.
        _debugSkipActive = true;

        // Cancel any pending Day 1 coroutines so the 7s delay can't re-arm Vlad's intercept.
        StopAllCoroutines();

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