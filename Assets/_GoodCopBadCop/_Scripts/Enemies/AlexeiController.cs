using System;
using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

/// <summary>
/// Server-authoritative controller for the Alexei scripted event on Day 1.
/// Activates the murder cutscene GameObject on all clients when <see cref="BeginSequence"/>
/// is called. The PlayableDirector on that object must have Play On Awake enabled.
/// </summary>
public class AlexeiController : NetworkBehaviour
{
    public static AlexeiController Instance { get; private set; }

    [Header("Murder Cutscene")]
    [Tooltip("The GameObject that holds the PlayableDirector. Starts inactive; activating it triggers Play On Awake.")]
    [SerializeField] private GameObject _cutsceneObject;

    [Tooltip("The At Booth Cam (Shaky) CinemachineCamera (child of ---Suspect Controller). " +
             "Activated on all clients when the cutscene starts and deactivated once the director " +
             "stops (by which point the lever megaphone cam has taken over).")]
    [SerializeField] private GameObject _boothShakyCam;

    [Header("Mutant Entrance")]
    [Tooltip("Networked Mutant_Alexei prefab to spawn. Must be registered in the NetworkManager's prefab list.")]
    [SerializeField] private GameObject _mutantPrefab;

    [Tooltip("Transform above the booth window where the mutant spawns in the air.")]
    [SerializeField] private Transform _mutantSpawnPos;

    [Tooltip("Transform at booth window level — the fall destination and stand position.")]
    [SerializeField] private Transform _mutantBoothPos;

    [Tooltip("Transform the mutant retreats to if it gives up or is driven back.")]
    [SerializeField] private Transform _mutantDespawnPos;

    [Tooltip("Transform inside the booth the mutant moves toward if it successfully climbs through.")]
    [SerializeField] private Transform _mutantClimbThroughPos;

    [Tooltip("Behaviour config for attack timing, climb duration, bang count, etc.")]
    [SerializeField] private MutantIntruderData _mutantData;

    [Header("Mutant Entrance Timing")]
    [Tooltip("Seconds for the fall tween from spawn pos to booth pos.")]
    [SerializeField] private float _fallDuration = 1.5f;

    [Tooltip("Seconds to idle at the booth window after landing before activating attack behaviour.")]
    [SerializeField] private float _idleAfterLandSeconds = 2f;

    [Tooltip("Seconds between the onMutantIdle callback (megaphone dialogue) and activating MutantSuspectBehaviour.")]
    [SerializeField] private float _behaviourActivationDelay = 3f;
    public float BehaviourActivationDelay => _behaviourActivationDelay;

    [Tooltip("Seconds between roar taunts while Alexei is idling at the booth window.")]
    [SerializeField] private float _roarInterval = 4f;

    [Header("Mutant Entrance Animations")]
    [Tooltip("Animator bool parameter name for the grounded state. False = falling, True = grounded (triggers landing).")]
    [SerializeField] private string _groundedAnimBool = "Grounded";

    [Header("Mutant Entrance Audio")]
    [Tooltip("Sound played at the booth window position the moment Alexei lands.")]
    [SerializeField] private AudioClip _landingSound;

    [Tooltip("Volume of the landing impact sound.")]
    [SerializeField] private float _landingSoundVolume = 1f;

    [Header("Mutant Music")]
    [Tooltip("Music clip that plays on all clients when Alexei spawns.")]
    [SerializeField] private AudioClip _alexeiMusicClip;

    [Tooltip("Seconds over which the music fades out after Alexei despawns.")]
    [SerializeField] private float _musicFadeOutDuration = 3f;

    [Header("Dead Soldier")]
    [Tooltip("SuspectCharacter on Suspect_Soldier (Day 1). Must have a JunkItem component attached " +
             "and kept disabled in the Inspector — TriggerEndOfShiftSetup calls EnableJunkPickup() " +
             "on all clients once the post-Alexei dialogue ends, making the body collectible.")]
    [SerializeField] private SuspectCharacter _soldierBodySuspect;

    [Header("Booth Door")]
    [Tooltip("DoorController on the Booth Door (door1 child). Unlocked and forced open on all clients " +
             "at the start of the end-of-shift setup sequence, matching the dialogue 'I'm unlocking the door for you'.")]
    [SerializeField] private DoorController _boothDoor;

    // Fallback timeout in case the PlayableDirector's stopped event never fires.
    private const float CutsceneTimeoutSeconds = 120f;

    private bool _cutsceneFinished;

