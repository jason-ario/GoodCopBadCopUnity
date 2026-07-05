using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 2 — mutation exam tutorial shift.
///
/// Unlocks mutation anomalies alongside the existing documentation set.
/// Waits for the first suspect that has at least one mutation anomaly, then guides
/// the player through picking up the Mutation Exam notebook, ticking the checklist,
/// and filing the page into the folder. Remaining suspects are unscripted.
///
/// All tutorial coroutines run server-only. Megaphone barks are broadcast to all
/// clients via MegaphoneDialogueManager.ShowDialogueSynced. Object-pickup gates use
/// the synced IsHeld NetworkVariable so either player's action advances the tutorial.
///
/// Day 2 Opening Sequence (server-side):
///   Vlad is placed at a specific spawn position in the scene. When a player approaches
///   him it triggers a scripted intro dialogue with camera pans to the supply box and
///   back. After the dialogue ends Vlad walks via NavMeshAgent through a series of waypoints:
///   he opens the booth door, performs an unlock gesture at the tool locker (unlocking
///   the padlock), then faces his final position and waits. When a player approaches him
///   there a second dialogue explains the tool locker. Vlad then makes an ominous remark
///   and walks off.
/// </summary>
public class Day_02 : DayBase
{
    // -------------------------------------------------------------------------
    // Day 2 Tutorial (existing)
    // -------------------------------------------------------------------------

