using System;
using System.Collections;
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

    [Tooltip("The folder inside the drawer the player must pick up and place on the desk.")]
    [SerializeField] private FolderController _folder;

    [Tooltip("The green ink stamp station — made interactable once both documents are filed.")]
    [SerializeField] private InkStamp _greenStampSlot;

    [Tooltip("The yellow ink stamp station — blocked for the entire Day 1 tutorial.")]
    [SerializeField] private InkStamp _yellowStampSlot;

    [Tooltip("The red ink stamp station — blocked for the entire Day 1 tutorial.")]
    [SerializeField] private InkStamp _redStampSlot;

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
        SuspectController.ForceNextSuspectClean = true;

        // Spawn the first suspect almost immediately after the switch is pressed (0 s scheduling
        // delay + the existing 3 s walk-in wait in WaitAndSpawnNextSuspect = ~3 s total).
        ShiftManager.OverrideFirstArrivalInterval = new UnityEngine.Vector2(0f, 0f);

        // All tutorial arrows start hidden.
        if (_switchArrow != null) _switchArrow.SetActive(false);
        if (_drawerArrow != null) _drawerArrow.SetActive(false);

        // All stamp stations are locked until the tutorial reaches the stamping beat.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);

        ShiftManager.Instance.OnDayStart     += OnDayStarted;
        SuspectController.OnSuspectArrived   += OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawnedHandler;
        SwitchButton.OnPressed               += OnSwitchPressed;
        _onFolderDocumentFiled = OnFolderDocumentFiled;
        FolderController.OnDocumentAdded     += _onFolderDocumentFiled;
        FolderController.OnFolderHandedOff   += OnFolderHandedOffHandler;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived   -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawnedHandler;
        SwitchButton.OnPressed               -= OnSwitchPressed;
        FolderController.OnDocumentAdded     -= _onFolderDocumentFiled;
        FolderController.OnFolderEquipped    -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff   -= OnFolderHandedOffHandler;

        if (_drawer != null)
            _drawer.OnOpened -= OnDrawerFirstOpened;

        if (_folder != null)
            _folder.OnEquip -= OnFolderPickedUp;

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.OnEquip   -= OnIDCardPickedUp;
            _tutorialIDCard.OnUnEquip -= OnIDCardPutDown;
        }

        if (_tutorialAppForm != null)
            _tutorialAppForm.OnEquip -= OnAppFormPickedUp;

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
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawnedHandler;
        SwitchButton.OnPressed               -= OnSwitchPressed;

        if (_onFolderDocumentFiled != null)
            FolderController.OnDocumentAdded  -= _onFolderDocumentFiled;

        FolderController.OnFolderEquipped     -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff    -= OnFolderHandedOffHandler;

        if (_drawer != null)
            _drawer.OnOpened -= OnDrawerFirstOpened;

        if (_tutorialIDCard != null)
        {
            _tutorialIDCard.OnEquip   -= OnIDCardPickedUp;
            _tutorialIDCard.OnUnEquip -= OnIDCardPutDown;
        }

        if (_tutorialAppForm != null)
            _tutorialAppForm.OnEquip -= OnAppFormPickedUp;
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
    private void OnPaperworkSpawnedHandler(IDCard idCard)
    {
        if (this == null) return;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawnedHandler;

        _tutorialIDCard = idCard;

        var docs = SuspectController.Instance.SpawnedDocuments;
        if (docs.Count >= 2)
            _tutorialAppForm = docs[1];

        if (_documentArrowPrefab != null)
        {
            _idCardArrow  = SpawnDocumentArrow(_tutorialIDCard != null  ? _tutorialIDCard.transform  : null);
            _appFormArrow = SpawnDocumentArrow(_tutorialAppForm != null ? _tutorialAppForm.transform : null);
        }

        foreach (var doc in docs)
            doc.SetInteractable(false);

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
            _tutorialIDCard.SetInteractable(true);
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
            _tutorialAppForm.SetInteractable(true);
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

    private void OnFolderPickedUp()
    {
        if (this == null) return;
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

        // Prevent the player from pulling the document back out of the folder.
        if (document != null)
            document.SetInteractable(false);
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Stamp
    // -------------------------------------------------------------------------

    private IEnumerator StampBeat()
    {
        yield return new WaitForSeconds(1.5f);

        yield return ShowAndWait("Both documents are filed. This suspect looks clean — stamp the folder green to approve them.");

        // Unlock only the green stamp station.
        _greenStampSlot?.SetSlotInteractable(true);

        // Spawn a tracking arrow above the green stamp slot.
        GameObject stampArrow = SpawnDocumentArrow(_greenStampSlot != null ? _greenStampSlot.transform : null);
        if (stampArrow != null)
            stampArrow.SetActive(true);

        // Wait until the player picks the stamp up out of its slot.
        yield return new WaitUntil(() =>
            _greenStampSlot == null || !_greenStampSlot.IsStampInSlot);

        if (stampArrow != null)
            Destroy(stampArrow);

        yield return new WaitForSeconds(0.5f);

        yield return ShowAndWait("Now interact with the folder while holding the stamp to approve it.");

        StartCoroutine(HandOffBeat());
    }

    // -------------------------------------------------------------------------
    // Tutorial Sequence — Hand Off Folder at Window
    // -------------------------------------------------------------------------

    /// <summary>
    /// A persistent flag set as soon as the static event fires.
    /// Ensures the WaitUntil resolves even if the event fires before the coroutine polls it.
    /// </summary>
    private bool _folderHandedOff;

    private void OnFolderHandedOffHandler()
    {
        if (this == null) return;
        _folderHandedOff = true;
        FolderController.OnFolderHandedOff -= OnFolderHandedOffHandler;
    }

    private IEnumerator HandOffBeat()
    {
        // Wait until the folder has been stamped (isStamped syncs via NetworkVariable).
        yield return new WaitUntil(() =>
            _folder == null || _folder.IsStamped);

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Good. Now place the stamped folder in the window slot to send them on their way.");

        // Wait for the player to hand the folder off to the HandOffPoint.
        yield return new WaitUntil(() => _folderHandedOff);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Well done. That's your first subject processed. Keep it up.");
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
        MegaphoneDialogueManager.Instance.ShowDialogue(line);
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeaking);
    }
}
