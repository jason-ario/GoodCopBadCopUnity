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

    [Tooltip("Prefab instantiated as a child of each tutorial document when it spawns.")]
    [SerializeField] private GameObject _documentArrowPrefab;

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

    [Tooltip("Tutorial arrow displayed above the exam notebook. Starts inactive.")]
    [SerializeField] private GameObject _notebookArrow;

    // Cached document references received via OnPaperworkSpawned.
    private IDCard _tutorialIDCard;
    private PickableObject _tutorialAppForm;

    // Arrows instantiated at runtime above the spawned documents.
    private GameObject _idCardArrow;
    private GameObject _appFormArrow;

    // Running count of documents successfully filed into the tutorial folder.
    private int _documentsFiledCount;

    // Cached delegate so subscribe/unsubscribe reference equality is preserved.
    private System.Action<PickableObject> _onFolderDocumentFiled;
    private System.Action<IDCard, PickableObject> _onPaperworkSpawned;
    private System.Action<IDCard, PickableObject> _onSuspect2PaperworkSpawned;

    // -------------------------------------------------------------------------
    // Suspect 2 state
    // -------------------------------------------------------------------------
    private IDCard _suspect2IDCard;
    private PickableObject _suspect2AppForm;
    private GameObject _s2IDCardArrow;
    private GameObject _s2AppFormArrow;
    private int _suspect2DocumentsFiledCount;
    private System.Action<PickableObject> _onSuspect2DocumentFiled;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated(); // Calls AnomalyManager.ApplyUnlocksFromSave()

        // Persist documentation-only unlock for Day 1 and apply to live category locks.
        AnomalyManager.Instance.UnlockDocumentationOnly();

        // Lock the exit door immediately — it stays locked for the entire tutorial shift.
        ShiftManager.Instance.OnDoorLock?.Invoke();

        // Gate the drawer — unlocked later when the tutorial prompts the player.
        if (_drawer != null)
            _drawer.SetLocked(true);

        // Guarantee the first suspect has no anomalies — the tutorial intro must be clean.
        // Server-only: these static flags are consumed exclusively by the server's spawn logic.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            SuspectController.ForceNextSuspectClean          = true;
            ShiftManager.OverrideFirstArrivalInterval        = new UnityEngine.Vector2(0f, 0f);
            ShiftManager.OverrideSuspectArrivalInterval      = new UnityEngine.Vector2(3f, 3f);
        }

        // All tutorial arrows start hidden.
        if (_switchArrow != null) _switchArrow.SetActive(false);
        if (_drawerArrow != null) _drawerArrow.SetActive(false);

        // All stamp stations are locked until the tutorial reaches the stamping beat.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);

        // Notebook stays non-interactable until the anomaly reveal beat.
        _examNotebook?.SetInteractableNetworked(false);

        ShiftManager.Instance.OnDayStart        += OnDayStarted;
        SuspectController.OnSuspectArrived       += OnSuspectArrivedHandler;
        _onPaperworkSpawned = OnPaperworkSpawnedHandler;
        SuspectController.OnPaperworkSpawned     += _onPaperworkSpawned;
        SwitchButton.OnPressed                   += OnSwitchPressed;
        _onFolderDocumentFiled = OnFolderDocumentFiled;
        FolderController.OnDocumentAdded         += _onFolderDocumentFiled;
        FolderController.OnFolderHandedOff       += OnFolderHandedOffHandler;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived      -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned    -= _onPaperworkSpawned;
        SwitchButton.OnPressed                  -= OnSwitchPressed;
        FolderController.OnDocumentAdded        -= _onFolderDocumentFiled;
        FolderController.OnFolderEquipped       -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff      -= OnFolderHandedOffHandler;
        FolderController.OnAnyFolderStamped     -= OnFolderStamped;

        if (_onSuspect2DocumentFiled != null)
            FolderController.OnDocumentAdded    -= _onSuspect2DocumentFiled;
        if (_onSuspect2PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned -= _onSuspect2PaperworkSpawned;
        FolderController.OnFolderEquipped       -= OnSuspect2FolderPickedUp;
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
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived   -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned -= _onPaperworkSpawned;
        SwitchButton.OnPressed               -= OnSwitchPressed;

        if (_onFolderDocumentFiled != null)
            FolderController.OnDocumentAdded      -= _onFolderDocumentFiled;

        FolderController.OnFolderEquipped         -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff        -= OnFolderHandedOffHandler;
        FolderController.OnAnyFolderStamped       -= OnFolderStamped;

        if (_onSuspect2DocumentFiled != null)
            FolderController.OnDocumentAdded      -= _onSuspect2DocumentFiled;
        if (_onSuspect2PaperworkSpawned != null)
            SuspectController.OnPaperworkSpawned  -= _onSuspect2PaperworkSpawned;
        FolderController.OnFolderEquipped         -= OnSuspect2FolderPickedUp;
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
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Switch
    // -------------------------------------------------------------------------

    private void OnDayStarted()
    {
        if (this == null) return;
        StartCoroutine(Day1TutorialSequence());
    }

    private IEnumerator Day1TutorialSequence()
    {
        yield return new WaitForSeconds(7f);

        yield return ShowAndWait("Good morning, sunshine.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Welcome to your first day on the job...");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("We've been waiting for you.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("The last guy didn't last very long. We're hoping you can do better.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Judging by the looks of you, I give you a week, tops.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("But to give you the best shot, I'll be here to help out.");
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("See that button over there? Press it to begin your shift.");

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _switchButton.SetReady(true);

        if (_switchArrow != null)
            _switchArrow.SetActive(true);
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Paperwork Arrives
    // -------------------------------------------------------------------------

    private void OnSuspectArrivedHandler(int suspectIndex)
    {
        if (suspectIndex != 0) return;
        SuspectController.OnSuspectArrived -= OnSuspectArrivedHandler;
    }

    /// <summary>
    /// Fires on all clients once the ID card has been network-spawned and set up.
    /// Locks both documents, attaches tutorial arrows to each, and begins the ID card beat.
    /// </summary>
    private void OnPaperworkSpawnedHandler(IDCard idCard, PickableObject appForm)
    {
        if (this == null) return;
        SuspectController.OnPaperworkSpawned -= _onPaperworkSpawned;
        _onPaperworkSpawned = null;

        _tutorialIDCard = idCard;
        _tutorialAppForm = appForm;

        if (_documentArrowPrefab != null)
        {
            _idCardArrow  = SpawnDocumentArrow(_tutorialIDCard  != null ? _tutorialIDCard.transform  : null);
            _appFormArrow = SpawnDocumentArrow(_tutorialAppForm != null ? _tutorialAppForm.transform : null);
        }

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
    private GameObject SpawnDocumentArrow(Transform target)
    {
        if (_documentArrowPrefab == null || target == null) return null;

        GameObject arrow = Instantiate(_documentArrowPrefab);
        var marker = arrow.GetComponent<TutorialMarker>();
        marker.SetHoverHeight(0.2f);
        marker.Show(target);
        arrow.SetActive(false);
        return arrow;
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — ID Card Pickup
    // -------------------------------------------------------------------------

    private IEnumerator IDCardInspectionBeat()
    {
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("A suspect has arrived. Pick up their ID card with the left mouse button.");

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.SetInteractableNetworked(true);
            _tutorialIDCard.OnEquip += OnIDCardPickedUp;
        }

        if (_idCardArrow != null)
            _idCardArrow.SetActive(true);
    }

    private void OnIDCardPickedUp()
    {
        if (this == null) return;
        _tutorialIDCard.OnEquip -= OnIDCardPickedUp;

        if (_idCardArrow != null)
            _idCardArrow.SetActive(false);

        StartCoroutine(PutDownTutorialBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Inspect & Put Down
    // -------------------------------------------------------------------------

    private IEnumerator PutDownTutorialBeat()
    {
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Hold the left mouse button to inspect it up close.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Hold the right mouse button and position it over the desk to put it down.");

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.OnUnEquip += OnIDCardPutDown;
            yield return new WaitUntil(() => !_tutorialIDCard.IsHeld);
            _tutorialIDCard.OnUnEquip -= OnIDCardPutDown;
        }

        StartCoroutine(AppFormInspectionBeat());
    }

    // Stub — WaitUntil on IsHeld drives the flow; this hook exists for future use.
    private void OnIDCardPutDown() { }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Application Form Pickup
    // -------------------------------------------------------------------------

    private IEnumerator AppFormInspectionBeat()
    {
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Good. Now pick up the application form.");

        if (_tutorialAppForm != null)
        {
            _tutorialAppForm.SetInteractableNetworked(true);
            _tutorialAppForm.OnEquip += OnAppFormPickedUp;
        }

        if (_appFormArrow != null)
            _appFormArrow.SetActive(true);
    }

    private void OnAppFormPickedUp()
    {
        if (this == null) return;
        _tutorialAppForm.OnEquip -= OnAppFormPickedUp;

        if (_appFormArrow != null)
            _appFormArrow.SetActive(false);

        StartCoroutine(DiscrepancyBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Discrepancy Lesson
    // -------------------------------------------------------------------------

    private IEnumerator DiscrepancyBeat()
    {
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Always cross-reference your documents — look for any discrepancies between them.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("This subject looks clean. No anomalies detected. You can let them through.");
        yield return new WaitForSeconds(2f);

        StartCoroutine(DrawerTutorialBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Drawer
    // -------------------------------------------------------------------------

    private IEnumerator DrawerTutorialBeat()
    {
        yield return ShowAndWait("Now assemble the subject's folder. Grab it from the drawer.");

        if (_drawer != null)
            _drawer.SetLocked(false);

        if (_drawerArrow != null)
        {
            _drawerArrow.SetActive(true);
            _drawer.OnOpened += OnDrawerFirstOpened;
        }

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
        yield return ShowAndWait("Place it on the desk with the right mouse button.");

        yield return new WaitUntil(() => _folder == null || !_folder.IsHeld);

        StartCoroutine(DocumentInsertBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Insert Documents into Folder
    // -------------------------------------------------------------------------

    private IEnumerator DocumentInsertBeat()
    {
        yield return new WaitForSeconds(1f);

        _documentsFiledCount = 0;

        // First document.
        yield return ShowAndWait("Now pick up the ID card and drag it onto the folder to file it.");
        yield return new WaitUntil(() => _documentsFiledCount >= 1);

        yield return new WaitForSeconds(0.5f);

        // Second document — only prompt if the player hasn't already filed it.
        if (_documentsFiledCount < 2)
            yield return ShowAndWait("Good. Now do the same with the application form.");
        yield return new WaitUntil(() => _documentsFiledCount >= 2);

        FolderController.OnDocumentAdded -= _onFolderDocumentFiled;
        StartCoroutine(StampBeat());
    }

    private void OnFolderDocumentFiled(PickableObject document)
    {
        if (this == null) return;
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

        yield return ShowAndWait("Both documents are filed. This suspect looks clean — stamp the folder green to approve them.");

        // Subscribe to the static event now — any folder stamped from this point counts.
        FolderController.OnAnyFolderStamped += OnFolderStamped;

        // Unlock only the green stamp station.
        _greenStampSlot?.SetSlotInteractable(true);

        // Spawn a tracking arrow above the green stamp slot.
        _stampArrow = SpawnDocumentArrow(_greenStampSlot != null ? _greenStampSlot.transform : null);
        if (_stampArrow != null)
            _stampArrow.SetActive(true);

        // Hide the arrow the moment the stamp leaves its slot — fire-and-forget.
        StartCoroutine(HideStampArrowOnPickup(_greenStampSlot));

        // HandOffBeat waits for the static event flag set by OnFolderStamped.
        StartCoroutine(HandOffBeat());
    }

    /// <summary>
    /// Hides the stamp arrow as soon as the player lifts the stamp out of its slot.
    /// Runs independently of HandOffBeat so neither blocks the other.
    /// </summary>
    private IEnumerator HideStampArrowOnPickup(InkStamp slot)
    {
        yield return new WaitUntil(() => slot == null || !slot.IsStampInSlot);
        if (_stampArrow != null)
            _stampArrow.SetActive(false);
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

    // Arrow above the stamp station — kept as a field so OnFolderStamped can destroy it.
    private GameObject _stampArrow;

    private void OnFolderStamped()
    {
        if (this == null) return;
        _folderStamped = true;
        FolderController.OnAnyFolderStamped -= OnFolderStamped;
        if (_stampArrow != null)
        {
            Destroy(_stampArrow);
            _stampArrow = null;
        }
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

        Debug.Log("[Day_01] HandOffBeat: folder stamped, proceeding.");
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Good. Now place the stamped folder in the window slot to send them on their way.");

        // Spawn a tracking arrow above the hand-off point so the player knows where to go.
        GameObject handOffArrow = SpawnDocumentArrow(_handOffPoint != null ? _handOffPoint.transform : null);
        if (handOffArrow != null)
            handOffArrow.SetActive(true);

        // Wait for the player to hand the folder off to the HandOffPoint.
        yield return new WaitUntil(() => _folderHandedOff);

        if (handOffArrow != null)
            Destroy(handOffArrow);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Well done. That's your first subject processed. Keep it up.");

        // Subscribe for the second suspect's paperwork — the beat begins on actual arrival,
        // not on a fixed timer, so we don't race ahead of the suspect's walk-in.
        // Force exactly 2 documentation anomalies so the notebook tutorial always has material.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
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
        SuspectController.OnPaperworkSpawned -= _onSuspect2PaperworkSpawned;
        _onSuspect2PaperworkSpawned = null;

        _suspect2IDCard  = idCard;
        _suspect2AppForm = appForm;

        _s2IDCardArrow  = SpawnDocumentArrow(_suspect2IDCard  != null ? _suspect2IDCard.transform  : null);
        _s2AppFormArrow = SpawnDocumentArrow(_suspect2AppForm != null ? _suspect2AppForm.transform : null);

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

    private IEnumerator Suspect2IDCardBeat()
    {
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("Another subject has arrived. Inspect their ID card and application form.");

        // Unlock both documents and show both arrows simultaneously.
        _s2IDCardInspected  = false;
        _s2AppFormInspected = false;

        if (_suspect2IDCard != null)
        {
            _suspect2IDCard.SetInteractableNetworked(true);
            if (_s2IDCardArrow != null) _s2IDCardArrow.SetActive(true);
            _suspect2IDCard.OnEquip += OnSuspect2IDCardPickedUp;
        }

        if (_suspect2AppForm != null)
        {
            _suspect2AppForm.SetInteractableNetworked(true);
            if (_s2AppFormArrow != null) _s2AppFormArrow.SetActive(true);
            _suspect2AppForm.OnEquip += OnSuspect2AppFormPickedUp;
        }

        // Wait until the player has picked up both documents at least once.
        yield return new WaitUntil(() => _s2IDCardInspected && _s2AppFormInspected);

        StartCoroutine(Suspect2AnomalyRevealBeat());
    }

    private void OnSuspect2IDCardPickedUp()
    {
        if (this == null) return;
        if (_suspect2IDCard != null) _suspect2IDCard.OnEquip -= OnSuspect2IDCardPickedUp;
        if (_s2IDCardArrow != null) _s2IDCardArrow.SetActive(false);
        _s2IDCardInspected = true;
    }

    private void OnSuspect2AppFormPickedUp()
    {
        if (this == null) return;
        if (_suspect2AppForm != null) _suspect2AppForm.OnEquip -= OnSuspect2AppFormPickedUp;
        if (_s2AppFormArrow != null) _s2AppFormArrow.SetActive(false);
        _s2AppFormInspected = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Anomaly reveal + notebook unlock
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2AnomalyRevealBeat()
    {
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("You'll notice there's a discrepancy between the two documents. Something doesn't add up.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Multiple documentation anomalies are a warning sign that a subject's mind may be deteriorating — a prompt for medical evaluation.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("When you spot an anomaly, mark it using the exam notebook. Pick it up and tick the box for what you found.");

        _examNotebook?.SetInteractableNetworked(true);
        if (_notebookArrow != null) _notebookArrow.SetActive(true);

        yield return new WaitUntil(() => _examNotebook != null && _examNotebook.IsHeld);

        if (_notebookArrow != null) _notebookArrow.SetActive(false);

        StartCoroutine(NotebookCheckBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Checkbox completion
    // -------------------------------------------------------------------------

    private IEnumerator NotebookCheckBeat()
    {
        yield return ShowAndWait("Tick the boxes for every anomaly you can find on the page.");

        yield return new WaitUntil(() => _examNotebook != null && _examNotebook.AllVisibleBoxesChecked);

        StartCoroutine(NotebookFileIntoBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — File notebook into folder
    // -------------------------------------------------------------------------

    private bool _notebookPageFiled;

    private IEnumerator NotebookFileIntoBeat()
    {
        yield return ShowAndWait("Now interact with the folder while holding the notebook to file your findings.");

        _notebookPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnNotebookPageFiled;

        yield return new WaitUntil(() => _notebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnNotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Based on how accurate your findings are, you'll receive matching compensation. The more thorough you are, the better.");

        StartCoroutine(NotebookFileBeat());
    }

    private void OnNotebookPageFiled()
    {
        if (this == null) return;
        _notebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — File remaining documents into folder
    // -------------------------------------------------------------------------

    private IEnumerator NotebookFileBeat()
    {
        yield return ShowAndWait("Now file the ID card and application form into the folder as well.");

        _suspect2DocumentsFiledCount = 0;
        _onSuspect2DocumentFiled = OnSuspect2DocumentFiled;
        FolderController.OnDocumentAdded += _onSuspect2DocumentFiled;

        yield return new WaitUntil(() => _suspect2DocumentsFiledCount >= 2);

        StartCoroutine(Suspect2StampBeat());
    }

    private void OnSuspect2FolderPickedUp(FolderController folder)
    {
        if (this == null) return;
        FolderController.OnFolderEquipped -= OnSuspect2FolderPickedUp;
        if (_drawerArrow != null) _drawerArrow.SetActive(false);
    }

    private void OnSuspect2DocumentFiled(PickableObject doc)
    {
        if (this == null) return;
        _suspect2DocumentsFiledCount++;
        if (doc != null) doc.LockInteractableNetworked();

        if (_suspect2DocumentsFiledCount >= 3)
        {
            FolderController.OnDocumentAdded -= _onSuspect2DocumentFiled;
            _onSuspect2DocumentFiled = null;
        }
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Stamp beat
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2StampBeat()
    {
        _folderStamped = false;

        yield return new WaitForSeconds(1.5f);

        yield return ShowAndWait("All documents filed. This subject has an anomaly — stamp the folder yellow to quarantine them.");

        FolderController.OnAnyFolderStamped += OnFolderStamped;

        // Only the yellow stamp is available; green and red remain locked for this beat.
        _yellowStampSlot?.SetSlotInteractable(true);

        _stampArrow = SpawnDocumentArrow(_yellowStampSlot != null ? _yellowStampSlot.transform : null);
        if (_stampArrow != null) _stampArrow.SetActive(true);

        StartCoroutine(HideStampArrowOnPickup(_yellowStampSlot));
        StartCoroutine(Suspect2HandOffBeat());
    }

    // -------------------------------------------------------------------------
    // Suspect 2 — Hand-off beat (mirrors suspect 1's HandOffBeat)
    // -------------------------------------------------------------------------

    private IEnumerator Suspect2HandOffBeat()
    {
        _folderHandedOff = false;

        yield return new WaitUntil(() => _folderStamped);

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Quarantine isolates the subject and gives them time to recover over the next couple of days. Better safe than sorry.");
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Place the stamped folder in the window slot to send them on their way.");

        GameObject handOffArrow = SpawnDocumentArrow(_handOffPoint != null ? _handOffPoint.transform : null);
        if (handOffArrow != null) handOffArrow.SetActive(true);

        yield return new WaitUntil(() => _folderHandedOff);

        if (handOffArrow != null) Destroy(handOffArrow);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Excellent. You've learned the full inspection workflow. Good luck out there.");
    }

    // -------------------------------------------------------------------------
    // Tutorial Arrow Dismissal
    // -------------------------------------------------------------------------

    private void OnSwitchPressed()
    {
        if (this == null) return;
        SwitchButton.OnPressed -= OnSwitchPressed;

        if (_switchArrow != null)
            _switchArrow.SetActive(false);

        // Day 1 hasn't introduced the lever yet — open the window automatically
        // so the suspect can deliver paperwork once they arrive.
        // ShiftManager.OpenBoothShutter guards against closing an already-open shutter.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            ShiftManager.Instance.OpenBoothShutter();
    }

    private void OnDrawerFirstOpened()
    {
        if (this == null) return;
        _drawer.OnOpened -= OnDrawerFirstOpened;

        if (_drawerArrow != null)
            _drawerArrow.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Shows a megaphone bark and waits until it finishes speaking.</summary>
    private IEnumerator ShowAndWait(string line)
    {
        // Wait for any previously running bark to complete before issuing the next one.
        // MegaphoneDialogueManager.ShowDialogue silently drops the call if IsSpeaking is true,
        // which would leave the WaitUntil below hanging indefinitely.
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeaking);
        MegaphoneDialogueManager.Instance.ShowDialogue(line);
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeaking);
    }
}