    [Header("Day 2 Tutorial")]
    [Tooltip("The Mutation Exam notebook — hidden until the tutorial beat.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Header("Other Day Notebooks — Hidden During Day 2")]
    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 2.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    // Whether the mutation notebook tutorial beat has already fired this shift.
    private bool _mutationTutorialFired;

    // Persistent flags for early-action guards (mirrors Day_01 pattern).
    private bool _notebookPageFiled;

    // -------------------------------------------------------------------------
    // Day 2 Opening Sequence
    // -------------------------------------------------------------------------

    [Header("Day 2 — Vlad Opening Sequence")]
    [Tooltip("Vlad's SuspectCharacter placed in the scene. He starts at _vladSpawnPos and walks the player " +
             "through the booth when approached.")]
    [SerializeField] private SuspectCharacter _vladCharacter;

    [Tooltip("Where Vlad stands at the start of Day 2. He waits here until a player approaches.")]
    [SerializeField] private Transform _vladSpawnPos;

    [Tooltip("Delivery controller that spawns the supply box. On Day 2 the box is redirected to a unique position.")]
    [SerializeField] private SupplyBoxDeliveryController _supplyBoxDelivery;

    [Tooltip("The position and rotation at which the Day 2 supply box is spawned (overrides the controller's default spawn point).")]
    [SerializeField] private Transform _day2SupplyBoxSpawnPoint;

    [Header("Day 2 — Scripted Dialogues")]
    [Tooltip("4-node intro dialogue. Use cameraTrigger 'SupplyBox' on node 2 and 'SuspectFaceCam' on node 3.")]
    [SerializeField] private ScriptedDialogue _vladIntroDialogue;

    [Tooltip("3-node tool locker dialogue. Last node should be Vlad's ominous closing remark.")]
    [SerializeField] private ScriptedDialogue _vladToolLockerDialogue;

    [Header("Day 2 — Vlad Waypoints")]
    [Tooltip("Vlad walks here first — should be just outside the booth door.")]
    [SerializeField] private Transform _boothDoorWaypoint;

    [Tooltip("Vlad walks here second — beside the tool locker.")]
    [SerializeField] private Transform _toolLockerWaypoint;

    [Tooltip("Vlad stands here after unlocking the tool locker and waits for the player to approach.")]
    [SerializeField] private Transform _vladFinalWaypoint;

    [Tooltip("Vlad walks here to exit after the tool locker dialogue.")]
    [SerializeField] private Transform _vladDespawnWaypoint;

    [Header("Day 2 — Environment")]
    [Tooltip("The booth door Vlad opens as he passes through.")]
    [SerializeField] private DoorController _boothDoor;

    [Tooltip("The padlock on the tool locker. Vlad unlocks it during the walkthrough.")]
    [SerializeField] private LockController _toolLockerLock;

    [Header("Day 2 — Timing")]
    [Tooltip("Radius (world units) within which a player triggers Vlad's intro dialogue.")]
    [SerializeField] private float _introProximityRadius = 3.5f;

    [Tooltip("Radius within which a player triggers the tool locker dialogue at the final waypoint.")]
    [SerializeField] private float _toolLockerProximityRadius = 3.5f;

    [Tooltip("Seconds Vlad pauses after performing the unlock gesture before the lock triggers.")]
    [SerializeField] private float _unlockGestureDuration = 1.5f;

    [Tooltip("Seconds after the tool locker dialogue before Vlad starts walking to his despawn point.")]
    [SerializeField] private float _vladExitDelay = 1.5f;

    // Guards — prevent re-triggering if a player walks in and out of range.
    private bool _introDialogueTriggered;
    private bool _toolLockerDialogueTriggered;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Unlock the mutation exam refill in the tool locker shop for all clients.
        if (NetworkManager.Singleton.IsServer && MegaphoneDialogueManager.Instance != null)
            MegaphoneDialogueManager.Instance.SetShopItemAvailableSynced("Mutation Exams (5)");

        // Hide the mutation notebook until the tutorial beat spawns it in.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);

        // Hide the biological notebook — not introduced until Day 3.
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        _mutationTutorialFired = false;

        // Listen for each suspect's paperwork so we can detect the first with a mutation anomaly.
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawned;

        // Ensure the first booth suspect has at least one mutation anomaly for the tutorial.
        if (NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 1;

        // ── Opening Sequence Setup ──────────────────────────────────────────────

        _introDialogueTriggered       = false;
        _toolLockerDialogueTriggered  = false;

        // Activate and spawn Vlad on the server — NGO propagates to all clients.
        // Uses the same pattern as SuspectController.IntroduceSceneSuspect for scene objects.
        if (NetworkManager.Singleton.IsServer && _vladCharacter != null)
        {
            NetworkObject vladNetObj = _vladCharacter.GetComponent<NetworkObject>();
            if (vladNetObj == null)
            {
                Debug.LogError("[Day_02] _vladCharacter is missing a NetworkObject component — opening sequence will be skipped.", this);
            }
            else
            {
                if (!_vladCharacter.gameObject.activeSelf)
                    _vladCharacter.gameObject.SetActive(true);

                if (!vladNetObj.IsSpawned)
                    vladNetObj.Spawn();

                if (_vladSpawnPos != null)
                {
                    _vladCharacter.transform.position = _vladSpawnPos.position;
                    _vladCharacter.transform.rotation = _vladSpawnPos.rotation;
                }

                _vladCharacter.InitNavigation();
            }
        }

        // Redirect the supply box delivery to the Day 2 unique position.
        // SupplyBoxDeliveryController will consume the override when OnDayStart fires.
        if (NetworkManager.Singleton.IsServer && _supplyBoxDelivery != null && _day2SupplyBoxSpawnPoint != null)
            _supplyBoxDelivery.SpawnPointOverride = _day2SupplyBoxSpawnPoint;

        // Subscribe to ShiftManager so the opening sequence starts when the day officially begins.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDay2Started;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        // Restore biological notebook so Day 3 can manage it normally.
        _biologicalNotebook?.SetVisible(true);
        TutorialMarkerManager.Instance?.UnmarkAll();

        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        ExamNotebook.OnAnyNotebookPageFiled  -= OnNotebookPageFiled;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDay2Started;

        // Clear any unused spawn override so it doesn't bleed into the next day.
        if (_supplyBoxDelivery != null)
            _supplyBoxDelivery.SpawnPointOverride = null;

        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        ExamNotebook.OnAnyNotebookPageFiled  -= OnNotebookPageFiled;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDay2Started;
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Day 2 Opening Sequence
    // -------------------------------------------------------------------------

    private void OnDay2Started()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        ShiftManager.Instance.OnDayStart -= OnDay2Started;
        StartCoroutine(Day2OpeningSequence());
    }

    /// <summary>
    /// Waits for a player to approach Vlad, plays the intro dialogue, then walks Vlad
    /// through the booth and tool locker, and fires the second dialogue when the player
    /// approaches Vlad's final position. Server-side only.
    /// </summary>
    private IEnumerator Day2OpeningSequence()
    {
        if (_vladCharacter == null)
        {
            Debug.LogWarning("[Day_02] _vladCharacter is not assigned — skipping opening sequence.");
            yield break;
        }

        // ── Phase 1: wait for a player to walk up to Vlad ──────────────────────
        yield return StartCoroutine(WaitForPlayerProximity(_vladCharacter.transform, _introProximityRadius));

        if (_introDialogueTriggered) yield break;
        _introDialogueTriggered = true;

        // Small settle beat before dialogue begins.
        yield return new WaitForSeconds(0.5f);

        // ── Phase 2: intro scripted dialogue ───────────────────────────────────
        if (_vladIntroDialogue == null)
        {
            Debug.LogWarning("[Day_02] _vladIntroDialogue not assigned — skipping to waypoint walk.");
        }
        else
        {
            bool introDone = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _vladCharacter,
                _vladIntroDialogue,
                () => introDone = true);
            yield return new WaitUntil(() => introDone);
        }

        // Short beat after dialogue before Vlad starts moving.
        yield return new WaitForSeconds(0.5f);

        // ── Phase 3: Vlad walks to booth door, opens it, continues to tool locker ──
        yield return StartCoroutine(VladWaypointSequence());
    }

    /// <summary>
    /// Moves Vlad through his waypoints, triggering environment interactions along the way.
    /// </summary>
    private IEnumerator VladWaypointSequence()
    {
        // ── Walk to booth door ──────────────────────────────────────────────────
        if (_boothDoorWaypoint != null)
        {
            yield return StartCoroutine(WalkVladTo(_boothDoorWaypoint));

            // Open the booth door.
            if (_boothDoor != null)
            {
                _boothDoor.ForceOpen(openedIn: true);
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ── Walk to tool locker ─────────────────────────────────────────────────
        if (_toolLockerWaypoint != null)
        {
            yield return StartCoroutine(WalkVladTo(_toolLockerWaypoint));

            // Perform an unlock gesture (use the existing "Give" trigger as a stand-in;
            // replace with a dedicated "Unlock" trigger once that animation is authored).
            _vladCharacter.FireAnimatorTrigger("Give");
            yield return new WaitForSeconds(_unlockGestureDuration);

            // Unlock the tool locker padlock.
            _toolLockerLock?.ForceUnlock();
            yield return new WaitForSeconds(0.5f);
        }

        // ── Rotate toward final waypoint and wait ───────────────────────────────
        if (_vladFinalWaypoint != null)
        {
            Vector3 dir = _vladFinalWaypoint.position - _vladCharacter.transform.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                bool rotDone = false;
                _vladCharacter.transform
                    .DORotateQuaternion(Quaternion.LookRotation(dir.normalized), 0.4f)
                    .OnComplete(() => rotDone = true);
                yield return new WaitUntil(() => rotDone);
            }
        }

        // ── Phase 4: wait for a player to approach, then play tool locker dialogue ──
        yield return StartCoroutine(WaitForPlayerProximity(_vladCharacter.transform, _toolLockerProximityRadius));

        if (_toolLockerDialogueTriggered) yield break;
        _toolLockerDialogueTriggered = true;

        yield return new WaitForSeconds(0.3f);

        if (_vladToolLockerDialogue != null)
        {
            bool lockerDone = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _vladCharacter,
                _vladToolLockerDialogue,
                () => lockerDone = true);
            yield return new WaitUntil(() => lockerDone);
        }

        // ── Phase 5: Vlad leaves ────────────────────────────────────────────────
        yield return new WaitForSeconds(_vladExitDelay);

        if (_vladDespawnWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_vladDespawnWaypoint));

        // Despawn Vlad via NGO so all clients deactivate him.
        // destroyGameObject: false — the scene object is preserved so it can be re-spawned later.
        if (_vladCharacter != null)
        {
            NetworkObject vladNetObj = _vladCharacter.GetComponent<NetworkObject>();
            if (vladNetObj != null && vladNetObj.IsSpawned)
                vladNetObj.Despawn(false);
        }

        Debug.Log("[Day_02] Opening sequence complete — Vlad has left.");
    }

