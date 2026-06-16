using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 1 — the tutorial shift.
///
/// Orchestrates every sequenced tutorial beat explicitly:
///   1. Welcome barks → switch prompt → switch arrow.
///   2. First suspect arrives → paperwork spawns → inspection beats begin.
///   3. ID card pickup → put-down tutorial → app form pickup → discrepancy lesson.
///   4. Drawer tutorial → folder grab → place on desk → insert documents.
///   5. Both documents filed → stamp green → stamp folder.
///
/// All automatic triggers that would normally fire on Day 1 are suppressed in their
/// respective systems and driven from this class instead.
///
/// All tutorial coroutines run server-only. Megaphone barks are broadcast to all
/// clients via MegaphoneDialogueManager.ShowDialogueSynced. Object-pickup gates use
/// the synced IsHeld NetworkVariable so either player's action advances the tutorial.
/// </summary>
public class Day_01 : DayBase
{
    [Header("Day 1 Tutorial")]
    [Tooltip("The booth drawer the player must open to retrieve the folder.")]
    [SerializeField] private Drawer _drawer;

    [Tooltip("The shift switch button.")]
    [SerializeField] private SwitchButton _switchButton;

    [Tooltip("Tutorial arrow above the switch button. Starts inactive.")]
    [SerializeField] private GameObject _switchArrow;

    [Tooltip("Tutorial arrow above the drawer. Starts inactive.")]
    [SerializeField] private GameObject _drawerArrow;

    /// The live FolderController instance spawned at runtime by StackOfFolders.
    /// Assigned in OnFolderPickedUp when the player picks up the folder from the drawer.
    private FolderController _folder;

    [Tooltip("The green ink stamp station — made interactable once both documents are filed.")]
    [SerializeField] private InkStamp _greenStampSlot;

    [Tooltip("The yellow ink stamp station — blocked for the entire Day 1 tutorial.")]
    [SerializeField] private InkStamp _yellowStampSlot;

    [Tooltip("The red ink stamp station — blocked for the entire Day 1 tutorial.")]
    [SerializeField] private InkStamp _redStampSlot;

    [Tooltip("The window hand-off point where the player places the stamped folder to finalize the verdict.")]
    [SerializeField] private HandOffPoint _handOffPoint;

    [Header("Day 1 Tutorial — Part 2")]
    [Tooltip("The Documentation Exam Notebook scene object — kept non-interactable until the notebook beat.")]
    [SerializeField] private ExamNotebook _examNotebook;

    [Header("Day 1 Tutorial — Tool Locker Refill")]
    [Tooltip("Transform used as the marker target for the tool locker tutorial arrow. Assign the tool locker's own Transform or its Look Target child.")]
    [SerializeField] private Transform _toolLockerTarget;

    [Tooltip("The quarantine ink refill ShopItem — its price is temporarily set to 0 during the tutorial.")]
    [SerializeField] private ShopItem _quarantineRefillItem;

    [Tooltip("The kill ink refill ShopItem — its price is temporarily set to 0 during the tutorial.")]
    [SerializeField] private ShopItem _killRefillItem;

    [Header("Day 1 Suspects — Guards")]
    [Tooltip("Suspect pool to draw from for the first three suspects on Day 1. Assign the 'Guards' SuspectSet asset.")]
    [SerializeField] private SuspectSet _guardSuspectsSet;

    [Tooltip("Number of guard suspects to place at the front of the Day 1 shift queue.")]
    [SerializeField] private int _guardCount = 3;

    [Tooltip("The full suspect pool used after the tool locker tutorial ends. Assign the 'All Suspects' SuspectSet asset.")]
    [SerializeField] private SuspectSet _allSuspectsSet;

    [Tooltip("Number of additional suspects drawn from the all-suspects pool after the tool locker tutorial.")]
    [SerializeField] private int _postTutorialSuspectCount = 3;

