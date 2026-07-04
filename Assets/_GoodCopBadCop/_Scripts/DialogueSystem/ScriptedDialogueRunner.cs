using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Maps a string key defined in <see cref="ScriptedDialogueNode.cameraTrigger"/> to a
/// scene <see cref="GameObject"/> that contains a Cinemachine camera.
/// Assign entries in the Inspector on the ScriptedDialogueRunner scene object.
/// </summary>
[Serializable]
public class ScriptedCameraEntry
{
    [Tooltip("Key referenced by ScriptedDialogueNode.cameraTrigger (case-sensitive).")]
    public string key;

    [Tooltip("The GameObject containing the CinemachineCamera to activate for this key.")]
    public GameObject cam;
}

/// <summary>
/// Drives a <see cref="ScriptedDialogue"/> sequence end-to-end, synchronising subtitle
/// display, player-choice UI, camera cuts, and text effects across all connected clients.
///
/// Call <see cref="PlayDialogue"/> on the server to start a sequence. The runner:
/// <list type="bullet">
///   <item>Locks the booth player's movement and activates the suspect camera.</item>
///   <item>Plays each node in order using <see cref="DialogueManager"/>.</item>
///   <item>For Monologue nodes: waits for the player to press E or left-click to advance.</item>
///   <item>For Choice nodes: shows two buttons, waits for a pick, then plays the NPC reply.</item>
///   <item>Cuts to the Cinemachine camera named by <see cref="ScriptedDialogueNode.cameraTrigger"/>
///         before each line plays. An empty trigger deactivates any override and returns to the
///         default suspect cam.</item>
///   <item>Applies a vertex-wobble effect to subtitles when
///         <see cref="ScriptedDialogueNode.wobbleText"/> is true.</item>
///   <item>Restores all player state and camera state when the last node finishes.</item>
/// </list>
///
/// Requires a networked scene object that has this component attached. The singleton
/// <see cref="Instance"/> is resolved automatically in <c>Awake</c>.
/// </summary>
public class ScriptedDialogueRunner : NetworkBehaviour
{
    public static ScriptedDialogueRunner Instance { get; private set; }

    [Header("Cutscene Cameras")]
    [Tooltip("Maps camera trigger keys (set on ScriptedDialogueNode) to Cinemachine camera GameObjects. " +
             "An empty trigger deactivates overrides and returns to the default suspect cam.")]
    [SerializeField] private ScriptedCameraEntry[] _cameras;

    [Header("Megaphone Speaker")]
    [Tooltip("Speaker name shown in subtitles when playing megaphone scripted dialogue.")]
    [SerializeField] private string _megaphoneSpeakerName = "Megaphone";

    [Tooltip("Name colour for megaphone subtitles.")]
    [SerializeField] private Color _megaphoneSpeakerColor = new Color(1f, 0.65f, 0f); // orange

    [Header("Wobble")]
    [Tooltip("Default wobble profile applied to every scripted dialogue subtitle. " +
             "Individual nodes can override this with a different profile via wobbleProfileOverride.")]
    [SerializeField] private TMPWobbleProfile _defaultWobbleProfile;

    [Tooltip("Additional profiles that nodes can reference as per-line overrides. " +
             "A node's wobbleProfileOverride must appear in this list to be sent over the network. " +
             "Index 0 here maps to RPC profile index 0 (the default is always RPC index -1).")]
    [SerializeField] private TMPWobbleProfile[] _additionalWobbleProfiles;

    // Set true while coroutines are waiting for player E / left-click input so that
    // Update can send AdvanceDialogueServerRpc on mouse-click in addition to E key.
    private bool _awaitingScriptedInput;

    // Choice submission state — server-side only.
    private bool _choiceReceived;
    private int _pendingChoiceIndex;

    // Cached per ShowChoicesClientRpc so the local-player callback can read the text.
    private string[] _currentChoiceTexts;

    // Tracks the currently active override camera (client-side) so it can be deactivated
    // when the trigger changes or the sequence ends.
    private GameObject _activeOverrideCam;