    // -------------------------------------------------------------------------
    // Movement helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks Vlad to <paramref name="target"/> using the NavMeshAgent, then settles his
    /// facing to match the waypoint's forward direction. Server-side.
    /// </summary>
    private IEnumerator WalkVladTo(Transform target)
    {
        if (_vladCharacter == null || target == null) yield break;

        _vladCharacter.SetAnimatorBool("Walking", true);

        bool arrived = false;
        _vladCharacter.NavigateTo(target.position, () => arrived = true);
        yield return new WaitUntil(() => arrived);

        _vladCharacter.SetAnimatorBool("Walking", false);

        // Settle facing to the waypoint's exact forward direction.
        if (target.forward.sqrMagnitude > 0.01f)
        {
            bool rotDone = false;
            _vladCharacter.transform
                .DORotateQuaternion(target.rotation, 0.3f)
                .OnComplete(() => rotDone = true);
            yield return new WaitUntil(() => rotDone);
        }
    }

    // -------------------------------------------------------------------------
    // Proximity check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Polls every 0.5 s until any connected player's object is within <paramref name="radius"/>
    /// world units of <paramref name="target"/>. Server-side.
    /// </summary>
    private IEnumerator WaitForPlayerProximity(Transform target, float radius)
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (NetworkManager.Singleton == null) yield break;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                float dist = Vector3.Distance(client.PlayerObject.transform.position, target.position);
                if (dist <= radius)
                    yield break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Suspect arrival — fire tutorial once on the first suspect with a mutation anomaly
    // -------------------------------------------------------------------------