    [Header("Other Day Notebooks — Hidden During Day 1")]
    [Tooltip("The Mutation Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    [Header("Day 1 — Phone Task Delivery")]
    [Tooltip("Index into Telephone._availableTasks that maps to the 'Go Hunting' PhoneTaskData.")]
    [SerializeField] private int _huntingTaskCallIndex = 1;

    [Tooltip("Seconds after the trash task call before the hunting task call is triggered.")]
    [SerializeField] private float _huntingCallDelaySeconds = 60f;

    [Header("Day 1 — Soldier Event (6th Suspect)")]
    [Tooltip("The SoldierMockingController placed in the scene — handles the scripted soldier arrival on the 6th suspect slot.")]
    [SerializeField] private SoldierMockingController _soldierMockingController;

    [Tooltip("When true, the soldier scripted event intercepts the 6th suspect slot on Day 1. " +
             "When false, the 6th slot draws from the all-suspects pool like any other post-tutorial suspect.")]
    [SerializeField] private bool _enableSoldierEvent = false;

    // Cached document references received via OnPaperworkSpawned.
    private IDCard _tutorialIDCard;
    private PickableObject _tutorialAppForm;

    // Running count of documents successfully filed into the tutorial folder.
    private int _documentsFiledCount;

    // Tracks which specific document objects have already been counted, preventing
    // the double-count that occurs in host+client games: when a non-host player files
    // a document, OnDocumentAdded fires once from SyncDocumentAddedServerRpc on the
    // server and again from SyncDocumentAddedClientRpc arriving on the host-client,
    // both in the same process.
    private readonly HashSet<PickableObject> _filedDocuments = new HashSet<PickableObject>();

    // Cached delegate so subscribe/unsubscribe reference equality is preserved.
    private System.Action<PickableObject> _onFolderDocumentFiled;
    private System.Action<IDCard, PickableObject> _onPaperworkSpawned;
    private System.Action<IDCard, PickableObject> _onSuspect2PaperworkSpawned;

    // Guards the phone-ring tutorial so it fires exactly once per day activation.
    private bool _phoneRingTutorialShown;

    // -------------------------------------------------------------------------
    // Suspect 2 state
    // -------------------------------------------------------------------------
    private IDCard _suspect2IDCard;
    private PickableObject _suspect2AppForm;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated(); // Calls AnomalyManager.ApplyUnlocksFromSave()

        // Persist documentation-only unlock for Day 1 and apply to live category locks.
        AnomalyManager.Instance.UnlockDocumentationOnly();

        // Door lock is deferred to OnDayStarted (ShiftManager.OnDayStart) so the sound
        // plays after the intro cutscene ends, not while it is playing.

        bool tutorialDone = SaveDataManager.Instance != null && SaveDataManager.Instance.Day1TutorialComplete;

        if (tutorialDone)
        {
            // Tutorial already completed on a previous run — open everything immediately,
            // no tutorial gating needed.
            if (_drawer != null) _drawer.SetLocked(false);
            _greenStampSlot?.SetSlotInteractable(true);
            _yellowStampSlot?.SetSlotInteractable(true);
            _redStampSlot?.SetSlotInteractable(true);
            _examNotebook?.SetInteractableNetworked(true);
        }
        else
        {
            // First-time tutorial path: gate the drawer and all stamp stations.
            if (_drawer != null)
                _drawer.SetLocked(true);

            // Guarantee the first suspect has no anomalies — the tutorial intro must be clean.
            // Server-only: these static flags are consumed exclusively by the server's spawn logic.
            if (NetworkManager.Singleton.IsServer)
            {
                SuspectController.ForceNextSuspectClean          = true;
                ShiftManager.OverrideFirstArrivalInterval        = new UnityEngine.Vector2(0f, 0f);
                ShiftManager.OverrideSuspectArrivalInterval      = new UnityEngine.Vector2(3f, 3f);
            }

            // All stamp stations are locked until the tutorial reaches the stamping beat.
            _greenStampSlot?.SetSlotInteractable(false);
            _yellowStampSlot?.SetSlotInteractable(false);
            _redStampSlot?.SetSlotInteractable(false);

            // Notebook stays non-interactable until the anomaly reveal beat.
            _examNotebook?.SetInteractableNetworked(false);
        }

        // All tutorial arrows start hidden.
        if (_switchArrow != null) _switchArrow.SetActive(false);
        if (_drawerArrow != null) _drawerArrow.SetActive(false);

        // Hide the mutation and biological notebooks — they are not introduced until Day 2 and Day 3.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);

        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        ShiftManager.Instance.OnDayStart        += OnDayStarted;
        Debug.Log($"[Day_01] DayActivated: subscribed to ShiftManager.OnDayStart. IsServer={NetworkManager.Singleton?.IsServer}, IsHost={NetworkManager.Singleton?.IsHost}.");
        SuspectController.OnSuspectArrived       += OnSuspectArrivedHandler;
        SwitchButton.OnPressed                   += OnSwitchPressed;
        _onFolderDocumentFiled = OnFolderDocumentFiled;
        FolderController.OnDocumentAdded         += _onFolderDocumentFiled;

        // Paperwork-spawn tutorial beats only run on the first playthrough.
        if (!tutorialDone)
        {
            _onPaperworkSpawned = OnPaperworkSpawnedHandler;
            SuspectController.OnPaperworkSpawned += _onPaperworkSpawned;
        }

        // Override the default random population so the first suspects are always guards.
        if (DailySuspectManager.Instance != null)
            DailySuspectManager.Instance.PopulateSuspectOverride = PopulateDay1Suspects;

        // Soldier scripted-event callback — always subscribed regardless of tutorial state.
        SoldierMockingController.OnSoldierSequenceComplete += OnSoldierSequenceCompleteHandler;

        // Phone-ring tutorial: show black bars on the first ring of Day 1.
        _phoneRingTutorialShown = false;
        Telephone.OnRingStarted += OnPhoneRingStarted;

        // OnFolderHandedOff is subscribed inside HandOffBeat / Suspect2HandOffBeat
        // so the window is scoped exactly to when each beat needs it.
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        // Restore mutation and biological notebooks so Day 2 / Day 3 can manage them normally.
        _mutationNotebook?.SetVisible(true);
        _biologicalNotebook?.SetVisible(true);

        // Clear any tutorial markers that may still be active if the day ended mid-tutorial.
        TutorialMarkerManager.Instance?.UnmarkAll();

        // Clean up tool locker tutorial state in case the day ended mid-beat.
        CleanupToolLockerTutorial();

        // Release the Day 1 suspect population override so subsequent days use the default logic.
        if (DailySuspectManager.Instance != null)
            DailySuspectManager.Instance.PopulateSuspectOverride = null;

        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnDayStart   -= OnDayStarted;
        }

        SuspectController.OnSuspectArrived      -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned    -= _onPaperworkSpawned;
        SwitchButton.OnPressed                  -= OnSwitchPressed;
        FolderController.OnDocumentAdded        -= _onFolderDocumentFiled;
        FolderController.OnFolderEquipped       -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff      -= OnFolderHandedOffHandler;
        FolderController.OnAnyFolderStamped     -= OnFolderStamped;

        SoldierMockingController.OnSoldierSequenceComplete -= OnSoldierSequenceCompleteHandler;

        Telephone.OnRingStarted -= OnPhoneRingStarted;

        if (_onSuspect2PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned -= _onSuspect2PaperworkSpawned;

        if (_onSuspect3PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned -= _onSuspect3PaperworkSpawned;

        ExamNotebook.OnAnyNotebookPageFiled     -= OnNotebookPageFiled;

        if (_drawer != null)
            _drawer.OnOpened -= OnDrawerFirstOpened;

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.OnEquip   -= OnIDCardPickedUp;
            _tutorialIDCard.OnUnEquip -= OnIDCardPutDown;
        }

        if (_tutorialAppForm != null)
            _tutorialAppForm.OnEquip -= OnAppFormPickedUp;

        if (_suspect2IDCard != null)
            _suspect2IDCard.OnEquip -= OnSuspect2IDCardPickedUp;

        if (_suspect2AppForm != null)
            _suspect2AppForm.OnEquip -= OnSuspect2AppFormPickedUp;

        if (_suspect3IDCard != null)
            _suspect3IDCard.OnEquip -= OnSuspect3IDCardPickedUp;

        if (_suspect3AppForm != null)
            _suspect3AppForm.OnEquip -= OnSuspect3AppFormPickedUp;

        StopAllCoroutines();
    }

    /// <summary>
    /// Safety net: unsubscribes from all static/instance events if the object is
    /// destroyed while a tutorial coroutine is still running (e.g. scene teardown
    /// mid-shift), preventing MissingReferenceException callbacks.
    /// </summary>
    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnDayStart   -= OnDayStarted;
        }

        // Clear the population override in case OnDestroy fires before DayDeactivated.
        if (DailySuspectManager.Instance != null)
            DailySuspectManager.Instance.PopulateSuspectOverride = null;

        SuspectController.OnSuspectArrived   -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned -= _onPaperworkSpawned;
        SwitchButton.OnPressed               -= OnSwitchPressed;

        if (_onFolderDocumentFiled != null)
            FolderController.OnDocumentAdded      -= _onFolderDocumentFiled;

        FolderController.OnFolderEquipped         -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff        -= OnFolderHandedOffHandler;
        FolderController.OnAnyFolderStamped       -= OnFolderStamped;

        SoldierMockingController.OnSoldierSequenceComplete -= OnSoldierSequenceCompleteHandler;

        Telephone.OnRingStarted -= OnPhoneRingStarted;