    // ── Network state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Set to true by the server the moment the cutscene is triggered.
    /// NGO replicates this as part of the NetworkObject spawn payload, so clients
    /// that are already connected receive it on the next network tick and late-joining
    /// clients receive it the instant the NetworkObject spawns for them — no missed-window
    /// race condition possible.
    /// </summary>
    private readonly NetworkVariable<bool> _cutsceneActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Authoritative server time (NetworkManager.ServerTime.Time) recorded at the moment
    /// the cutscene was triggered. Clients use this to seek the PlayableDirector forward by
    /// the propagation delay so all machines stay frame-accurate.
    /// </summary>
    private readonly NetworkVariable<double> _cutsceneStartServerTime = new(
        0.0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake() => Instance = this;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _cutsceneActive.OnValueChanged += OnCutsceneActiveChanged;

        // Late-joiner / reconnect catch-up: if the cutscene was already triggered
        // before this client spawned the NetworkObject, activate immediately.
        if (_cutsceneActive.Value)
            ActivateCutsceneLocally(_cutsceneStartServerTime.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _cutsceneActive.OnValueChanged -= OnCutsceneActiveChanged;
    }

    // ── Attack behaviour gate ──────────────────────────────────────────────────

    /// <summary>
    /// Set to true by <see cref="ActivateAttackBehaviour"/> once the player has dismissed
    /// the lever-megaphone dialogue. <see cref="MutantEntranceSequence"/> waits on this before
    /// starting <see cref="MutantSuspectBehaviour"/> so the attack only begins after the player
    /// has been instructed to use the lever.
    /// </summary>
    private bool _attackBehaviourRequested;

    /// <summary>
    /// Signals the mutant entrance sequence to activate attack behaviour. Call this from the
    /// lever-megaphone dialogue's onComplete so Alexei only starts climbing after the player
    /// has finished reading the instruction. Server-only.
    /// </summary>
    public void ActivateAttackBehaviour()
    {
        if (!IsServer) return;
        _attackBehaviourRequested = true;
    }

    /// <summary>
    /// Ends the cinematic phase of the Alexei sequence on all clients: deactivates the shaky
    /// booth camera and resets the cutscene flag. Call this as soon as the scripted action phase
    /// begins (e.g. when the mutant idles and the player is released to pull the lever), rather
    /// than waiting for the PlayableDirector to stop. Server-only.
    /// </summary>
    public void EndCinematicPhase()
    {
        if (!IsServer) return;
        if (_cutsceneActive.Value)
            _cutsceneActive.Value = false;
    }

    /// <summary>
    /// Activates the cutscene GameObject on all clients, triggering Play On Awake.
    /// <paramref name="onCutsceneDone"/> fires on the server when the director stops.
    /// Server-only.
    /// </summary>
    public void BeginSequence(Action onCutsceneDone = null)
    {
        if (!IsServer) return;
        StartCoroutine(CutsceneSequence(onCutsceneDone));
    }

    private IEnumerator CutsceneSequence(Action onCutsceneDone)
    {
        // Grab the director before activating so we can subscribe to stopped first.
        // GetComponent works on inactive GameObjects.
        var director = _cutsceneObject != null
            ? _cutsceneObject.GetComponent<PlayableDirector>()
            : null;

        _cutsceneFinished = false;

        if (director != null)
            director.stopped += OnCutsceneDirectorStopped;

        // Record the server time first, then flip the active flag.
        // On the server, OnCutsceneActiveChanged fires synchronously (same frame),
        // so _cutsceneStartServerTime is already set when ActivateCutsceneLocally runs.
        // On clients, both NetworkVariable updates travel in the same network message
        // and are applied before OnValueChanged fires.
        _cutsceneStartServerTime.Value = NetworkManager.ServerTime.Time;
        _cutsceneActive.Value = true;

        Debug.Log($"[AlexeiController] Cutscene triggered. Director found: {director != null}, IsSpawned: {IsSpawned}");

        float elapsed = 0f;
        while (!_cutsceneFinished && elapsed < CutsceneTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (director != null)
            director.stopped -= OnCutsceneDirectorStopped;

        if (elapsed >= CutsceneTimeoutSeconds)
            Debug.LogWarning($"[AlexeiController] Cutscene timed out after {CutsceneTimeoutSeconds}s.");
        else
            Debug.Log($"[AlexeiController] Cutscene finished after {elapsed:F2}s.");

        // Reset the flag so OnCutsceneActiveChanged(_, false) fires on all clients,
        // deactivating _boothShakyCam. By this point the lever megaphone dialogue's
        // camera has already taken over, so the visual transition is seamless.
        _cutsceneActive.Value = false;

        onCutsceneDone?.Invoke();
    }

    private void OnCutsceneDirectorStopped(PlayableDirector _) => _cutsceneFinished = true;

    private void OnCutsceneActiveChanged(bool previous, bool current)
    {
        if (current)
            ActivateCutsceneLocally(_cutsceneStartServerTime.Value);
        else
            _boothShakyCam?.SetActive(false);
    }

    /// <summary>
    /// Activates the cutscene GameObject locally and — on clients — seeks the
    /// PlayableDirector forward by the network propagation delay so all machines
    /// stay frame-accurate with the server's timeline.
    /// </summary>
    private void ActivateCutsceneLocally(double serverStartTime)
    {
        if (_cutsceneObject == null)
        {
            Debug.LogWarning("[AlexeiController] ActivateCutsceneLocally: _cutsceneObject is not assigned.");
            return;
        }

        _cutsceneObject.SetActive(true);
        _boothShakyCam?.SetActive(true);

        // Clients seek the director to compensate for propagation delay.
        // The server's director is at time 0 when this fires (OnNetworkSpawn callback
        // also runs after SetActive, so playOnAwake has not yet started).
        if (!IsServer)
        {
            var director = _cutsceneObject.GetComponent<PlayableDirector>();
            if (director != null)
            {
                double elapsed = NetworkManager.ServerTime.Time - serverStartTime;
                if (elapsed > 0.0 && elapsed < director.duration)
                    director.time = elapsed;
            }
        }

        Debug.Log($"[AlexeiController] ActivateCutsceneLocally complete — IsServer={IsServer}");
    }

    // ── Music / SFX client RPCs ────────────────────────────────────────────────

    [ClientRpc]
    private void PlayLandingSoundClientRpc(Vector3 position)
    {
        // Host already played the sound locally — remote clients only.
        if (IsServer) return;
        if (_landingSound == null) return;
        SFXController.Instance?.PlayAtPosition(_landingSound, position, _landingSoundVolume);
    }

    private IEnumerator DelayedStartAlexeiMusic(float delay)
    {
        yield return new WaitForSeconds(delay);
        MusicManager.Instance?.Play(_alexeiMusicClip);
        if (IsSpawned)
            StartAlexeiMusicClientRpc();
    }

    [ClientRpc]
    private void StartAlexeiMusicClientRpc()
    {
        // Host already played locally — only remote clients need to act here.
        if (IsServer) return;
        if (_alexeiMusicClip == null)
        {
            Debug.LogWarning("[AlexeiController] StartAlexeiMusicClientRpc: _alexeiMusicClip is null on this client.");
            return;
        }
        if (MusicManager.Instance == null)
        {
            Debug.LogWarning("[AlexeiController] StartAlexeiMusicClientRpc: MusicManager.Instance is null.");
            return;
        }
        MusicManager.Instance.Play(_alexeiMusicClip);
    }

    [ClientRpc]
    private void FadeAlexeiMusicClientRpc()
    {
        // Host already faded locally — only remote clients need to act here.
        if (IsServer) return;
        if (MusicManager.Instance == null)
        {
            Debug.LogWarning("[AlexeiController] FadeAlexeiMusicClientRpc: MusicManager.Instance is null.");
            return;
        }
        MusicManager.Instance.FadeOut(_musicFadeOutDuration);
    }

    // ── End-of-shift setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Called on the server once the post-Alexei megaphone dialogue completes.
    /// Enables the soldier body as a collectible JunkItem, and triggers the TakeOutTrashTask
    /// and CleanGraffitiTask so they appear in the HUD and register themselves as pending
    /// daily tasks with <see cref="ShiftManager"/>. Clock-out (and the timecard machine's
    /// fanfare) only becomes available once <see cref="ShiftManager"/> confirms every pending
    /// daily task — including these two — has actually been completed. Server-only.
    /// </summary>
    public void TriggerEndOfShiftSetup()
    {
        Debug.Log($"[AlexeiController] TriggerEndOfShiftSetup called. IsServer={IsServer}, IsSpawned={IsSpawned}");
        if (!IsServer) return;
        EndOfShiftSetupSequence();
    }

    private void EndOfShiftSetupSequence()
    {
        Debug.Log("[AlexeiController] EndOfShiftSetupSequence — beginning end-of-shift setup.");

        // Enable the soldier body as collectible junk on all clients.
        if (_soldierBodySuspect != null)
            _soldierBodySuspect.EnableJunkPickup();
        else
            Debug.LogWarning("[AlexeiController] EndOfShiftSetupSequence: _soldierBodySuspect is not assigned.");

        // Unlock and open the booth door on all clients — matches "I'm unlocking the door for you".
        if (_boothDoor != null)
        {
            _boothDoor.Unlock();
            _boothDoor.ForceOpen();
        }
        else
            Debug.LogWarning("[AlexeiController] EndOfShiftSetupSequence: _boothDoor is not assigned.");

        // Trigger TakeOutTrashTask to spawn all task items immediately. TriggerTask registers
        // itself with ShiftManager as a pending daily task (see TakeOutTrashTask.RegisterPendingDailyTask
        // usage), so the timecard machine stays un-primed until every item is turned in.
        // Junk items are NOT highlighted here — Day_01.OnTrashBagGrabbedSync defers that until
        // the player actually grabs their first bag from the Trash Spawner dispenser, so the
        // tutorial arrow points them to the dispenser first.
        if (TakeOutTrashTask.Instance != null)
        {
            Debug.Log($"[AlexeiController] Calling TakeOutTrashTask.TriggerTask(). IsServer={IsServer}");
            TakeOutTrashTask.Instance.TriggerTask();
        }
        else
        {
            Debug.LogWarning("[AlexeiController] EndOfShiftSetupSequence: TakeOutTrashTask.Instance is null.");
        }

        // Also trigger CleanGraffitiTask alongside the trash task so graffiti appears
        // for the player to scrub during the same end-of-shift/night phase. It likewise
        // registers itself as a pending daily task with ShiftManager.
        if (CleanGraffitiTask.Instance != null)
        {
            Debug.Log($"[AlexeiController] Calling CleanGraffitiTask.TriggerDailyTask(). IsServer={IsServer}");
            CleanGraffitiTask.Instance.TriggerDailyTask();
        }
        else
        {
            Debug.LogWarning("[AlexeiController] EndOfShiftSetupSequence: CleanGraffitiTask.Instance is null.");
        }

        // The tasks above have already registered themselves as pending (if they exist), so
        // marking suspects complete now cannot prime the timecard machine early — ShiftManager
        // waits for both this flag and an empty pending-task set before enabling clock-out.
        ShiftManager.Instance?.MarkSuspectsComplete();
    }

    // ── Mutant Entrance ────────────────────────────────────────────────────────

    /// <summary>
    /// Callback invoked on the server when the mutant has landed and is idling at the booth window.
    /// Set this before the cutscene plays so a Timeline <see cref="SignalReceiver"/> can call
    /// <see cref="TriggerMutantEntrance"/> without needing to pass parameters.
    /// </summary>
    public Action OnMutantIdleCallback { get; set; }

    /// <summary>
    /// Callback invoked on the server when the mutant has finished banging on the shutters and despawned.
    /// Set this before the cutscene plays so Day_01 can chain the post-Alexei megaphone dialogue.
    /// </summary>
    public Action OnAlexeiSequenceDone { get; set; }

    /// <summary>
    /// Parameterless entry point for the mutant entrance sequence intended to be wired to a
    /// Timeline SignalReceiver. Delegates to <see cref="BeginMutantEntrance"/> using
    /// <see cref="OnMutantIdleCallback"/> as the idle callback.
    /// Server-only.
    /// </summary>
    public void TriggerMutantEntrance() => BeginMutantEntrance(OnMutantIdleCallback);

    /// <summary>
    /// Spawns the Alexei mutant at the aerial spawn position, tweens a falling descent to the
    /// booth window with fall/land animations, then activates <see cref="MutantSuspectBehaviour"/>
    /// after a configurable delay.
    /// <paramref name="onMutantIdle"/> fires on the server once the mutant has landed and is idling —
    /// use this moment to trigger the megaphone lever-close instruction in Day_01.
    /// Server-only.
    /// </summary>
    public void BeginMutantEntrance(Action onMutantIdle = null)
    {
        if (!IsServer) return;
        StartCoroutine(MutantEntranceSequence(onMutantIdle));
    }

    private IEnumerator MutantEntranceSequence(Action onMutantIdle)
    {
        if (_mutantPrefab == null || _mutantSpawnPos == null || _mutantBoothPos == null)
        {
            Debug.LogWarning("[AlexeiController] MutantEntranceSequence: missing prefab or position references — invoking callback immediately.");
            onMutantIdle?.Invoke();
            yield break;
        }

        // Spawn at the aerial position.
        GameObject instance = Instantiate(_mutantPrefab, _mutantSpawnPos.position, _mutantSpawnPos.rotation);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[AlexeiController] Mutant prefab is missing a NetworkObject component.");
            Destroy(instance);
            onMutantIdle?.Invoke();
            yield break;
        }

        NavMeshAgent agent          = instance.GetComponent<NavMeshAgent>();
        MutantSuspectBehaviour msb  = instance.GetComponent<MutantSuspectBehaviour>();
        MutantEnemy mutantEnemy     = instance.GetComponent<MutantEnemy>();

        // The NavMeshAgent snaps to the nearest NavMesh surface during Awake (inside Instantiate).
        // Disable it immediately and restore the intended aerial position before spawning.
        if (agent != null) agent.enabled = false;
        instance.transform.SetPositionAndRotation(_mutantSpawnPos.position, _mutantSpawnPos.rotation);

        // Keep MutantEnemy dormant (IsActive stays false) through the fall/land/idle/roar entrance —
        // mirrors the normal lineup-mutant pattern (SuspectController/SuspectCharacter) so other
        // systems that gate on IsActive (e.g. SoldierMutantResponder) don't aggro on Alexei before
        // MutantSuspectBehaviour.BeginAtStandPos() hands off real control via InitialiseServer().
        // Must be called before Spawn() — InitialiseServer() would otherwise run during it.
        mutantEnemy?.DisableAutoInit();

        netObj.Spawn(true);

        // Start the encounter music on all clients after a short delay.
        // Play locally first (works offline / host), then broadcast to remote clients.
        if (_alexeiMusicClip != null)
            StartCoroutine(DelayedStartAlexeiMusic(1.5f));

        // Suspend AI immediately after spawn — ChaseLoop's first frame yield means this
        // call wins the race and prevents the mutant from targeting players during the entrance.
        mutantEnemy?.SuspendForLineup();

        Debug.Log("[AlexeiController] Mutant spawned at air pos — beginning fall.");

        // Not grounded — broadcasts Grounded = false so the falling animation plays.
        msb?.SetAnimBool(_groundedAnimBool, false);

        // Tween downward with gravity-like ease.
        bool fallDone = false;
        instance.transform
            .DOMove(_mutantBoothPos.position, _fallDuration)
            .SetEase(Ease.InQuad)
            .OnComplete(() => fallDone = true);

        yield return new WaitUntil(() => fallDone);

        // Now grounded — the animator transitions from fall to land, then roars.
        msb?.SetAnimBool(_groundedAnimBool, true);
        msb?.TriggerAnim("Roar");

        // Play the landing impact sound at the booth window position on all clients.
        if (_landingSound != null && SFXController.Instance != null)
        {
            SFXController.Instance.PlayAtPosition(_landingSound, _mutantBoothPos.position, _landingSoundVolume);
            if (IsSpawned)
                PlayLandingSoundClientRpc(_mutantBoothPos.position);
        }

        // Re-enable the NavMeshAgent now that the mutant is on the ground.
        if (agent != null) agent.enabled = true;

        Debug.Log("[AlexeiController] Mutant landed — idling.");

        yield return new WaitForSeconds(_idleAfterLandSeconds);

        // Notify Day_01 to play the lever megaphone dialogue.
        _attackBehaviourRequested = false;
        onMutantIdle?.Invoke();

        // Wait for Day_01 to confirm the player has finished the lever dialogue before
        // starting the attack, roaring periodically to taunt the players.
        float roarTimer = _roarInterval;
        while (!_attackBehaviourRequested)
        {
            roarTimer -= Time.deltaTime;
            if (roarTimer <= 0f)
            {
                msb?.TriggerAnim("Roar");
                roarTimer = _roarInterval;
            }
            yield return null;
        }

        if (msb == null || _mutantData == null)
        {
            Debug.LogWarning("[AlexeiController] MutantEntranceSequence: missing MutantSuspectBehaviour or MutantIntruderData — cannot start attack behaviour.");
            yield break;
        }

        Debug.Log("[AlexeiController] Activating mutant suspect behaviour.");

        msb.DespawnInsteadOfRetreat = true;
        msb.OnSequenceComplete = _ =>
        {
            OnAlexeiSequenceDone?.Invoke();
            // Fade locally first, then broadcast to remote clients.
            MusicManager.Instance?.FadeOut(_musicFadeOutDuration);
            if (IsSpawned)
                FadeAlexeiMusicClientRpc();
        };
        msb.BeginAtStandPos(
            _mutantData,
            _mutantBoothPos,
            _mutantDespawnPos,
            _mutantClimbThroughPos,
            ShutterController.Instance,
            null   // Alexei is not a regular suspect slot — no SuspectController needed.
        );
    }
}