    private void OnPaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (_mutationTutorialFired) return;
        if (!NetworkManager.Singleton.IsServer) return;
        _mutationTutorialFired = true;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        StartCoroutine(MutationExamTutorialSequence());
    }

    // -------------------------------------------------------------------------
    // Tutorial sequence
    // -------------------------------------------------------------------------

    private IEnumerator MutationExamTutorialSequence()
    {
        yield return new WaitForSeconds(4f);

        yield return ShowAndWait("A new type of anomaly has appeared in the field — physical mutations.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mutations are catalogued separately. Use the Mutation Exam notebook to record them.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Pick up the Mutation Exam notebook and mark every mutation you find on this subject.");

        _mutationNotebook?.SetVisible(true);
        _mutationNotebook?.SetInteractableNetworked(true);
        if (_mutationNotebook != null)
            ShowMutationNotebookMarker(true);

        yield return new WaitUntil(() => _mutationNotebook != null && _mutationNotebook.IsHeld);

        if (_mutationNotebook != null)
            ShowMutationNotebookMarker(false);

        yield return MutationCheckBeat();
    }

    // -------------------------------------------------------------------------
    // Checkbox beat
    // -------------------------------------------------------------------------

    private IEnumerator MutationCheckBeat()
    {
        ChecklistItem.AnyBoxChecked = false;

        bool anyBoxChecked = false;
        System.Action<ExamNotebook> onChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += onChecked;

        yield return ShowAndWait("Tick the boxes for every mutation you can identify.");

        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ExamNotebook.OnAnyCheckboxChecked -= onChecked;

        yield return MutationFileIntoBeat();
    }

    // -------------------------------------------------------------------------
    // File notebook into folder beat
    // -------------------------------------------------------------------------

    private IEnumerator MutationFileIntoBeat()
    {
        _notebookPageFiled = false;
        ExamNotebook.AnyPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnNotebookPageFiled;

        yield return ShowAndWait("Good. Now interact with the folder while holding the notebook to file your mutation findings.");

        if (ExamNotebook.AnyPageFiled)
            _notebookPageFiled = true;

        yield return new WaitUntil(() => _notebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnNotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mutations are catalogued separately from documentation anomalies. Each notebook type covers a different threat profile.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Proceed with the remaining subjects. Stay vigilant.");
    }

    private void OnNotebookPageFiled()
    {
        if (this == null) return;
        _notebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Networked Marker helpers
    // -------------------------------------------------------------------------

    private void ShowMutationNotebookMarker(bool show)
    {
        if (_mutationNotebook == null) return;
        NetworkObject netObj = _mutationNotebook.GetComponent<NetworkObject>();
        if (netObj == null) return;
        if (show) MegaphoneDialogueManager.Instance?.ShowMarkerSynced(netObj);
        else      MegaphoneDialogueManager.Instance?.HideMarkerSynced(netObj);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private IEnumerator ShowAndWait(string line)
    {
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);
        MegaphoneDialogueManager.Instance.ShowDialogueSynced(line);
        yield return null;
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);
    }
}