        if (_onSuspect2PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned  -= _onSuspect2PaperworkSpawned;

        if (_onSuspect3PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned  -= _onSuspect3PaperworkSpawned;

        ExamNotebook.OnAnyNotebookPageFiled       -= OnNotebookPageFiled;

        if (_drawer != null)
            _drawer.OnOpened -= OnDrawerFirstOpened;

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.OnEquip   -= OnIDCardPickedUp;
            _tutorialIDCard.OnUnEquip -= OnIDCardPutDown;
        }

        if (_tutorialAppForm != null)
            _tutorialAppForm.OnEquip -= OnAppFormPickedUp;

        if (_suspect2IDCard != null)
            _suspect2IDCard.OnEquip -= OnSuspect2IDCardPickedUp;

        if (_suspect2AppForm != null)
            _suspect2AppForm.OnEquip -= OnSuspect2AppFormPickedUp;

        if (_suspect3IDCard != null)
            _suspect3IDCard.OnEquip -= OnSuspect3IDCardPickedUp;

        if (_suspect3AppForm != null)
            _suspect3AppForm.OnEquip -= OnSuspect3AppFormPickedUp;

        CleanupToolLockerTutorial();
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Switch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Guards against OnDayStarted firing more than once (e.g. if ShiftManager.OnDayStart
    /// is broadcast twice due to EndIntroCutscene being called by both host and client UI).
    /// A second invocation would spawn a second Day1TutorialSequence coroutine that replays
    /// every bark and re-shows the switch arrow after it has already been hidden.
    /// </summary>
    private bool _tutorialSequenceStarted = false;

    private void OnDayStarted()
    {
        if (this == null) return;

        // Lock the exit door now that the intro cutscene has finished and the day is live.
        ShiftManager.Instance.OnDoorLock?.Invoke();

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning($"[Day_01] OnDayStarted: not server (IsServer={NetworkManager.Singleton.IsServer}, IsHost={NetworkManager.Singleton.IsHost}, IsClient={NetworkManager.Singleton.IsClient}) — tutorial skipped on this machine.");
            return;
        }
        if (_tutorialSequenceStarted)
        {
            Debug.LogWarning("[Day_01] OnDayStarted: tutorial sequence already started — ignoring duplicate call.");
            return;
        }
        _tutorialSequenceStarted = true;

        if (SaveDataManager.Instance != null && SaveDataManager.Instance.Day1TutorialComplete)
        {
            Debug.Log("[Day_01] OnDayStarted: tutorial complete — running Day1FreeshiftSequence.");
            StartCoroutine(Day1FreeshiftSequence());
        }
        else
        {
            Debug.Log("[Day_01] OnDayStarted: starting Day1TutorialSequence on server.");
            StartCoroutine(Day1TutorialSequence());
        }
    }

    private IEnumerator Day1TutorialSequence()
    {
        yield return new WaitForSeconds(7f);

        yield return ShowAndWait("Good morning. We've been expecting you.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("You'll be screening subjects. Press the button when ready.");

        if (NetworkManager.Singleton.IsServer)
            _switchButton.SetReady(true);

        SetSwitchArrow(true);
    }

    /// <summary>
    /// Runs on the server in place of <see cref="Day1TutorialSequence"/> when
    /// <see cref="SaveDataManager.Day1TutorialComplete"/> is true (i.e. the player has already
    /// finished Day 1 and is replaying after a game-over). Skips all tutorial gating: opens
    /// stamps/drawer immediately, delivers a short bark, then enables the switch and lets the
    /// shift run freely. The soldier scripted event fires on the 6th suspect only when
    /// <see cref="_enableSoldierEvent"/> is true.
    /// </summary>
    private IEnumerator Day1FreeshiftSequence()
    {
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("Back again. You know what to do.");

        // Enable the switch so the player can start the shift.
        _switchButton?.SetReady(true);
        SetSwitchArrow(true);

        // Trigger the hunting task call. The trash reminder is now driven automatically
        // by TrashThreat when the bag count crosses the configured threshold.
        if (Telephone.Instance != null)
            StartCoroutine(TriggerHuntingCallAfterDelay());
        else
            Debug.LogWarning("[Day_01] Day1FreeshiftSequence: Telephone.Instance is null — hunting task call skipped.");

        // Unlock the exit door — no tutorial gating on retry runs.
        ShiftManager.Instance.OnDoorUnlock?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Paperwork Arrives
    // -------------------------------------------------------------------------

    private void OnSuspectArrivedHandler(int suspectIndex)
    {
        // Arm the soldier event intercept after the 5th regular suspect (index 4) arrives,
        // but only when _enableSoldierEvent is true. When disabled, the 6th slot draws from
        // the all-suspects pool like any other post-tutorial suspect.
        if (suspectIndex != 4) return;

        // Unsubscribe — we only need to arm once.
        SuspectController.OnSuspectArrived -= OnSuspectArrivedHandler;

        if (!NetworkManager.Singleton.IsServer) return;
        if (!_enableSoldierEvent)
        {
            Debug.Log("[Day_01] Soldier event disabled (_enableSoldierEvent = false) — 6th suspect will draw from all-suspects pool.");
            return;
        }

        SuspectController.InterceptNextSuspectSpawn = () => _soldierMockingController?.BeginSequence();
        Debug.Log("[Day_01] Soldier mocking intercept armed — next suspect spawn slot will trigger the soldier event.");
    }

    /// <summary>
    /// Fires on the server once the ID card has been network-spawned and set up.
    /// Locks both documents, attaches tutorial arrows to each, and begins the ID card beat.
    /// </summary>
    private void OnPaperworkSpawnedHandler(IDCard idCard, PickableObject appForm)
    {
        if (this == null) return;
        if (!NetworkManager.Singleton.IsServer) return;
        SuspectController.OnPaperworkSpawned -= _onPaperworkSpawned;
        _onPaperworkSpawned = null;

        _tutorialIDCard = idCard;
        _tutorialAppForm = appForm;

        var docs = new System.Collections.Generic.List<PickableObject>();
        if (_tutorialIDCard  != null) docs.Add(_tutorialIDCard);
        if (_tutorialAppForm != null) docs.Add(_tutorialAppForm);
        foreach (var doc in docs)
            doc.SetInteractableNetworked(false);

        StartCoroutine(IDCardInspectionBeat());
    }

    /// <summary>
    /// Instantiates the document arrow prefab in world space 0.2 m above <paramref name="target"/>
    /// using TutorialMarker for per-frame tracking.
    /// </summary>

    // -------------------------------------------------------------------------
    // Tutorial Sequence — ID Card Pickup
    // -------------------------------------------------------------------------

    private IEnumerator IDCardInspectionBeat()
    {
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("A suspect has arrived.");

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.SetInteractableNetworked(true);
            ShowNetworkedMarker(_tutorialIDCard.GetComponent<NetworkObject>());
        }

        // Wait until any player picks up the ID card (IsHeld is a synced NetworkVariable).
        yield return new WaitUntil(() => _tutorialIDCard == null || _tutorialIDCard.IsHeld);

        if (_tutorialIDCard != null)
            HideNetworkedMarker(_tutorialIDCard.GetComponent<NetworkObject>());

        StartCoroutine(PutDownTutorialBeat());
    }

    // Kept for DayDeactivated/OnDestroy unsubscription safety — no longer used as a gate.
    private void OnIDCardPickedUp() { }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Inspect & Put Down
    // -------------------------------------------------------------------------

    private IEnumerator PutDownTutorialBeat()
    {
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Hold to inspect. Right-click to put down.");

        // IsHeld is driven by _holdingClientId NetworkVariable — synced on all clients.
        if (_tutorialIDCard != null)
            yield return new WaitUntil(() => !_tutorialIDCard.IsHeld);

        StartCoroutine(AppFormInspectionBeat());
    }

    // Stub kept for DayDeactivated/OnDestroy unsubscription safety.
    private void OnIDCardPutDown() { }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Application Form Pickup
    // -------------------------------------------------------------------------

    private IEnumerator AppFormInspectionBeat()
    {
        yield return new WaitForSeconds(0.5f);

        if (_tutorialAppForm != null)
        {
            _tutorialAppForm.SetInteractableNetworked(true);
            ShowNetworkedMarker(_tutorialAppForm.GetComponent<NetworkObject>());
        }

        // Wait until any player picks up the application form.
        yield return new WaitUntil(() => _tutorialAppForm == null || _tutorialAppForm.IsHeld);

        if (_tutorialAppForm != null)
            HideNetworkedMarker(_tutorialAppForm.GetComponent<NetworkObject>());

        StartCoroutine(DiscrepancyBeat());
    }

    // Kept for DayDeactivated/OnDestroy unsubscription safety — no longer used as a gate.
    private void OnAppFormPickedUp() { }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Discrepancy Lesson
    // -------------------------------------------------------------------------

    private IEnumerator DiscrepancyBeat()
    {
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Cross-reference the documents.");

        StartCoroutine(DrawerTutorialBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Drawer
    // -------------------------------------------------------------------------

    private IEnumerator DrawerTutorialBeat()
    {
        yield return ShowAndWait("Grab the subject's folder from the drawer.");

        if (_drawer != null)
            _drawer.SetLocked(false);

        SetDrawerArrow(true);
        if (_drawer != null)
            _drawer.OnOpened += OnDrawerFirstOpened;

        FolderController.OnFolderEquipped += OnFolderPickedUp;
    }

    private void OnFolderPickedUp(FolderController folder)
    {
        if (this == null) return;
        _folder = folder;
        Debug.Log($"[Day_01] OnFolderPickedUp: captured folder '{folder.name}' NetworkObjectId={folder.NetworkObjectId}");
        FolderController.OnFolderEquipped -= OnFolderPickedUp;
        StartCoroutine(FolderPlaceBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Place Folder on Desk
    // -------------------------------------------------------------------------

    private IEnumerator FolderPlaceBeat()
    {
        // Wait until the folder is placed on any surface that is NOT the HandOffPoint (window slot).
        // If the player drops it straight on the window slot, the folder would skip the desk-placement
        // step and land in the wrong position for document filing.
        yield return new WaitUntil(() =>
            _folder == null ||
            (!_folder.IsHeld && !_folder.IsHandedOff));

        StartCoroutine(DocumentInsertBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Insert Documents into Folder
    // -------------------------------------------------------------------------

    private IEnumerator DocumentInsertBeat()
    {
        yield return new WaitForSeconds(1f);

        // Snapshot documents already filed before resetting — the player may have filed
        // one or both documents while the folder-place dialogue was still playing.
        int alreadyFiled = _documentsFiledCount;
        _documentsFiledCount = alreadyFiled;

        // First document — skip prompt if already in the folder.
        if (_documentsFiledCount < 1)
        {
            yield return ShowAndWait("File the documents into the folder.");
            yield return new WaitUntil(() => _documentsFiledCount >= 1);
        }

        yield return new WaitForSeconds(0.5f);

        // Second document — wait silently; player already knows the gesture.
        if (_documentsFiledCount < 2)
        {
            yield return new WaitUntil(() => _documentsFiledCount >= 2);
        }

        FolderController.OnDocumentAdded -= _onFolderDocumentFiled;
        StartCoroutine(StampBeat());
    }

    private void OnFolderDocumentFiled(PickableObject document)
    {
        if (this == null) return;

        // Guard against double-counting: in a host+client game, when a non-host player
        // files a document, OnDocumentAdded fires both from SyncDocumentAddedServerRpc
        // (server side) and from SyncDocumentAddedClientRpc received on the host-client —
        // both in the same process. The HashSet ensures each physical document is counted once.
        if (document != null && !_filedDocuments.Add(document)) return;

        _documentsFiledCount++;

        // Permanently lock the document on all clients so the holder network-variable
        // release callback cannot re-enable its colliders after it lands in the folder slot.
        if (document != null)
            document.LockInteractableNetworked();
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Stamp
    // -------------------------------------------------------------------------

    private IEnumerator StampBeat()
    {
        yield return new WaitForSeconds(1.5f);

        yield return ShowAndWait("Stamp the folder green.");

        // Arm the hand-off listener now — before the stamp event and before any
        // dialogue, so a fast player who stamps and hands off immediately is captured.
        _folderHandedOff = false;
        FolderController.OnFolderHandedOff += OnFolderHandedOffHandler;

        // Reset before subscribing so a stale value from a previous run can't skip the wait.
        _folderStamped = false;

        // Subscribe to the static event now — any folder stamped from this point counts.
        FolderController.OnAnyFolderStamped += OnFolderStamped;

        // Unlock only the green stamp station.
        _greenStampSlot?.SetSlotInteractable(true);

        // Show a synced tracking arrow above the green stamp slot on all clients.
        ShowStaticMarker(StaticMarkerTarget.GreenStamp);

        // Hide the arrow the moment the stamp leaves its slot — fire-and-forget.
        StartCoroutine(HideStampArrowOnPickup(_greenStampSlot, StaticMarkerTarget.GreenStamp));

        // HandOffBeat waits for the static event flag set by OnFolderStamped.
        StartCoroutine(HandOffBeat());
    }

    /// <summary>
    /// Hides the stamp arrow as soon as the player lifts the stamp out of its slot.
    /// Runs independently of HandOffBeat so neither blocks the other.
    /// </summary>
    private IEnumerator HideStampArrowOnPickup(InkStamp slot, StaticMarkerTarget target)
    {
        yield return new WaitUntil(() => slot == null || !slot.IsStampInSlot);
        HideStaticMarker(target);
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Hand Off Folder at Window
    // -------------------------------------------------------------------------

    /// <summary>
    /// A persistent flag set the moment <see cref="FolderController.OnStamped"/> fires.
    /// Ensures the WaitUntil in HandOffBeat resolves even if the event fires before the
    /// coroutine polls it for the first time.
    /// </summary>
    private bool _folderStamped;

    private void OnFolderStamped()
    {
        if (this == null) return;
        _folderStamped = true;
        FolderController.OnAnyFolderStamped -= OnFolderStamped;
        Debug.Log("[Day_01] OnFolderStamped: stamp event received — advancing HandOffBeat.");
    }

    /// <summary>
    /// A persistent flag set as soon as the static event fires.
    /// Ensures the WaitUntil resolves even if the event fires before the coroutine polls it.
    /// </summary>
    private bool _folderHandedOff;

    private void OnFolderHandedOffHandler()
    {
        if (this == null) return;
        Debug.Log("[Day_01] OnFolderHandedOffHandler: folder handed off event received.");
        _folderHandedOff = true;
        FolderController.OnFolderHandedOff -= OnFolderHandedOffHandler;
    }

    private IEnumerator HandOffBeat()
    {
        // Wait until OnStamped fires and sets the flag — no polling of a NetworkVariable.
        Debug.Log("[Day_01] HandOffBeat: waiting for folder stamp via event flag.");
        yield return new WaitUntil(() => _folderStamped);

        Debug.Log("[Day_01] HandOffBeat: folder stamped — guiding player to return green stamp.");

        // Brief pause so the stamp animation settles before the return arrow appears.
        yield return new WaitForSeconds(2f);

        // Show the return arrow only once the folder is stamped AND the player is
        // still holding the stamp (not in slot). This prevents a race where _folderStamped
        // resolves before the player has even picked up the stamp.
        yield return new WaitUntil(() => _greenStampSlot == null || !_greenStampSlot.IsStampInSlot);
        ShowStaticMarker(StaticMarkerTarget.GreenStamp);
        yield return new WaitUntil(() => _greenStampSlot == null || _greenStampSlot.IsStampInSlot);
        HideStaticMarker(StaticMarkerTarget.GreenStamp);

        // Permanently lock the green stamp slot and pickup for the rest of the tutorial.
        _greenStampSlot?.LockStampAndSlot();
        Debug.Log("[Day_01] HandOffBeat: green stamp returned and locked — proceeding to hand-off.");

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Place the folder in the window slot.");

        ShowStaticMarker(StaticMarkerTarget.HandOff);

        // Guard: player may have placed the folder during the dialogue above.
        if (!_folderHandedOff)
            yield return new WaitUntil(() => _folderHandedOff);

        HideStaticMarker(StaticMarkerTarget.HandOff);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Your decisions are being recorded.");

        // Subscribe for the second suspect's paperwork — the beat begins on actual arrival,
        // not on a fixed timer, so we don't race ahead of the suspect's walk-in.
        // Force exactly 2 documentation anomalies so the notebook tutorial always has material.
        if (NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 2;

        _onSuspect2PaperworkSpawned = OnSuspect2PaperworkSpawned;
        SuspectController.OnPaperworkSpawned += _onSuspect2PaperworkSpawned;
    }

    // =========================================================================
    // Tutorial Sequence — Suspect 2 (Notebook introduction)
    // =========================================================================

    private void OnSuspect2PaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (this == null) return;
        if (!NetworkManager.Singleton.IsServer) return;
        SuspectController.OnPaperworkSpawned -= _onSuspect2PaperworkSpawned;
        _onSuspect2PaperworkSpawned = null;

        _suspect2IDCard  = idCard;
        _suspect2AppForm = appForm;

        var docs = new System.Collections.Generic.List<PickableObject>();
        if (_suspect2IDCard  != null) docs.Add(_suspect2IDCard);
        if (_suspect2AppForm != null) docs.Add(_suspect2AppForm);
        foreach (var doc in docs)
            doc.SetInteractableNetworked(false);

        StartCoroutine(Suspect2IDCardBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Inspect both documents beat
    // -------------------------------------------------------------------------

    // Tracks how many of the two suspect 2 documents have been picked up at least once.
    private bool _s2IDCardInspected;
    private bool _s2AppFormInspected;

    // =========================================================================
    // Suspect 3 state
    // =========================================================================
    private IDCard _suspect3IDCard;
    private PickableObject _suspect3AppForm;
    private System.Action<IDCard, PickableObject> _onSuspect3PaperworkSpawned;

    private bool _s3IDCardInspected;
    private bool _s3AppFormInspected;

    // =========================================================================
    // Tool locker refill tutorial state
    // =========================================================================
    private bool _quarantineRefilled;
    private bool _killRefilled;
    private System.Action _onToolLockerOpenedDelegate;
    private System.Action _onShopOpenedForTutorialDelegate;
    private ShopItemView _quarantineRefillView;
    private ShopItemView _killRefillView;

    private IEnumerator Suspect2IDCardBeat()
    {
        yield return new WaitForSeconds(1f);

        // Unlock both documents. No bark — players already know how to pick up documents.
        _s2IDCardInspected  = false;
        _s2AppFormInspected = false;

        if (_suspect2IDCard != null)
            _suspect2IDCard.SetInteractableNetworked(true);

        if (_suspect2AppForm != null)
            _suspect2AppForm.SetInteractableNetworked(true);

        // Wait until any player has held each document at least once, in any order.
        // IsHeld is a synced NetworkVariable — it becomes true on all machines when any player picks it up.
        yield return WaitForDocumentInspected(_suspect2IDCard);
        _s2IDCardInspected = true;
        yield return WaitForDocumentInspected(_suspect2AppForm);
        _s2AppFormInspected = true;

        StartCoroutine(Suspect2AnomalyRevealBeat());
    }

    /// <summary>Waits until the given document is picked up and then put down at least once.</summary>
    private IEnumerator WaitForDocumentInspected(PickableObject doc)
    {
        if (doc == null) yield break;
        yield return new WaitUntil(() => doc.IsHeld);
        HideNetworkedMarker(doc.GetComponent<NetworkObject>());
        yield return new WaitUntil(() => !doc.IsHeld);
    }

    private void OnSuspect2IDCardPickedUp()
    {
        if (this == null) return;
        if (_suspect2IDCard != null) _suspect2IDCard.OnEquip -= OnSuspect2IDCardPickedUp;
        _s2IDCardInspected = true;
    }

    private void OnSuspect2AppFormPickedUp()
    {
        if (this == null) return;
        if (_suspect2AppForm != null) _suspect2AppForm.OnEquip -= OnSuspect2AppFormPickedUp;
        _s2AppFormInspected = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Anomaly reveal + notebook unlock
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2AnomalyRevealBeat()
    {
        // Reset early-action flags here — the earliest point before any of the
        // notebook beats run — so interaction during dialogue is always captured.
        ChecklistItem.AnyBoxChecked = false;
        ExamNotebook.AnyPageFiled = false;

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("There's a discrepancy in these documents. Something doesn't line up.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mark anomalies in the exam notebook.");

        _examNotebook?.SetInteractableNetworked(true);
        if (_examNotebook != null)
            ShowNetworkedMarker(_examNotebook.GetComponent<NetworkObject>());

        yield return new WaitUntil(() => _examNotebook != null && _examNotebook.IsHeld);

        if (_examNotebook != null)
            HideNetworkedMarker(_examNotebook.GetComponent<NetworkObject>());

        StartCoroutine(NotebookCheckBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Checkbox completion
    // -------------------------------------------------------------------------

    private IEnumerator NotebookCheckBeat()
    {
        // Subscribe before the guard check so any box ticked before or during is captured.
        bool anyBoxChecked = false;
        System.Action<ExamNotebook> onChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += onChecked;

        // Guard: player may have already ticked a box.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ExamNotebook.OnAnyCheckboxChecked -= onChecked;

        StartCoroutine(NotebookFileIntoBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — File notebook into folder
    // -------------------------------------------------------------------------

    private bool _notebookPageFiled;

    private IEnumerator NotebookFileIntoBeat()
    {
        // Subscribe before dialogue so filing that happens during the prompt is not missed.
        _notebookPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnNotebookPageFiled;

        yield return ShowAndWait("File your findings.");

        // Guard: player may have filed during dialogue — AnyPageFiled captures that.
        if (ExamNotebook.AnyPageFiled)
            _notebookPageFiled = true;

        yield return new WaitUntil(() => _notebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnNotebookPageFiled;

        StartCoroutine(Suspect2StampBeat());
    }

    private void OnNotebookPageFiled()
    {
        if (this == null) return;
        _notebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Stamp beat
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2StampBeat()
    {
        _folderStamped = false;

        yield return new WaitForSeconds(1.5f);

        yield return ShowAndWait("Quarantine this subject — stamp the folder yellow.");

        // Arm the hand-off listener before the stamp event so a fast player who stamps
        // and hands off in one motion doesn't race ahead of Suspect2HandOffBeat's subscribe.
        _folderHandedOff = false;
        FolderController.OnFolderHandedOff += OnFolderHandedOffHandler;

        FolderController.OnAnyFolderStamped += OnFolderStamped;

        // Only the yellow stamp is available; green and red remain locked for this beat.
        _yellowStampSlot?.SetSlotInteractable(true);

        ShowStaticMarker(StaticMarkerTarget.YellowStamp);

        StartCoroutine(HideStampArrowOnPickup(_yellowStampSlot, StaticMarkerTarget.YellowStamp));
        StartCoroutine(Suspect2HandOffBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Hand-off beat (mirrors suspect 1's HandOffBeat)
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2HandOffBeat()
    {
        // _folderHandedOff reset and OnFolderHandedOff subscription are done in
        // Suspect2StampBeat before arming the stamp, so no early hand-off is missed.
        yield return new WaitUntil(() => _folderStamped);

        // Wait for the player to return the yellow stamp — no arrow, already taught.
        yield return new WaitUntil(() => _yellowStampSlot == null || _yellowStampSlot.IsStampInSlot);
        _yellowStampSlot?.LockStampAndSlot();
        Debug.Log("[Day_01] Suspect2HandOffBeat: yellow stamp returned and locked.");

        yield return new WaitForSeconds(1f);

        ShowStaticMarker(StaticMarkerTarget.HandOff);

        // Guard: player may have placed the folder while stamp was being returned.
        if (!_folderHandedOff)
            yield return new WaitUntil(() => _folderHandedOff);

        HideStaticMarker(StaticMarkerTarget.HandOff);

        // Force suspect 3 to have exactly 5 documentation anomalies.
        if (NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 5;

        _onSuspect3PaperworkSpawned = OnSuspect3PaperworkSpawned;
        SuspectController.OnPaperworkSpawned += _onSuspect3PaperworkSpawned;
    }

    // =========================================================================
    // Tutorial Sequence — Suspect 3 (Elimination introduction) v2
    // =========================================================================

    private void OnSuspect3PaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (this == null) return;
        if (!NetworkManager.Singleton.IsServer) return;
        SuspectController.OnPaperworkSpawned -= _onSuspect3PaperworkSpawned;
        _onSuspect3PaperworkSpawned = null;

        _suspect3IDCard  = idCard;
        _suspect3AppForm = appForm;

        var docs = new System.Collections.Generic.List<PickableObject>();
        if (_suspect3IDCard  != null) docs.Add(_suspect3IDCard);
        if (_suspect3AppForm != null) docs.Add(_suspect3AppForm);
        foreach (var doc in docs)
            doc.SetInteractableNetworked(false);

        StartCoroutine(Suspect3IDCardBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — Inspect both documents beat
    // -------------------------------------------------------------------------

    private IEnumerator Suspect3IDCardBeat()
    {
        yield return new WaitForSeconds(1f);

        _s3IDCardInspected  = false;
        _s3AppFormInspected = false;

        if (_suspect3IDCard != null)
            _suspect3IDCard.SetInteractableNetworked(true);

        if (_suspect3AppForm != null)
            _suspect3AppForm.SetInteractableNetworked(true);

        yield return WaitForDocumentInspected(_suspect3IDCard);
        _s3IDCardInspected = true;
        yield return WaitForDocumentInspected(_suspect3AppForm);
        _s3AppFormInspected = true;

        StartCoroutine(Suspect3AnomalyRevealBeat());
    }

    private void OnSuspect3IDCardPickedUp()
    {
        if (this == null) return;
        if (_suspect3IDCard != null) _suspect3IDCard.OnEquip -= OnSuspect3IDCardPickedUp;
        _s3IDCardInspected = true;
    }

    private void OnSuspect3AppFormPickedUp()
    {
        if (this == null) return;
        if (_suspect3AppForm != null) _suspect3AppForm.OnEquip -= OnSuspect3AppFormPickedUp;
        _s3AppFormInspected = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — Anomaly reveal + notebook unlock
    // -------------------------------------------------------------------------

    private IEnumerator Suspect3AnomalyRevealBeat()
    {
        ChecklistItem.AnyBoxChecked = false;
        ExamNotebook.AnyPageFiled = false;

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Multiple anomalies. This one was never going to walk out of here.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Note every deviation. Leave nothing out.");

        _examNotebook?.SetInteractableNetworked(true);

        yield return new WaitUntil(() => _examNotebook != null && _examNotebook.IsHeld);

        StartCoroutine(Suspect3NotebookCheckBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — Checkbox completion
    // -------------------------------------------------------------------------

    private IEnumerator Suspect3NotebookCheckBeat()
    {
        bool anyBoxChecked = false;
        System.Action<ExamNotebook> s3OnChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += s3OnChecked;

        // Guard: player may have already ticked a box.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ExamNotebook.OnAnyCheckboxChecked -= s3OnChecked;

        StartCoroutine(Suspect3NotebookFileBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — File notebook into folder
    // -------------------------------------------------------------------------

    private bool _s3NotebookPageFiled;

    private IEnumerator Suspect3NotebookFileBeat()
    {
        _s3NotebookPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnSuspect3NotebookPageFiled;

        // Guard: player may have already filed.
        if (ExamNotebook.AnyPageFiled)
            _s3NotebookPageFiled = true;

        yield return new WaitUntil(() => _s3NotebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnSuspect3NotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("There's only one outcome for someone this compromised.");

        StartCoroutine(Suspect3StampBeat());
    }

    private void OnSuspect3NotebookPageFiled()
    {
        if (this == null) return;
        _s3NotebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — Stamp beat (red / Kill)
    // -------------------------------------------------------------------------

    private IEnumerator Suspect3StampBeat()
    {
        _folderStamped = false;

        yield return new WaitForSeconds(1.5f);

        yield return ShowAndWait("Red. It suits them perfectly.");

        // Arm the hand-off listener before the stamp event so a fast player who stamps
        // and hands off immediately doesn't race ahead of Suspect3HandOffBeat's subscribe.
        _folderHandedOff = false;
        FolderController.OnFolderHandedOff += OnFolderHandedOffHandler;

        FolderController.OnAnyFolderStamped += OnFolderStamped;

        // Only the red stamp is available for this beat.
        _redStampSlot?.SetSlotInteractable(true);

        ShowStaticMarker(StaticMarkerTarget.RedStamp);

        StartCoroutine(HideStampArrowOnPickup(_redStampSlot, StaticMarkerTarget.RedStamp));
        StartCoroutine(Suspect3HandOffBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 3 — Hand-off beat
    // -------------------------------------------------------------------------

    private IEnumerator Suspect3HandOffBeat()
    {
        // Append post-tutorial suspects and pause scheduling synchronously before any yield.
        // This ensures SetNextSuspectReady() — which fires when the folder is handed off —
        // sees a non-empty remaining queue (shiftSuspects.Count > 3) and queues the 4th
        // arrival instead of triggering the end-of-shift clock-out.
        if (NetworkManager.Singleton.IsServer)
        {
            AppendPostTutorialSuspects();
            ShiftManager.PauseSuspectScheduling = true;
        }

        // _folderHandedOff reset and OnFolderHandedOff subscription are done in
        // Suspect3StampBeat before arming the stamp, so no early hand-off is missed.
        yield return new WaitUntil(() => _folderStamped);

        // Wait for the player to return the red stamp — no arrow, already taught.
        yield return new WaitUntil(() => _redStampSlot == null || _redStampSlot.IsStampInSlot);
        _redStampSlot?.LockStampAndSlot();
        Debug.Log("[Day_01] Suspect3HandOffBeat: red stamp returned and locked.");

        yield return new WaitForSeconds(1f);

        ShowStaticMarker(StaticMarkerTarget.HandOff);

        if (!_folderHandedOff)
            yield return new WaitUntil(() => _folderHandedOff);

        HideStaticMarker(StaticMarkerTarget.HandOff);

        StartCoroutine(ToolLockerRefillTutorialBeat());
    }

    // =========================================================================
    // Tutorial Sequence — Tool Locker Refill (after kill tutorial)
    // =========================================================================

    /// <summary>
    /// Guides the player to the tool locker to refill their quarantine and kill stamps.
    /// Both refill items are set to free just for this beat. The 4th suspect is held until
    /// both refills have been purchased, then released via <see cref="ShiftManager.ResumeScheduledSuspect"/>.
    /// </summary>
    private IEnumerator ToolLockerRefillTutorialBeat()
    {
        // Broadcast the free-price override to all clients before the locker is opened so the
        // price is already 0 on every machine when ToolShopController initialises the views.
        if (_quarantineRefillItem != null)
            MegaphoneDialogueManager.Instance.SetShopItemPriceOverrideSynced(_quarantineRefillItem.Name, 0);
        if (_killRefillItem != null)
            MegaphoneDialogueManager.Instance.SetShopItemPriceOverrideSynced(_killRefillItem.Name, 0);

        // All stamps are now unlocked — the player can freely use them going forward.
        _greenStampSlot?.SetSlotInteractable(true);
        _yellowStampSlot?.SetSlotInteractable(true);
        _redStampSlot?.SetSlotInteractable(true);

        yield return new WaitForSeconds(6f);

        yield return ShowAndWait("Your stamps are running low. Head to the tool locker and restock.");

        ShowStaticMarker(StaticMarkerTarget.ToolLocker);

        // Wait until any player opens a tool locker (fires on all machines via NetworkVariable callback).
        bool lockerOpened = false;
        _onToolLockerOpenedDelegate = () => lockerOpened = true;
        ToolsLocker.OnAnyLockerOpened += _onToolLockerOpenedDelegate;

        yield return new WaitUntil(() => lockerOpened);

        ToolsLocker.OnAnyLockerOpened -= _onToolLockerOpenedDelegate;
        _onToolLockerOpenedDelegate = null;
        HideStaticMarker(StaticMarkerTarget.ToolLocker);

        // Subscribe to OnShopOpened NOW — before the bark — so we never miss the shop
        // UI becoming active, even if the player enters the shop during the dialogue.
        _quarantineRefilled = false;
        _killRefilled = false;
        StampInkManager.OnInkChanged += OnInkRefilled;

        _onShopOpenedForTutorialDelegate = OnToolLockerShopOpened;
        ToolShopController.OnShopOpened += _onShopOpenedForTutorialDelegate;

        yield return ShowAndWait("Pick up the refills — they're on us.");

        yield return new WaitUntil(() => _quarantineRefilled && _killRefilled);

        StampInkManager.OnInkChanged -= OnInkRefilled;

        // Restore prices on all clients now that both refills have been purchased.
        if (_quarantineRefillItem != null)
            MegaphoneDialogueManager.Instance.ClearShopItemPriceOverrideSynced(_quarantineRefillItem.Name);
        if (_killRefillItem != null)
            MegaphoneDialogueManager.Instance.ClearShopItemPriceOverrideSynced(_killRefillItem.Name);

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("You know what's expected. We'll be watching.");

        // Ring the telephone with the hunting task call — serves as the phone tutorial beat.
        // The trash reminder call is now triggered automatically by TrashThreat when
        // the bag count exceeds the configured threshold.
        // TriggerCall is server-only and this coroutine is already guarded to run on the server.
        if (Telephone.Instance != null)
            Telephone.Instance.TriggerCall(_huntingTaskCallIndex);
        else
            Debug.LogWarning("[Day_01] Telephone.Instance is null — hunting task call skipped.");

        // Unlock the exit door — the tutorial gating is over.
        ShiftManager.Instance.OnDoorUnlock?.Invoke();

        // Remove the tutorial interval override so subsequent suspects arrive at the
        // normal paced intervals configured in ShiftManager rather than the compressed
        // 3-second tutorial cadence.
        ShiftManager.OverrideSuspectArrivalInterval = null;

        // Release the queued 4th suspect — suspects from the all-suspects pool were
        // already appended at the start of Suspect3HandOffBeat, before the hand-off.
        if (NetworkManager.Singleton.IsServer)
            ShiftManager.Instance.ResumeScheduledSuspect();

        // Mark the tutorial as fully complete so subsequent Day 1 runs (after a game-over)
        // skip all tutorial gating and run the free-shift path instead.
        if (SaveDataManager.Instance != null)
        {
            SaveDataManager.Instance.Day1TutorialComplete = true;
            Debug.Log("[Day_01] Tutorial complete flag saved — future Day 1 runs will skip the tutorial.");
        }
    }

    /// <summary>
    /// Waits <see cref="_huntingCallDelaySeconds"/> seconds after the trash task call,
    /// then triggers the hunting task phone call. Server-only — called from
    /// <see cref="Day1FreeshiftSequence"/> and the main tutorial sequence.
    /// </summary>
    private IEnumerator TriggerHuntingCallAfterDelay()
    {
        yield return new WaitForSeconds(_huntingCallDelaySeconds);

        if (Telephone.Instance != null)
            Telephone.Instance.TriggerCall(_huntingTaskCallIndex);
        else
            Debug.LogWarning("[Day_01] TriggerHuntingCallAfterDelay: Telephone.Instance is null — hunting task call skipped.");
    }

    /// <summary>Tracks which stamp types have been refilled during the tool locker tutorial beat.</summary>
    private void OnInkRefilled(StampContainer.StampType type, int newCount)
    {
        if (this == null) return;

        if (type == StampContainer.StampType.Quarantine && !_quarantineRefilled)
        {
            _quarantineRefilled = true;
            _quarantineRefillView?.SetArrowVisible(false);
            _quarantineRefillView = null;

            // Restore the price as soon as this item is purchased — don't wait for the other refill.
            if (_quarantineRefillItem != null)
                MegaphoneDialogueManager.Instance.ClearShopItemPriceOverrideSynced(_quarantineRefillItem.Name);
        }

        if (type == StampContainer.StampType.Kill && !_killRefilled)
        {
            _killRefilled = true;
            _killRefillView?.SetArrowVisible(false);
            _killRefillView = null;

            // Restore the price as soon as this item is purchased — don't wait for the other refill.
            if (_killRefillItem != null)
                MegaphoneDialogueManager.Instance.ClearShopItemPriceOverrideSynced(_killRefillItem.Name);
        }

        // Both purchased — re-enable the back button and unlock the shop so the player can leave.
        if (_quarantineRefilled && _killRefilled)
        {
            ToolShopController.Instance?.SetBackButtonActive(true);
            ToolShopController.Instance?.UnlockAllItems();
        }
    }

    /// <summary>
    /// Called when the shop screen opens during the tool locker tutorial beat.
    /// Hides the back button and shows tutorial arrows on the two refill items.
    /// </summary>
    private void OnToolLockerShopOpened()
    {
        // Only act once per beat — unsubscribe immediately.
        ToolShopController.OnShopOpened -= _onShopOpenedForTutorialDelegate;
        _onShopOpenedForTutorialDelegate = null;

        var controller = ToolShopController.Instance;
        if (controller == null) return;

        controller.SetBackButtonActive(false);
        controller.SetItemsLockedExcept(_quarantineRefillItem, _killRefillItem);

        if (!_quarantineRefilled && _quarantineRefillItem != null)
        {
            _quarantineRefillView = controller.GetViewForItem(_quarantineRefillItem);
            _quarantineRefillView?.SetArrowVisible(true);
        }

        if (!_killRefilled && _killRefillItem != null)
        {
            _killRefillView = controller.GetViewForItem(_killRefillItem);
            _killRefillView?.SetArrowVisible(true);
        }
    }

    /// <summary>
    /// Unsubscribes all tool locker tutorial events, restores item prices, hides any active
    /// marker, and clears the suspect scheduling pause. Safe to call from <see cref="DayDeactivated"/>
    /// and <see cref="OnDestroy"/>.
    /// </summary>
    private void CleanupToolLockerTutorial()
    {
        StampInkManager.OnInkChanged -= OnInkRefilled;

        if (_onToolLockerOpenedDelegate != null)
        {
            ToolsLocker.OnAnyLockerOpened -= _onToolLockerOpenedDelegate;
            _onToolLockerOpenedDelegate = null;
        }

        if (_onShopOpenedForTutorialDelegate != null)
        {
            ToolShopController.OnShopOpened -= _onShopOpenedForTutorialDelegate;
            _onShopOpenedForTutorialDelegate = null;
        }

        // Hide any tutorial arrows that may still be active.
        _quarantineRefillView?.SetArrowVisible(false);
        _quarantineRefillView = null;
        _killRefillView?.SetArrowVisible(false);
        _killRefillView = null;

        // Restore the back button and unlock all shop items in case the day ends mid-tutorial.
        ToolShopController.Instance?.SetBackButtonActive(true);
        ToolShopController.Instance?.UnlockAllItems();

        // Dismiss the marker if it is still visible (e.g. day ended mid-beat).
        HideStaticMarker(StaticMarkerTarget.ToolLocker);

        _quarantineRefillItem?.ClearPriceOverride();
        _killRefillItem?.ClearPriceOverride();

        // Ensure the pause flag is never left set if the day ends mid-tutorial.
        ShiftManager.PauseSuspectScheduling = false;
    }

    /// <summary>
    /// Populates the shift suspect list for Day 1: up to <see cref="_guardCount"/> randomly
    /// selected guards from <see cref="_guardSuspectsSet"/>, with no duplicates.
    /// Assigned to <see cref="DailySuspectManager.PopulateSuspectOverride"/> so it replaces
    /// the default random population for this day only.
    /// Must only be called on the server (via <see cref="DailySuspectManager.PopulateShiftCharacters"/>).
    /// </summary>
    private void PopulateDay1Suspects()
    {
        if (_guardSuspectsSet == null || _guardSuspectsSet.suspects == null || _guardSuspectsSet.suspects.Count == 0)
        {
            Debug.LogWarning("[Day_01] PopulateDay1Suspects: _guardSuspectsSet is not assigned or empty — shift will have no suspects.");
            return;
        }

        var queue = DailySuspectManager.Instance.shiftSuspects;
        var pool = new List<SuspectData>(_guardSuspectsSet.suspects);

        int count = Mathf.Min(_guardCount, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            queue.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        Debug.Log($"[Day_01] Populated {count} guard suspect(s) for the start of the Day 1 shift.");

        // On tutorial-skip (retry) runs, Suspect3HandOffBeat never fires so
        // AppendPostTutorialSuspects() is never called from there. Append them here instead
        // so the shift still has all 6 suspects, including the 6th for the Alexei event.
        if (SaveDataManager.Instance != null && SaveDataManager.Instance.Day1TutorialComplete)
            AppendPostTutorialSuspects();
    }

    /// <summary>
    /// Appends <see cref="_postTutorialSuspectCount"/> randomly selected suspects from
    /// <see cref="_allSuspectsSet"/> to the live shift queue so the shift continues past
    /// the Day 1 tutorial section. Suspects already in the queue are excluded to avoid
    /// immediate duplicates. Must only be called on the server.
    /// </summary>
    private void AppendPostTutorialSuspects()
    {
        if (_allSuspectsSet == null || _allSuspectsSet.suspects == null || _allSuspectsSet.suspects.Count == 0)
        {
            Debug.LogWarning("[Day_01] AppendPostTutorialSuspects: _allSuspectsSet is not assigned or empty — skipping.");
            return;
        }

        var queue = DailySuspectManager.Instance.shiftSuspects;

        // Build a pool that excludes suspects already queued to avoid immediate repeats.
        var existingSet = new HashSet<SuspectData>(queue);
        var pool = new List<SuspectData>();
        foreach (SuspectData s in _allSuspectsSet.suspects)
        {
            if (!existingSet.Contains(s))
                pool.Add(s);
        }

        // Fall back to the full set if there aren't enough unique suspects available.
        if (pool.Count == 0)
            pool = new List<SuspectData>(_allSuspectsSet.suspects);

        int count = Mathf.Min(_postTutorialSuspectCount, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            queue.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        Debug.Log($"[Day_01] Appended {count} post-tutorial suspect(s) from '{_allSuspectsSet.name}'. Shift queue now has {queue.Count} suspect(s).");
    }

    // -------------------------------------------------------------------------
    // Tutorial Arrow Dismissal
    // -------------------------------------------------------------------------

    private void OnSwitchPressed()
    {
        if (this == null) return;
        SwitchButton.OnPressed -= OnSwitchPressed;

        SetSwitchArrow(false);

        // Day 1 hasn't introduced the lever yet — open the window automatically
        // so the suspect can deliver paperwork once they arrive.
        if (NetworkManager.Singleton.IsServer)
            ShiftManager.Instance.OpenBoothShutter();
    }

    private void OnDrawerFirstOpened()
    {
        if (this == null) return;
        _drawer.OnOpened -= OnDrawerFirstOpened;
        SetDrawerArrow(false);
    }

    // -------------------------------------------------------------------------
    // Networked Marker RPCs
    // -------------------------------------------------------------------------

    /// <summary>Identifies scene-static interactables that need a synced tutorial marker.</summary>
    private enum StaticMarkerTarget { GreenStamp, YellowStamp, RedStamp, HandOff, ToolLocker }

    private Transform GetStaticMarkerTransform(StaticMarkerTarget target) => target switch
    {
        StaticMarkerTarget.GreenStamp  => _greenStampSlot?.transform,
        StaticMarkerTarget.YellowStamp => _yellowStampSlot?.transform,
        StaticMarkerTarget.RedStamp    => _redStampSlot?.transform,
        StaticMarkerTarget.HandOff     => _handOffPoint?.transform,
        StaticMarkerTarget.ToolLocker  => _toolLockerTarget,
        _                              => null
    };

    // All marker/arrow helpers route through MegaphoneDialogueManager which is always-active
    // and fully spawned, avoiding the ActiveSceneSynchronization issue on Day GameObjects
    // (which start inactive and are never spawned by NGO into the session).

    private void ShowNetworkedMarker(NetworkObject target)
    {
        if (target != null) MegaphoneDialogueManager.Instance?.ShowMarkerSynced(target);
    }

    private void HideNetworkedMarker(NetworkObject target)
    {
        if (target != null) MegaphoneDialogueManager.Instance?.HideMarkerSynced(target);
    }

    private void ShowStaticMarker(StaticMarkerTarget target)
    {
        Transform t = GetStaticMarkerTransform(target);
        if (t != null) MegaphoneDialogueManager.Instance?.ShowStaticMarkerSynced(t);
    }

    private void HideStaticMarker(StaticMarkerTarget target)
    {
        Transform t = GetStaticMarkerTransform(target);
        if (t != null) MegaphoneDialogueManager.Instance?.HideStaticMarkerSynced(t);
    }

    private void SetSwitchArrow(bool active)
    {
        if (_switchArrow == null) return;
        // Routes through MegaphoneDialogueManager so the ClientRpc reaches all clients.
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_switchArrow.transform, active);
    }

    private void SetDrawerArrow(bool active)
    {
        if (_drawerArrow == null) return;
        // Routes through MegaphoneDialogueManager so the ClientRpc reaches all clients.
        MegaphoneDialogueManager.Instance?.SetGameObjectActiveSynced(_drawerArrow.transform, active);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows a megaphone bark on all clients and waits until it finishes speaking.
    /// Must only be called from server-side coroutines.
    /// </summary>
    private IEnumerator ShowAndWait(string line)
    {
        // Wait for any previously running bark to complete before issuing the next one.
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);

        MegaphoneDialogueManager.Instance.ShowDialogueSynced(line);

        // Yield one frame so the NetworkVariable write and ClientRpc dispatch can be
        // flushed by the Netcode transport before we start polling IsSpeakingSynced.
        // Without this the WaitUntil below exits immediately because the flag hasn't
        // propagated yet and the bark is silently skipped.
        yield return null;

        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);
    }

    // -------------------------------------------------------------------------
    // Phone Ring Tutorial Handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the "Answer the telephone" tutorial bar the first time the phone rings on Day 1.
    /// Fires on all clients via <see cref="Telephone.OnRingStarted"/>.
    /// The bars auto-dismiss after the default hold duration — this is a brief notification only.
    /// </summary>
    private void OnPhoneRingStarted()
    {
        if (_phoneRingTutorialShown) return;
        _phoneRingTutorialShown = true;

        PlayerTutorialUI.Instance?.Show("Answer the telephone");
    }

    // =========================================================================
    // Soldier Scripted Event — Handlers
    // =========================================================================

    /// <summary>
    /// Fires on the server when the soldier finishes mocking the player and begins walking away.
    /// The soldier occupies the last Day 1 suspect slot, so <see cref="ShiftManager"/> moves
    /// to clock-out automatically once <see cref="SoldierMockingController"/> calls
    /// <see cref="ShiftManager.SetNextSuspectReady"/>.
    /// </summary>
    private void OnSoldierSequenceCompleteHandler()
    {
        if (this == null) return;
        Debug.Log("[Day_01] Soldier mocking sequence complete — shift clock-out incoming.");
    }
}