    // Tracks the last animation trigger fired on the speaker so it can be reset before the
    // next node starts, preventing a fast-skipped trigger from replaying mid-sequence.
    private string _lastAnimTrigger = string.Empty;

    private void Awake() => Instance = this;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays a scripted dialogue with <paramref name="speaker"/> as the NPC.
    /// Must be called on the server. <paramref name="onComplete"/> fires on the server
    /// after the last node completes.
    /// <para>
    /// When <paramref name="deferExit"/> is <c>true</c>, scripted dialogue mode is NOT
    /// exited after the last node — the caller is responsible for calling
    /// <see cref="ExitScriptedMode"/> (or chaining <see cref="PlayMegaphoneDialogue"/>)
    /// when the full sequence is finished.
    /// </para>
    /// </summary>
    public void PlayDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue,
        Action onComplete = null, bool deferExit = false)
    {
        if (!IsServer) return;

        if (speaker == null || dialogue == null ||
            dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunDialogue(speaker, dialogue, onComplete, deferExit));
    }

    /// <summary>
    /// Switches the active override camera to the entry matching <paramref name="key"/>.
    /// An empty key deactivates any active override. Must be called on the server.
    /// </summary>
    public void SwitchCamera(string key)
    {
        if (!IsServer) return;
        SetActiveOverrideCamClientRpc(key ?? string.Empty);
    }

    /// <summary>
    /// Manually exits scripted dialogue mode and deactivates any active override camera.
    /// Use this when a previous <see cref="PlayDialogue"/> call was made with
    /// <c>deferExit: true</c> and no subsequent dialogue method will end the session.
    /// Must be called on the server.
    /// </summary>
    public void ExitScriptedMode()
    {
        if (!IsServer) return;
        ExitScriptedModeClientRpc();
    }

    /// <summary>
    /// Plays a scripted dialogue sequence using the megaphone speaker identity (no NPC character
    /// required). Lines are displayed with the configured megaphone name and colour, audio is
    /// routed through <see cref="MegaphoneDialogueManager"/>, and the player can click to advance
    /// exactly as with character dialogue. Must be called on the server.
    /// </summary>
    public void PlayMegaphoneDialogue(ScriptedDialogue dialogue, Action onComplete = null)
    {
        if (!IsServer) return;

        if (dialogue == null || dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayMegaphoneDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunMegaphoneDialogue(dialogue, onComplete));
    }

    // -------------------------------------------------------------------------
    // Click-to-advance — supplements the E key handled by WaitForInputRoutine
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!_awaitingScriptedInput) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Ignore clicks that land on any UI element (e.g. choice buttons).
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // Two-stage: first click completes the typewriter without skipping the line.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second click (or first when typewriter is already done): advance the sequence.
        DialogueManager.Instance.AdvanceDialogueServerRpc();
    }

    // -------------------------------------------------------------------------
    // Internal sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue,
        Action onComplete, bool deferExit = false)
    {
        ulong speakerNetId = speaker.GetComponent<NetworkObject>().NetworkObjectId;

        Debug.Log($"[ScriptedDialogueRunner] RunDialogue — IsSpawned={IsSpawned}, IsServer={IsServer}, speakerNetId={speakerNetId}, deferExit={deferExit}");

        _lastAnimTrigger = string.Empty;

        EnterScriptedModeClientRpc(speakerNetId);
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            // Camera cut and text effect are set before the line starts so they're
            // visible from the very first word of the subtitle.
            SetActiveOverrideCamClientRpc(node.cameraTrigger ?? string.Empty);
            SetWobbleClientRpc(ResolveWobbleProfileIndex(node.wobbleProfileOverride));

            // Reset the previous trigger and force idle before firing the next animation.
            // This prevents a skipped trigger from continuing to play into the new line.
            ResetAndTriggerAnimationClientRpc(speakerNetId, _lastAnimTrigger, node.animationTrigger);
            _lastAnimTrigger = node.animationTrigger ?? string.Empty;

            yield return StartCoroutine(SayAndWait(speaker, node.npcLine));

            if (node.type == ScriptedDialogueNodeType.Choice)
                yield return StartCoroutine(PlayChoiceNode(speaker, node, speakerNetId));
        }

        // When deferExit is true, scripted mode is kept active so the caller can chain
        // further sequences (e.g. a cutscene followed by megaphone dialogue) before
        // returning control to the player. The caller must eventually call ExitScriptedMode
        // or PlayMegaphoneDialogue (which exits mode when it completes).
        if (!deferExit)
        {
            ExitScriptedModeClientRpc();
            yield return null;
        }

        onComplete?.Invoke();
        Debug.Log($"[ScriptedDialogueRunner] Scripted dialogue complete (deferExit={deferExit}).");
    }

    /// <summary>Says a line and waits for the player to press E / click before continuing.</summary>
    private IEnumerator SayAndWait(SuspectCharacter speaker, string text)
    {
        _awaitingScriptedInput = true;
        DialogueManager.Instance.SayDialogue(speaker, text, waitForInput: true);
        yield return StartCoroutine(DialogueManager.Instance.WaitForInputRoutine());
        _awaitingScriptedInput = false;
    }

    // -------------------------------------------------------------------------
    // Megaphone sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunMegaphoneDialogue(ScriptedDialogue dialogue, Action onComplete)
    {
        EnterScriptedModeClientRpc(0UL); // no NPC speaker for megaphone dialogue
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            SetActiveOverrideCamClientRpc(node.cameraTrigger ?? string.Empty);
            SetWobbleClientRpc(ResolveWobbleProfileIndex(node.wobbleProfileOverride));
            yield return StartCoroutine(SayMegaphoneLineAndWait(node.npcLine));
            SetSpeakerSpeakingClientRpc(false);
        }

        ExitScriptedModeClientRpc();
        yield return null;

        onComplete?.Invoke();
        Debug.Log("[ScriptedDialogueRunner] Megaphone dialogue complete.");
    }

    private IEnumerator SayMegaphoneLineAndWait(string text)
    {
        _awaitingScriptedInput = true;
        SayMegaphoneLineClientRpc(text);
        yield return StartCoroutine(DialogueManager.Instance.WaitForInputRoutine());
        _awaitingScriptedInput = false;
    }

    /// <summary>
    /// Displays a subtitle line using the megaphone speaker identity and plays its audio
    /// through <see cref="MegaphoneDialogueManager"/>. Uses the standard dialogue audio slot
    /// so the player clicking to advance stops the audio, matching character dialogue behaviour.
    /// </summary>
    [ClientRpc]
    private void SayMegaphoneLineClientRpc(string text)
    {
        var mgr = MegaphoneDialogueManager.Instance;
        AudioClip[] clips = mgr != null ? mgr.AudioClips : System.Array.Empty<AudioClip>();
        AudioSource source = mgr != null ? mgr.MegaphoneAudioSource : null;

        mgr?.SetSpeakerSpeaking(true);

        DialogueManager.Instance.SpawnSubtitles(text, _megaphoneSpeakerName, _megaphoneSpeakerColor,
            isPlayer: false, clearHistory: false, waitForInput: true);

        if (clips.Length > 0 && source != null)
            DialogueManager.Instance.PlayDialogueAudio(text, clips, source);
    }

    [ClientRpc]
    private void SetSpeakerSpeakingClientRpc(bool speaking)
    {
        MegaphoneDialogueManager.Instance?.SetSpeakerSpeaking(speaking);
    }

    private IEnumerator PlayChoiceNode(SuspectCharacter speaker, ScriptedDialogueNode node, ulong speakerNetId)
    {
        if (node.choices == null || node.choices.Length < 2)
        {
            Debug.LogWarning("[ScriptedDialogueRunner] Choice node requires at least 2 choices. Skipping.");
            yield break;
        }

        _choiceReceived = false;
        _pendingChoiceIndex = -1;
        ShowChoicesClientRpc(node.choices[0].playerChoiceText, node.choices[1].playerChoiceText);

        yield return new WaitUntil(() => _choiceReceived);

        // Fire the response animation, then deliver the NPC's unique reply.
        var chosen = node.choices[_pendingChoiceIndex];
        ResetAndTriggerAnimationClientRpc(speakerNetId, _lastAnimTrigger, chosen.animationTrigger);
        _lastAnimTrigger = chosen.animationTrigger ?? string.Empty;
        yield return StartCoroutine(SayAndWait(speaker, chosen.npcResponse));
    }

    // -------------------------------------------------------------------------
    // Client RPCs — mode
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void EnterScriptedModeClientRpc(ulong speakerNetId)
    {
        Debug.Log($"[ScriptedDialogueRunner] EnterScriptedModeClientRpc — " +
                  $"PlayerInstance={PlayerInstance.Instance != null}, " +
                  $"IsOutsideLocal={PlayerInstance.Instance?.IsOutsideLocal}, " +
                  $"ChoiceSystemInstance={DialogueChoiceSystem.Instance != null}");

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;

        // Resolve the speaker's transform so the player rotates to face the booth.
        Transform lookTarget = null;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            lookTarget = netObj.transform;

        DialogueChoiceSystem.Instance.EnterScriptedDialogueMode(lookTarget);
    }

    [ClientRpc]
    private void ExitScriptedModeClientRpc()
    {
        // Deactivate any override camera before restoring the default cam state.
        DeactivateOverrideCam();

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;
        DialogueChoiceSystem.Instance.ExitScriptedDialogueMode();
    }

    // -------------------------------------------------------------------------
    // Client RPCs — camera
    // -------------------------------------------------------------------------

    /// <summary>
    /// Activates the camera mapped to <paramref name="key"/> and deactivates the previous
    /// override. An empty key simply deactivates any active override, returning to the
    /// default suspect cam.
    /// </summary>
    [ClientRpc]
    private void SetActiveOverrideCamClientRpc(string key)
    {
        // Always deactivate the current override first.
        if (_activeOverrideCam != null)
        {
            _activeOverrideCam.SetActive(false);
            _activeOverrideCam = null;
        }

        if (string.IsNullOrEmpty(key)) return;

        if (_cameras == null) return;
        foreach (var entry in _cameras)
        {
            if (entry.key == key && entry.cam != null)
            {
                entry.cam.SetActive(true);
                _activeOverrideCam = entry.cam;
                return;
            }
        }

        Debug.LogWarning($"[ScriptedDialogueRunner] No camera entry found for key '{key}'.");
    }

    private void DeactivateOverrideCam()
    {
        if (_activeOverrideCam == null) return;
        _activeOverrideCam.SetActive(false);
        _activeOverrideCam = null;
    }

    // -------------------------------------------------------------------------
    // Client RPCs — text effects
    // -------------------------------------------------------------------------

    /// <summary>
    /// Primes <see cref="DialogueManager"/> with the wobble profile to use for the next subtitle.
    /// <paramref name="profileIndex"/> of -1 resolves to the default profile; 0+ indexes into
    /// <see cref="_additionalWobbleProfiles"/>.
    /// </summary>
    [ClientRpc]
    private void SetWobbleClientRpc(int profileIndex)
    {
        DialogueManager.Instance.SetNextLineWobbleProfile(ResolveWobbleProfileByIndex(profileIndex));
    }

    /// <summary>
    /// Server-side: maps a node's optional override profile to an RPC-safe index.
    /// Returns -1 for the default (null or matching <see cref="_defaultWobbleProfile"/>),
    /// or the index within <see cref="_additionalWobbleProfiles"/> for a registered override.
    /// Logs a warning and falls back to -1 if the override is not registered.
    /// </summary>
    private int ResolveWobbleProfileIndex(TMPWobbleProfile overrideProfile)
    {
        if (overrideProfile == null || overrideProfile == _defaultWobbleProfile)
            return -1;

        if (_additionalWobbleProfiles != null)
        {
            for (int i = 0; i < _additionalWobbleProfiles.Length; i++)
            {
                if (_additionalWobbleProfiles[i] == overrideProfile)
                    return i;
            }
        }

        Debug.LogWarning($"[ScriptedDialogueRunner] wobbleProfileOverride '{overrideProfile.name}' is not registered " +
                         $"in _additionalWobbleProfiles. Falling back to default profile.");
        return -1;
    }

    /// <summary>
    /// Client-side: resolves an RPC profile index back to the corresponding <see cref="TMPWobbleProfile"/>.
    /// -1 returns <see cref="_defaultWobbleProfile"/>; 0+ indexes into <see cref="_additionalWobbleProfiles"/>.
    /// </summary>
    private TMPWobbleProfile ResolveWobbleProfileByIndex(int index)
    {
        if (index < 0)
            return _defaultWobbleProfile;

        if (_additionalWobbleProfiles != null && index < _additionalWobbleProfiles.Length)
            return _additionalWobbleProfiles[index];

        Debug.LogWarning($"[ScriptedDialogueRunner] Wobble profile index {index} is out of range. Falling back to default.");
        return _defaultWobbleProfile;
    }

    // -------------------------------------------------------------------------
    // Client RPCs — choices
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void ShowChoicesClientRpc(string choice0, string choice1)
    {
        _currentChoiceTexts = new[] { choice0, choice1 };

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;

        DialogueChoiceSystem.Instance.ShowScriptedChoices(_currentChoiceTexts, OnLocalPlayerPickedChoice);
    }

    private void OnLocalPlayerPickedChoice(int choiceIndex)
    {
        string playerName = GetLocalPlayerName();
        string choiceText = _currentChoiceTexts != null ? _currentChoiceTexts[choiceIndex] : string.Empty;

        // SpawnSubtitles also logs to DialogueHistoryManager internally.
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.cyan, isPlayer: true);

        SubmitScriptedChoiceServerRpc(choiceIndex, playerName, choiceText);
    }

    // -------------------------------------------------------------------------
    // Server RPCs — choice submission
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sent by the local player after selecting a choice.
    /// The server broadcasts the player's line to all other clients and advances the sequence.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitScriptedChoiceServerRpc(
        int choiceIndex, string playerName, string choiceText,
        ServerRpcParams rpcParams = default)
    {
        if (_choiceReceived) return; // only the first submission wins

        BroadcastPlayerChoiceClientRpc(choiceText, playerName, rpcParams.Receive.SenderClientId);
        _pendingChoiceIndex = choiceIndex;
        _choiceReceived = true;
    }

    [ClientRpc]
    private void BroadcastPlayerChoiceClientRpc(string choiceText, string playerName, ulong senderClientId)
    {
        // The sender already showed their own subtitle locally in OnLocalPlayerPickedChoice.
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.cyan, isPlayer: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resets <paramref name="previousTrigger"/> on the speaker's Animator (so a fast-skipped
    /// trigger can't replay into the next line), forces the Animator back to idle via the
    /// <c>ForceIdle</c> trigger, then — if non-empty — fires <paramref name="newTrigger"/>.
    /// All three steps run in one RPC so they're always applied atomically on every client.
    /// </summary>
    [ClientRpc]
    private void ResetAndTriggerAnimationClientRpc(ulong speakerNetId, string previousTrigger, string newTrigger)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            return;

        var character = netObj.GetComponent<SuspectCharacter>();
        if (character?.animator == null) return;

        var anim = character.animator;

        // 1. Reset the previous trigger so it can't fire again on a state re-entry.
        if (!string.IsNullOrEmpty(previousTrigger))
            anim.ResetTrigger(previousTrigger);

        // 2. Force return to idle — ensures a skipped animation doesn't bleed into the next line.
        anim.SetTrigger("ForceIdle");

        // 3. Set the new trigger (may be empty for lines with no specific animation).
        if (!string.IsNullOrEmpty(newTrigger))
            anim.SetTrigger(newTrigger);
    }

    private string GetLocalPlayerName()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
            return Steamworks.SteamClient.Name;

        return $"Player {NetworkManager.Singleton.LocalClientId}";
    }
}
