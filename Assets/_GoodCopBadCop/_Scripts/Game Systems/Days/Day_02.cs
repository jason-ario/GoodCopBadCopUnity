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
    public static Day_02 Instance { get; private set; }

    private void Awake() => Instance = this;

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
    [Tooltip("Vlad's SuspectCharacter prefab. Instantiated at day start and destroyed after the opening sequence.")]
    [SerializeField] private SuspectCharacter _vladCharacter;

    // Runtime instance spawned from the prefab. Null when not in-world.
    private SuspectCharacter _spawnedVlad;

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
    [SerializeField] private float _introProximityRadius = 35f;

    [Tooltip("Radius within which a player triggers the tool locker dialogue at the final waypoint.")]
    [SerializeField] private float _toolLockerProximityRadius = 35f;

    [Tooltip("Seconds Vlad pauses after performing the unlock gesture before the lock triggers.")]
    [SerializeField] private float _unlockGestureDuration = 1.5f;

    [Tooltip("Seconds after the tool locker dialogue before Vlad starts walking to his despawn point.")]
    [SerializeField] private float _vladExitDelay = 1.5f;

    // Guards — prevent re-triggering if a player walks in and out of range.
    private bool _introDialogueTriggered;
    private bool _toolLockerDialogueTriggered;

    // Set by DebugSkipOpening to bypass Day2OpeningSequence entirely.
    private bool _debugSkipOpening;

    // -------------------------------------------------------------------------
    // Day 2 Post-Shift Vlad Sequence (Out Back)
    // -------------------------------------------------------------------------

    [Header("Day 2 — Post-Shift Vlad (Out Back)")]
    [Tooltip("Vlad's SuspectCharacter prefab for the out-back sequence. Instantiated when the shift ends and destroyed after the sequence.")]
    [SerializeField] private SuspectCharacter _vladOutBackCharacter;

    // Runtime instance spawned from the prefab. Null when not in-world.
    private SuspectCharacter _spawnedVladOutBack;

    [Tooltip("Where Vlad stands when waiting out back. He teleports here when the shift ends.")]
    [SerializeField] private Transform _vladOutBackSpawnPos;

    [Tooltip("Vlad walks here first — in front of the exterior gate.")]
    [SerializeField] private Transform _vladGateWaypoint;

    [Tooltip("Vlad walks here second — beside the dead animal.")]
    [SerializeField] private Transform _vladDeadAnimalWaypoint;

    [Tooltip("Vlad's final standing position before he despawns.")]
    [SerializeField] private Transform _vladOutBackFinalWaypoint;

    [Tooltip("Vlad walks here to exit/despawn after the dead animal dialogue.")]
    [SerializeField] private Transform _vladOutBackDespawnWaypoint;

    [Tooltip("The padlock on the exterior gate. Vlad unlocks it with the Give gesture.")]
    [SerializeField] private LockController _exteriorLock;

    [Tooltip("The exterior gate that opens after Vlad unlocks the padlock.")]
    [SerializeField] private GateController _exteriorGate;

    [Tooltip("Transform used to determine the direction Vlad rotates toward when pointing at the dead animal.")]
    [SerializeField] private Transform _deadAnimalFacingTarget;

    [Tooltip("Root GameObject of the dead animal prop. Disabled by default; activated at the start of the out-back sequence.")]
    [SerializeField] private GameObject _deadAnimalObject;

    [Header("Day 2 — Ocho")]
    [Tooltip("Ocho prefab. Must contain a NetworkObject and OchoWatcherBehaviour. " +
             "Must be registered in NetworkManager's Network Prefabs list.")]
    [SerializeField] private GameObject _ochoPrefab;

    [Tooltip("Where Ocho spawns — position him deep in the tree line near the power plant, " +
             "far enough to read as a distant silhouette.")]
    [SerializeField] private Transform _ochoSpawnPoint;

    [Tooltip("Transform Ocho runs toward when fleeing. Place this off-screen behind the tree line " +
             "at a point that sits on the NavMesh.")]
    [SerializeField] private Transform _ochoFleeDestination;

    [Tooltip("Distance (metres) at which any player triggers Ocho to flee.")]
    [SerializeField] private float _ochoFleeRadius = 8f;

    [Tooltip("NavMeshAgent speed while Ocho is fleeing.")]
    [SerializeField] private float _ochoFleeSpeed = 20f;

    [Tooltip("Degrees-per-second at which Ocho rotates to face the nearest player while watching.")]
    [SerializeField] private float _ochoWatchRotateSpeed = 60f;

    [Tooltip("Stinger sound effect played on all clients the moment Ocho begins fleeing.")]
    [SerializeField] private AudioClip _ochoFleeStinger;

    // Runtime Ocho instance. Tracked so DayDeactivated can clean up mid-sequence.
    private NetworkObject _spawnedOcho;

    [Header("Day 2 — Trail Event")]
    [Tooltip("Index into FollowTrailThreat._possibleLocations to use for the Day 2 trail. " +
             "That location's TrailController should have its first waypoint placed at the dead animal. " +
             "Leave CorpsePoint unassigned — _deadAnimalObject already serves as the corpse. " +
             "Set PackSpawner and PackSize on the location to configure the enemy pack.")]
    [SerializeField] private int _day2TrailLocationIndex = 0;

    [Header("Day 2 — Post-Shift Scripted Dialogues")]
    [Tooltip("Brief intro dialogue played when the player first approaches Vlad outside.")]
    [SerializeField] private ScriptedDialogue _vladOutBackIntroDialogue;

    [Tooltip("First part of the dead animal dialogue — one node: 'There's been a lot of these...'. " +
             "Set cameraTrigger to your DeadAnimal camera key. Vlad rotates toward the animal before this plays.")]
    [SerializeField] private ScriptedDialogue _vladDeadAnimalPart1Dialogue;

    [Tooltip("Second part of the dead animal dialogue — flashlight UV mode, blood trails, follow the trail, gun, good luck. " +
             "Set cameraTrigger to your Gun camera key on the gun-related nodes.")]
    [SerializeField] private ScriptedDialogue _vladDeadAnimalPart2Dialogue;

    [Header("Day 2 — Post-Shift Timing")]
    [Tooltip("Radius (world units) within which a player triggers Vlad's out-back intro dialogue.")]
    [SerializeField] private float _outBackProximityRadius = 35f;

    [Tooltip("Seconds Vlad pauses after the Give gesture before the lock and gate respond.")]
    [SerializeField] private float _outBackUnlockGestureDuration = 1.5f;

    [Tooltip("Seconds between the dead animal dialogue completing and Vlad starting to walk away.")]
    [SerializeField] private float _outBackVladExitDelay = 1.5f;

    [Tooltip("Seconds for DOTween rotation when Vlad turns to face the dead animal (and back).")]
    [SerializeField] private float _vladAnimalFacingTweenDuration = 0.5f;

    // Post-shift guards.
    private bool _outBackIntroDialogueTriggered;
    private bool _outBackAnimalDialogueTriggered;

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

        _introDialogueTriggered      = false;
        _toolLockerDialogueTriggered = false;
        _debugSkipOpening            = false;
        _spawnedVlad                 = null;

        if (_vladCharacter == null)
            Debug.LogWarning("[Day_02] _vladCharacter prefab not assigned — opening sequence will be skipped.", this);

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

        // Clear the trail destination override so other days use default behaviour.
        if (FollowTrailThreat.Instance != null)
            FollowTrailThreat.Instance.OnDestinationDiscoveredOverride = null;

        // Clear all out-back task NetworkVariables so every client's HUD is clean.
        // The NetworkVariable setters are server-only; clients have their own local TaskRegistry
        // copies that will update when the NetworkVariable changes propagate.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer
            && FollowTrailThreat.Instance != null)
        {
            FollowTrailThreat.Instance.SetMeetVladActive(false);
            FollowTrailThreat.Instance.SetFollowTrailTaskActive(false);
            FollowTrailThreat.Instance.SetKillMutantActive(false);
        }

        // Local fallback — if the NetworkVariable propagation hasn't fired yet on this client,
        // remove any lingering task objects directly. Safe to call when Current is already null.
        MeetVladOutBackTask.CompleteAndRemove();
        KillMutantTask.CompleteAndRemove();

        // Destroy any Vlad instances that are still in-world (e.g. day skipped mid-sequence).
        DespawnVladInstance(ref _spawnedVlad);
        DespawnVladInstance(ref _spawnedVladOutBack);

        // Despawn Ocho if still present (e.g. player never approached him).
        DespawnOcho();

        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        ExamNotebook.OnAnyNotebookPageFiled  -= OnNotebookPageFiled;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDay2Started;
    }

    public override void ShiftEnded()
    {
        base.ShiftEnded();
        if (!NetworkManager.Singleton.IsServer) return;
        StartCoroutine(PostShiftSetupSequence());
    }

    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Debug / Skip helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Suppresses the Day 2 opening Vlad sequence so it can be skipped by the F12 cheat menu.
    /// Also unlocks the tool locker (which Vlad would normally unlock mid-sequence) to keep
    /// game state consistent. Server-only; call before TryStartShift.
    /// </summary>
    public void DebugSkipOpening()
    {
        _debugSkipOpening            = true;
        _introDialogueTriggered      = true;
        _toolLockerDialogueTriggered = true;

        // Unlock the tool locker so it isn't still padlocked when the player is in-world.
        _toolLockerLock?.ForceUnlock();

        Debug.Log("[Day_02] DebugSkipOpening: opening sequence suppressed, tool locker unlocked.");
    }

    // -------------------------------------------------------------------------
    // Day 2 Opening Sequence
    // -------------------------------------------------------------------------

    private void OnDay2Started()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        ShiftManager.Instance.OnDayStart -= OnDay2Started;
        if (_debugSkipOpening) return;
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
            Debug.LogWarning("[Day_02] _vladCharacter prefab not assigned — skipping opening sequence.");
            yield break;
        }

        // Instantiate Vlad from the prefab and network-spawn him at the opening position.
        Vector3    spawnPos = _vladSpawnPos != null ? _vladSpawnPos.position : Vector3.zero;
        Quaternion spawnRot = _vladSpawnPos != null ? _vladSpawnPos.rotation : Quaternion.identity;
        _spawnedVlad = Instantiate(_vladCharacter, spawnPos, spawnRot);

        NetworkObject vladNetObj = _spawnedVlad.GetComponent<NetworkObject>();
        if (vladNetObj == null)
        {
            Debug.LogError("[Day_02] Vlad prefab is missing a NetworkObject — opening sequence aborted.", this);
            Destroy(_spawnedVlad.gameObject);
            _spawnedVlad = null;
            yield break;
        }

        vladNetObj.Spawn(destroyWithScene: true);
        _spawnedVlad.InitNavigation();

        // ── Phase 1: wait for a player to walk up to Vlad ──────────────────────
        yield return StartCoroutine(WaitForPlayerProximity(_spawnedVlad.transform, _introProximityRadius));

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
                _spawnedVlad,
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
            _spawnedVlad.FireAnimatorTrigger("Give");
            yield return new WaitForSeconds(_unlockGestureDuration);

            // Unlock the tool locker padlock.
            _toolLockerLock?.ForceUnlock();
            yield return new WaitForSeconds(0.5f);
        }

        // ── Rotate toward final waypoint and wait ───────────────────────────────
        if (_vladFinalWaypoint != null)
        {
            Vector3 dir = _vladFinalWaypoint.position - _spawnedVlad.transform.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                bool rotDone = false;
                _spawnedVlad.transform
                    .DORotateQuaternion(Quaternion.LookRotation(dir.normalized), 0.4f)
                    .OnComplete(() => rotDone = true);
                yield return new WaitUntil(() => rotDone);
            }
        }

        // ── Phase 4: wait for a player to approach, then play tool locker dialogue ──
        yield return StartCoroutine(WaitForPlayerProximity(_spawnedVlad.transform, _toolLockerProximityRadius));

        if (_toolLockerDialogueTriggered) yield break;
        _toolLockerDialogueTriggered = true;

        yield return new WaitForSeconds(0.3f);

        if (_vladToolLockerDialogue != null)
        {
            bool lockerDone = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _spawnedVlad,
                _vladToolLockerDialogue,
                () => lockerDone = true);
            yield return new WaitUntil(() => lockerDone);
        }

        // ── Phase 5: Vlad leaves ────────────────────────────────────────────────
        yield return new WaitForSeconds(_vladExitDelay);

        if (_vladDespawnWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_vladDespawnWaypoint));

        // Despawn and destroy the runtime instance — it was created from a prefab.
        DespawnVladInstance(ref _spawnedVlad);

        Debug.Log("[Day_02] Opening sequence complete — Vlad has left.");
    }

    // -------------------------------------------------------------------------
    // Movement helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks the opening-sequence Vlad instance to <paramref name="target"/>. Delegates to the
    /// generic overload so both sequences share the same movement logic.
    /// </summary>
    private IEnumerator WalkVladTo(Transform target)
    {
        yield return StartCoroutine(WalkVladTo(_spawnedVlad, target));
    }

    /// <summary>
    /// Walks any <see cref="SuspectCharacter"/> to <paramref name="target"/> using the NavMeshAgent,
    /// then settles facing to the waypoint's forward direction. Server-side.
    /// </summary>
    private IEnumerator WalkVladTo(SuspectCharacter character, Transform target)
    {
        if (character == null || target == null) yield break;

        character.SetAnimatorBool("Walking", true);

        bool arrived = false;
        character.NavigateTo(target.position, () => arrived = true);
        yield return new WaitUntil(() => arrived);

        character.SetAnimatorBool("Walking", false);

        // Settle facing to the waypoint's exact forward direction.
        if (target.forward.sqrMagnitude > 0.01f)
        {
            bool rotDone = false;
            character.transform
                .DORotateQuaternion(target.rotation, 0.3f)
                .OnComplete(() => rotDone = true);
            yield return new WaitUntil(() => rotDone);
        }
    }

    // -------------------------------------------------------------------------
    // Proximity check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Despawns a runtime Vlad prefab instance (destroyGameObject: true) and clears the ref.
    /// Safe to call when the instance is null or already despawned.
    /// </summary>
    private static void DespawnVladInstance(ref SuspectCharacter instance)
    {
        if (instance == null) return;

        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
        else if (instance.gameObject != null)
            UnityEngine.Object.Destroy(instance.gameObject);

        instance = null;
    }

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

    // =========================================================================
    // Post-Shift Vlad Out-Back Sequence
    // =========================================================================

    /// <summary>
    /// Entry point called from <see cref="ShiftEnded"/>. Registers the "Meet Vlad" task,
    /// plays the megaphone bark, spawns Vlad outside, then hands off to the main coroutine.
    /// Server-side only.
    /// </summary>
    private IEnumerator PostShiftSetupSequence()
    {
        // Register the HUD task and bark simultaneously.
        // SetMeetVladActive sets a NetworkVariable on FollowTrailThreat → fires on all clients.
        FollowTrailThreat.Instance?.SetMeetVladActive(true);

        yield return ShowAndWait("Good work today. Meet me out back by the bunker. I have a special task for you.");

        // Instantiate Vlad from the prefab at the out-back spawn position and network-spawn him.
        if (_vladOutBackCharacter == null)
        {
            Debug.LogError("[Day_02] _vladOutBackCharacter prefab not assigned — post-shift sequence aborted.", this);
            yield break;
        }

        Vector3    spawnPos = _vladOutBackSpawnPos != null ? _vladOutBackSpawnPos.position : Vector3.zero;
        Quaternion spawnRot = _vladOutBackSpawnPos != null ? _vladOutBackSpawnPos.rotation : Quaternion.identity;
        _spawnedVladOutBack = Instantiate(_vladOutBackCharacter, spawnPos, spawnRot);

        NetworkObject vladNetObj = _spawnedVladOutBack.GetComponent<NetworkObject>();
        if (vladNetObj == null)
        {
            Debug.LogError("[Day_02] _vladOutBackCharacter prefab is missing a NetworkObject — post-shift sequence aborted.", this);
            Destroy(_spawnedVladOutBack.gameObject);
            _spawnedVladOutBack = null;
            yield break;
        }

        vladNetObj.Spawn(destroyWithScene: true);
        _spawnedVladOutBack.InitNavigation();

        _outBackIntroDialogueTriggered  = false;
        _outBackAnimalDialogueTriggered = false;

        StartCoroutine(PostShiftVladSequence());
    }

    /// <summary>
    /// Drives Vlad through the full out-back sequence: intro approach → gate unlock → dead animal
    /// dialogue → despawn → trail event handoff. Server-side only.
    /// </summary>
    private IEnumerator PostShiftVladSequence()
    {
        if (_spawnedVladOutBack == null)
        {
            Debug.LogWarning("[Day_02] _spawnedVladOutBack is null — skipping post-shift sequence.");
            yield break;
        }

        // Reveal the dead animal prop as soon as the out-back sequence begins.
        _deadAnimalObject?.SetActive(true);

        // ── Phase 1: wait for player to approach Vlad outside ─────────────────
        yield return StartCoroutine(WaitForPlayerProximity(_spawnedVladOutBack.transform, _outBackProximityRadius));

        if (_outBackIntroDialogueTriggered) yield break;
        _outBackIntroDialogueTriggered = true;

        // Remove the "Meet Vlad" HUD task on all clients via the NetworkVariable.
        FollowTrailThreat.Instance?.SetMeetVladActive(false);

        yield return new WaitForSeconds(0.5f);

        // ── Phase 2: intro scripted dialogue ──────────────────────────────────
        if (_vladOutBackIntroDialogue != null)
        {
            bool introDone = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _spawnedVladOutBack,
                _vladOutBackIntroDialogue,
                () => introDone = true,
                lockOutsidePlayers: true);
            yield return new WaitUntil(() => introDone);
        }

        yield return new WaitForSeconds(0.5f);

        // ── Phase 3: Vlad walks to gate, unlocks padlock, opens gate ──────────
        if (_vladGateWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_spawnedVladOutBack, _vladGateWaypoint));

        // Give animation (stand-in for unlock gesture; swap trigger name when dedicated anim exists).
        _spawnedVladOutBack.FireAnimatorTrigger("Give");
        yield return new WaitForSeconds(_outBackUnlockGestureDuration);

        // Unlock padlock and open gate.
        _exteriorLock?.ForceUnlock();
        yield return new WaitForSeconds(0.3f);
        _exteriorGate?.ForceOpen(openedIn: false);

        yield return new WaitForSeconds(0.5f);

        // ── Phase 4: Vlad walks to dead animal waypoint ───────────────────────
        if (_vladDeadAnimalWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_spawnedVladOutBack, _vladDeadAnimalWaypoint));

        // ── Phase 5: wait for player to approach Vlad at the dead animal ──────
        yield return StartCoroutine(WaitForPlayerProximity(_spawnedVladOutBack.transform, _outBackProximityRadius));

        if (_outBackAnimalDialogueTriggered) yield break;
        _outBackAnimalDialogueTriggered = true;

        yield return new WaitForSeconds(0.3f);

        // ── Phase 6: dead animal dialogue — Part 1 ────────────────────────────
        // Capture Vlad's current facing so we can restore it after Part 1.
        Quaternion vladFacingBeforeAnimal = _spawnedVladOutBack.transform.rotation;

        // Rotate Vlad toward the dead animal on the Y axis.
        if (_deadAnimalFacingTarget != null)
        {
            Vector3 toAnimal = _deadAnimalFacingTarget.position - _spawnedVladOutBack.transform.position;
            toAnimal.y = 0f;
            if (toAnimal.sqrMagnitude > 0.01f)
            {
                bool rotDone = false;
                _spawnedVladOutBack.transform
                    .DORotateQuaternion(Quaternion.LookRotation(toAnimal.normalized), _vladAnimalFacingTweenDuration)
                    .OnComplete(() => rotDone = true);
                yield return new WaitUntil(() => rotDone);
            }
        }

        // Play part 1 — "There's been a lot of these popping up since the incident."
        // Node should have cameraTrigger set to your DeadAnimal camera key.
        if (_vladDeadAnimalPart1Dialogue != null)
        {
            bool part1Done = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _spawnedVladOutBack,
                _vladDeadAnimalPart1Dialogue,
                () => part1Done = true,
                lockOutsidePlayers: true);
            yield return new WaitUntil(() => part1Done);
        }

        // ── Phase 7: rotate Vlad back to his original facing ──────────────────
        {
            bool rotBackDone = false;
            _spawnedVladOutBack.transform
                .DORotateQuaternion(vladFacingBeforeAnimal, _vladAnimalFacingTweenDuration)
                .OnComplete(() => rotBackDone = true);
            yield return new WaitUntil(() => rotBackDone);
        }

        yield return new WaitForSeconds(0.3f);

        // ── Phase 8: dead animal dialogue — Part 2 ────────────────────────────
        // Nodes cover: flashlight UV mode, blood trails, follow the trail,
        // camera pan to gun (cameraTrigger "Gun"), "Take that gun", "Good luck!".
        if (_vladDeadAnimalPart2Dialogue != null)
        {
            bool part2Done = false;
            ScriptedDialogueRunner.Instance.PlayDialogue(
                _spawnedVladOutBack,
                _vladDeadAnimalPart2Dialogue,
                () => part2Done = true,
                lockOutsidePlayers: true);
            yield return new WaitUntil(() => part2Done);
        }

        // ── Phase 9: activate trail immediately after Vlad's last line ────────
        // This fires before Vlad walks away so the task is live even if
        // his exit navigation stalls.
        ActivateDay2TrailEvent();

        // ── Phase 10: Vlad exits ──────────────────────────────────────────────
        yield return new WaitForSeconds(_outBackVladExitDelay);

        if (_vladOutBackFinalWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_spawnedVladOutBack, _vladOutBackFinalWaypoint));

        if (_vladOutBackDespawnWaypoint != null)
            yield return StartCoroutine(WalkVladTo(_spawnedVladOutBack, _vladOutBackDespawnWaypoint));

        // Despawn and destroy the runtime instance — it was created from a prefab.
        DespawnVladInstance(ref _spawnedVladOutBack);

        Debug.Log("[Day_02] Post-shift Vlad sequence complete — Vlad has left.");
    }

    /// <summary>
    /// Registers the Follow Trail HUD task on all clients, sets the Day 2 resolution override,
    /// and spawns the UV blood trail. Called immediately after Vlad's last dialogue line so the
    /// task is live regardless of what happens during his exit walk. Server only.
    /// </summary>
    private void ActivateDay2TrailEvent()
    {
        if (FollowTrailThreat.Instance == null)
        {
            Debug.LogError("[Day_02] FollowTrailThreat.Instance is null — trail event cannot be activated.", this);
            return;
        }

        // Show "Follow the trail" in every client's HUD via the NetworkVariable.
        FollowTrailThreat.Instance.SetFollowTrailTaskActive(true);

        // Pack spawning and kill task activation are handled by FollowTrailThreat when the
        // destination is discovered, using PackSpawner/PackSize on the FollowTrailLocation.
        // The override just needs to start KillMutantSequence to wait for all kills.
        FollowTrailThreat.Instance.OnDestinationDiscoveredOverride = () =>
        {
            StartCoroutine(KillMutantSequence());
        };

        // Spawn corpse (if assigned), trail particles, and destination interactable.
        FollowTrailThreat.Instance.TriggerTrailEvent(_day2TrailLocationIndex);

        // Spawn Ocho in the tree line as soon as the trail task goes live.
        SpawnOcho();

        Debug.Log("[Day_02] Trail event activated.");
    }

    /// <summary>
    /// Waits for <see cref="KillMutantTask.OnKillMutantTaskCompleted"/> then advances the night
    /// phase. The Kill Mutant task itself was registered on all clients by the
    /// <see cref="FollowTrailThreat"/> NetworkVariable in the destination discovered callback.
    /// Server-side only.
    /// </summary>
    private IEnumerator KillMutantSequence()
    {
        bool mutantKilled = false;
        KillMutantTask.OnKillMutantTaskCompleted += OnDay2MutantKilled;

        void OnDay2MutantKilled()
        {
            KillMutantTask.OnKillMutantTaskCompleted -= OnDay2MutantKilled;
            mutantKilled = true;
        }

        yield return new WaitUntil(() => mutantKilled);

        // Clear the trail override so future days use default behaviour.
        if (FollowTrailThreat.Instance != null)
            FollowTrailThreat.Instance.OnDestinationDiscoveredOverride = null;

        // Advance the night phase — lights up the "start next shift" button.
        BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();

        Debug.Log("[Day_02] Mutant killed — night phase ready.");
    }

    // -------------------------------------------------------------------------
    // Ocho
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instantiates and network-spawns Ocho at <see cref="_ochoSpawnPoint"/>.
    /// Ocho's <see cref="OchoWatcherBehaviour"/> handles proximity detection, fleeing,
    /// and self-despawn — no further orchestration is needed here.
    /// Server only.
    /// </summary>
    private void SpawnOcho()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (_ochoPrefab == null)
        {
            Debug.LogWarning("[Day_02] _ochoPrefab is not assigned — Ocho sighting skipped.", this);
            return;
        }

        Vector3    spawnPos = _ochoSpawnPoint != null ? _ochoSpawnPoint.position : transform.position;
        Quaternion spawnRot = _ochoSpawnPoint != null ? _ochoSpawnPoint.rotation : Quaternion.identity;

        GameObject instance = Instantiate(_ochoPrefab, spawnPos, spawnRot);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[Day_02] _ochoPrefab is missing a NetworkObject — Ocho not spawned.", this);
            Destroy(instance);
            return;
        }

        // Add OchoWatcherBehaviour at runtime and configure it with scene-owned values
        // before Spawn() fires OnNetworkSpawn. AddComponent is used so the prefab stays
        // clean — no need to bake the component onto the asset.
        AudioSource ochoAudio = instance.AddComponent<AudioSource>();
        ochoAudio.playOnAwake   = false;
        ochoAudio.spatialBlend  = 0f; // 2D — stinger plays flat on all clients

        OchoWatcherBehaviour ochoWatcher = instance.AddComponent<OchoWatcherBehaviour>();
        ochoWatcher.Initialise(
            fleeDestination:  _ochoFleeDestination,
            fleeRadius:       _ochoFleeRadius,
            fleeSpeed:        _ochoFleeSpeed,
            watchRotateSpeed: _ochoWatchRotateSpeed,
            audioSource:      ochoAudio,
            fleeSound:        _ochoFleeStinger
        );

        netObj.Spawn(destroyWithScene: true);
        _spawnedOcho = netObj;

        Debug.Log("[Day_02] Ocho spawned.", this);
    }

    /// <summary>
    /// Despawns the Ocho instance if it is still live.
    /// Safe to call when no instance exists.
    /// </summary>
    private void DespawnOcho()
    {
        if (_spawnedOcho != null && _spawnedOcho.IsSpawned)
            _spawnedOcho.Despawn(destroy: true);

        _spawnedOcho = null;
    }
}
