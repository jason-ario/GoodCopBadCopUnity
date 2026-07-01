using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
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
}
