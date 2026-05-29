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

    [Header("Other Day Notebooks — Hidden During Day 1")]
    [Tooltip("The Mutation Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

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

        // Lock the exit door immediately — it stays locked for the entire tutorial shift.
        ShiftManager.Instance.OnDoorLock?.Invoke();

        // Gate the drawer — unlocked later when the tutorial prompts the player.
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

        // All tutorial arrows start hidden.
        if (_switchArrow != null) _switchArrow.SetActive(false);
        if (_drawerArrow != null) _drawerArrow.SetActive(false);

        // All stamp stations are locked until the tutorial reaches the stamping beat.
        _greenStampSlot?.SetSlotInteractable(false);
        _yellowStampSlot?.SetSlotInteractable(false);
        _redStampSlot?.SetSlotInteractable(false);

        // Notebook stays non-interactable until the anomaly reveal beat.
        _examNotebook?.SetInteractableNetworked(false);

        // Hide the mutation and biological notebooks — they are not introduced until Day 2 and Day 3.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);

        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        ShiftManager.Instance.OnDayStart        += OnDayStarted;
        Debug.Log($"[Day_01] DayActivated: subscribed to ShiftManager.OnDayStart. IsServer={NetworkManager.Singleton?.IsServer}, IsHost={NetworkManager.Singleton?.IsHost}.");
        SuspectController.OnSuspectArrived       += OnSuspectArrivedHandler;
        _onPaperworkSpawned = OnPaperworkSpawnedHandler;
        SuspectController.OnPaperworkSpawned     += _onPaperworkSpawned;
        SwitchButton.OnPressed                   += OnSwitchPressed;
        _onFolderDocumentFiled = OnFolderDocumentFiled;
        FolderController.OnDocumentAdded         += _onFolderDocumentFiled;
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

        SuspectController.OnSuspectArrived   -= OnSuspectArrivedHandler;
        SuspectController.OnPaperworkSpawned -= _onPaperworkSpawned;
        SwitchButton.OnPressed               -= OnSwitchPressed;

        if (_onFolderDocumentFiled != null)
            FolderController.OnDocumentAdded      -= _onFolderDocumentFiled;

        FolderController.OnFolderEquipped         -= OnFolderPickedUp;
        FolderController.OnFolderHandedOff        -= OnFolderHandedOffHandler;
        FolderController.OnAnyFolderStamped       -= OnFolderStamped;

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
        Debug.Log("[Day_01] OnDayStarted: starting Day1TutorialSequence on server.");
        StartCoroutine(Day1TutorialSequence());
    }

    private IEnumerator Day1TutorialSequence()
    {
        yield return new WaitForSeconds(7f);

        yield return ShowAndWait("Good morning. We've been expecting you.");
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("You'll be screening subjects entering the town. Your responses will be noted. Press the button when ready.");

        if (NetworkManager.Singleton.IsServer)
            _switchButton.SetReady(true);

        SetSwitchArrow(true);
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

        yield return ShowAndWait("A suspect has arrived. Pick up their ID card.");

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

        yield return ShowAndWait("Hold left-click to inspect. Right-click over the desk to put it down.");

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
        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("Now pick up the application form.");

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

        yield return ShowAndWait("Cross-reference the documents. Note any discrepancies.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("This subject appears clean. Proceed.");
        yield return new WaitForSeconds(2f);

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
        yield return ShowAndWait("Place it on the desk.");

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
            yield return ShowAndWait("Pick up the ID card and drag it onto the folder to file it.");
            yield return new WaitUntil(() => _documentsFiledCount >= 1);
        }

        yield return new WaitForSeconds(0.5f);

        // Second document — only prompt if not already filed.
        if (_documentsFiledCount < 2)
        {
            yield return ShowAndWait("Now file the application form.");
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

        yield return ShowAndWait("Both documents filed. This subject is clean — stamp the folder green to clear them.");

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

        yield return ShowAndWait("Your decisions are being recorded. Stay attentive.");

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

    private IEnumerator Suspect2IDCardBeat()
    {
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("Another subject. Review their documents.");

        // Unlock both documents. No arrows — players already know how to pick up documents.
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
        yield return ShowAndWait("When you spot an anomaly, mark it in the exam notebook. Pick it up and tick the appropriate box.");

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
        // Subscribe before dialogue so ticking during the prompt is not missed.
        // OnAnyCheckboxChecked fires on all clients via ExamNotebook's NetworkVariable callback,
        // so the server-only coroutine receives the event regardless of which player ticked a box.
        bool anyBoxChecked = false;
        System.Action<ExamNotebook> onChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += onChecked;

        yield return ShowAndWait("Tick every anomaly on the page.");

        // Guard: player may have ticked a box during dialogue or earlier.
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

        yield return ShowAndWait("Interact with the folder while holding the notebook to file it.");

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

        yield return ShowAndWait("Place the stamped folder in the window slot.");

        ShowStaticMarker(StaticMarkerTarget.HandOff);

        // Guard: player may have placed the folder during the preceding dialogue.
        if (!_folderHandedOff)
            yield return new WaitUntil(() => _folderHandedOff);

        HideStaticMarker(StaticMarkerTarget.HandOff);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("A third subject is incoming.");

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
        yield return new WaitForSeconds(3f);

        yield return ShowAndWait("Review their documents.");

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

        yield return ShowAndWait("Multiple anomalies — far beyond the threshold.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mark all five in the exam notebook.");

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

        yield return ShowAndWait("Tick all five boxes.");

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

        yield return ShowAndWait("File your findings into the folder.");

        if (ExamNotebook.AnyPageFiled)
            _s3NotebookPageFiled = true;

        yield return new WaitUntil(() => _s3NotebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnSuspect3NotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Five anomalies — the threshold is reached. Elimination is required.");

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

        yield return ShowAndWait("Stamp the folder red.");

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
        // _folderHandedOff reset and OnFolderHandedOff subscription are done in
        // Suspect3StampBeat before arming the stamp, so no early hand-off is missed.
        yield return new WaitUntil(() => _folderStamped);

        // Wait for the player to return the red stamp — no arrow, already taught.
        yield return new WaitUntil(() => _redStampSlot == null || _redStampSlot.IsStampInSlot);
        _redStampSlot?.LockStampAndSlot();
        Debug.Log("[Day_01] Suspect3HandOffBeat: red stamp returned and locked.");

        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Elimination is final. The decision is yours to make.");
        yield return new WaitForSeconds(1f);

        yield return ShowAndWait("Place the folder in the window slot.");

        ShowStaticMarker(StaticMarkerTarget.HandOff);

        if (!_folderHandedOff)
            yield return new WaitUntil(() => _folderHandedOff);

        HideStaticMarker(StaticMarkerTarget.HandOff);

        yield return new WaitForSeconds(2f);

        yield return ShowAndWait("You know what's expected. We'll be watching.");
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
    private enum StaticMarkerTarget { GreenStamp, YellowStamp, RedStamp, HandOff }

    private Transform GetStaticMarkerTransform(StaticMarkerTarget target) => target switch
    {
        StaticMarkerTarget.GreenStamp  => _greenStampSlot?.transform,
        StaticMarkerTarget.YellowStamp => _yellowStampSlot?.transform,
        StaticMarkerTarget.RedStamp    => _redStampSlot?.transform,
        StaticMarkerTarget.HandOff     => _handOffPoint?.transform,
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
}
