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

    // Fallback timeout in case the PlayableDirector's stopped event never fires.
    private const float CutsceneTimeoutSeconds = 120f;

    private bool _cutsceneFinished;

    private void Awake() => Instance = this;

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
        var director = _cutsceneObject != null
            ? _cutsceneObject.GetComponent<PlayableDirector>()
            : null;

        _cutsceneFinished = false;

        if (director != null)
            director.stopped += OnCutsceneDirectorStopped;

        // Activate directly — this works regardless of network spawn state.
        // The ClientRpc then syncs the activation to any connected remote clients.
        _cutsceneObject?.SetActive(true);
        if (IsSpawned)
            ActivateCutsceneClientRpc();

        Debug.Log($"[AlexeiController] Cutscene GO activated. Director found: {director != null}, IsSpawned: {IsSpawned}");

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

        onCutsceneDone?.Invoke();
    }

    private void OnCutsceneDirectorStopped(PlayableDirector _) => _cutsceneFinished = true;

    [ClientRpc]
    private void ActivateCutsceneClientRpc()
    {
        _cutsceneObject?.SetActive(true);
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
    /// Enables clock-out via the normal ShiftManager path (timecard machine, fanfare, and
    /// "clock out ready" megaphone line), and registers the TrashThreat in the
    /// <see cref="GuidebookTaskRegistry"/> on all clients so it appears in the HUD task list.
    /// Server-only.
    /// </summary>
    public void TriggerEndOfShiftSetup()
    {
        if (!IsServer) return;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.DebugEnableClockOut();
        else
            Debug.LogWarning("[AlexeiController] TriggerEndOfShiftSetup: ShiftManager.Instance not found.");

        RegisterTrashThreatClientRpc();
    }

    /// <summary>Adds TrashThreat to GuidebookTaskRegistry on every client so it shows in the HUD task list.</summary>
    [ClientRpc]
    private void RegisterTrashThreatClientRpc()
    {
        if (TrashThreat.Instance != null)
            GuidebookTaskRegistry.Instance.AddThreat(TrashThreat.Instance);
        else
            Debug.LogWarning("[AlexeiController] RegisterTrashThreatClientRpc: TrashThreat.Instance not found.");
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

        NavMeshAgent agent     = instance.GetComponent<NavMeshAgent>();
        MutantSuspectBehaviour msb = instance.GetComponent<MutantSuspectBehaviour>();
        MutantEnemy mutantEnemy    = instance.GetComponent<MutantEnemy>();

        // The NavMeshAgent snaps to the nearest NavMesh surface during Awake (inside Instantiate).
        // Disable it immediately and restore the intended aerial position before spawning.
        if (agent != null) agent.enabled = false;
        instance.transform.SetPositionAndRotation(_mutantSpawnPos.position, _mutantSpawnPos.rotation);

        netObj.Spawn(true);

        // Start the encounter music on all clients as soon as Alexei is in the world.
        // Play locally first (works offline / host), then broadcast to remote clients.
        if (_alexeiMusicClip != null)
        {
            MusicManager.Instance?.Play(_alexeiMusicClip);
            if (IsSpawned)
                StartAlexeiMusicClientRpc();
        }

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

        // Play the landing impact sound at the booth window position.
        if (_landingSound != null && SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(_landingSound, _mutantBoothPos.position, _landingSoundVolume);

        // Re-enable the NavMeshAgent now that the mutant is on the ground.
        if (agent != null) agent.enabled = true;

        Debug.Log("[AlexeiController] Mutant landed — idling.");

        yield return new WaitForSeconds(_idleAfterLandSeconds);

        // Notify Day_01 to play the lever megaphone dialogue.
        onMutantIdle?.Invoke();

        // Pause before activating the attack threat so players have time to react to the dialogue.
        yield return new WaitForSeconds(_behaviourActivationDelay);

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
