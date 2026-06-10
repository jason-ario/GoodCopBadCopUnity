using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Server-authoritative controller for the Alexei scripted event on Day 1.
///
/// This component sits on an empty parent GameObject that also holds the PlayableDirector.
/// Alexei's character (with MutantSuspectBehaviour, MutantEnemy, etc.) is a child of that parent.
///
/// Flow:
///   1. SuspectController.InterceptNextSuspectSpawn fires BeginSequence() instead of spawning a guard.
///   2. The murder Timeline plays on all clients — it manages everything including the guard dialogue.
///   3. After the cutscene ends, MutantSuspectBehaviour.BeginLineup() activates on the Alexei character.
///   4. Static events notify Day_01 of the outcome (retreated or broke through).
/// </summary>
public class AlexeiController : NetworkBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static AlexeiController Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Mutant Components")]
    [Tooltip("MutantSuspectBehaviour on the Alexei child object — resolved automatically if left empty.")]
    [SerializeField] private MutantSuspectBehaviour _mutantBehaviour;

    [Tooltip("MutantIntruderData asset controlling Alexei's walk speed, bang count, and timing.")]
    [SerializeField] private MutantIntruderData _alexeiIntruderData;

    [Header("Positions")]
    [Tooltip("World position Alexei walks to before attacking.")]
    [SerializeField] private Transform _standPos;

    [Tooltip("Off-screen position Alexei retreats to after giving up.")]
    [SerializeField] private Transform _despawnPos;

    [Tooltip("Booth-interior landing point used if Alexei climbs through the window.")]
    [SerializeField] private Transform _climbThroughTarget;

    [Header("Murder Cutscene")]
    [Tooltip("The GameObject that holds the PlayableDirector. Starts inactive in the scene and is activated when the sequence begins.")]
    [SerializeField] private GameObject _cutsceneObject;

    [Tooltip("PlayableDirector for the murder Timeline. Manages guard dialogue, Alexei reveal, and murder animation.")]
    [SerializeField] private PlayableDirector _murderCutscene;

    // ── Static Events — subscribed by Day_01 ──────────────────────────────────

    /// <summary>Fired on the server immediately after the murder cutscene ends, before BeginLineup().</summary>
    public static event Action OnMurderComplete;

    /// <summary>Fired on the server when Alexei retreats (window was closed in time).</summary>
    public static event Action OnAlexeiRetreated;

    /// <summary>Fired on the server when Alexei climbs through (MutantEnemy takes over from here).</summary>
    public static event Action OnAlexeiBrokeThrough;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        if (_mutantBehaviour == null)
            _mutantBehaviour = GetComponentInChildren<MutantSuspectBehaviour>();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the full Alexei event sequence. Server-only.
    /// Called by SuspectController.InterceptNextSuspectSpawn when it's the Alexei slot's turn.
    /// </summary>
    public void BeginSequence()
    {
        if (!IsServer) return;
        StartCoroutine(CutsceneSequence());
    }

    // ── Sequence Coroutine (server-only) ──────────────────────────────────────

    private IEnumerator CutsceneSequence()
    {
        if (!IsServer) yield break;

        // Play the murder Timeline on all clients. It manages guard dialogue, Alexei reveal, etc.
        if (_murderCutscene != null)
        {
            PlayCutsceneClientRpc();
            yield return new WaitForSeconds((float)_murderCutscene.duration);
        }
        else
        {
            Debug.LogWarning("[AlexeiController] No murder cutscene assigned — skipping directly to BeginLineup.");
        }

        // Notify Day_01 so it shows the lever prompt.
        OnMurderComplete?.Invoke();

        // Hand off to MutantSuspectBehaviour for all shutter/climb/retreat logic.
        // Null controller means SuspectController.OnMutantIntruderComplete never fires —
        // Day_01.AlexeiRetreatedSequence calls SetNextSuspectReady() instead.
        _mutantBehaviour.OnSequenceComplete = HandleAlexeiComplete;
        _mutantBehaviour.BeginLineup(
            _alexeiIntruderData,
            _standPos,
            _despawnPos,
            _climbThroughTarget,
            ShutterController.Instance,
            controller: null
        );
    }

    // ── Sequence Complete Callback ─────────────────────────────────────────────

    /// <summary>
    /// Called by MutantSuspectBehaviour.OnSequenceComplete. Runs on the server.
    /// </summary>
    private void HandleAlexeiComplete(bool brokeThrough)
    {
        if (brokeThrough)
        {
            // MutantEnemy is now active — it will chase and kill players normally.
            OnAlexeiBrokeThrough?.Invoke();
        }
        else
        {
            // Alexei retreated — despawn the container NetworkObject and notify Day_01.
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();

            OnAlexeiRetreated?.Invoke();
        }
    }

    // ── ClientRpcs ────────────────────────────────────────────────────────────

    /// <summary>Activates the cutscene GameObject and plays the Timeline on every client simultaneously.</summary>
    [ClientRpc]
    private void PlayCutsceneClientRpc()
    {
        _cutsceneObject?.SetActive(true);
        _murderCutscene?.Play();
    }
}
