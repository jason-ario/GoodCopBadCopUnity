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

    // Tracks which of Vlad's two documents have been picked up so far.
    private PickableObject _vladIDCard;
    private PickableObject _vladAppForm;
    private int _docsPickedUp;

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
        SuspectController.OnPaperworkSpawned += OnVladPaperworkSpawned;
        FolderController.OnDocumentAdded += OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped += OnTutorialFolderStamped;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced += OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced += OnFolderHandedToVlad;

        // Block the HandOffPoint from immediately processing the verdict on Day 1 so the
        // closing cutscene can play first. The verdict is delivered in OnClosingDialogueComplete.
        HandOffPoint.BlockVerdict = true;

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

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();

        StopAllCoroutines();

        Debug.Log("[Day_01] DayDeactivated.");
    }

    private void OnDestroy()
    {
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
        SuspectController.OnPaperworkSpawned -= OnVladPaperworkSpawned;
        FolderController.OnDocumentAdded -= OnDocumentFiledInFolder;
        FolderController.OnAnyFolderStamped -= OnTutorialFolderStamped;

        if (_deskPlacementBoard != null)
            _deskPlacementBoard.OnItemPlaced -= OnFolderPlacedOnDesk;

        if (_windowPlacementBoard != null)
            _windowPlacementBoard.OnItemPlaced -= OnFolderHandedToVlad;

        HandOffPoint.ClearPendingVerdict();

        UnsubscribeDocumentPickupEvents();
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
    /// Server-side coroutine. Drops Vlad's papers on the desk then — after a short pause
    /// that allows them to settle — has him deliver his bark and shows the first HUD task.
    /// </summary>
    private IEnumerator VladPaperworkTutorialRoutine()
    {
        yield return new WaitForSeconds(_vladPaperworkDelay);

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
    /// </summary>
    private void OnVladPaperworkSpawned(IDCard card, PickableObject appForm)
    {
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
    /// Clears the verdict block and delivers any folder that was held back during the cutscene.
    /// </summary>
    private void OnClosingDialogueComplete()
    {
        DeliverDeferredVerdict();
        Debug.Log("[Day_01] Closing dialogue complete — deferred verdict delivered.");
    }

    private void DeliverDeferredVerdict()
    {
        FolderController pending = HandOffPoint.PendingVerdictFolder;
        HandOffPoint.ClearPendingVerdict();

        if (pending != null && SuspectController.Instance != null)
            SuspectController.Instance.DeliverVerdict(pending);
    }
}

// end of file