using System;
using System.Collections;
using System.Collections.Generic;
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
///   <item>For Monologue nodes: waits for all connected players to press E or click before
///         advancing. After the first advance a <see cref="_advanceTimeoutSeconds"/> countdown
///         starts; the sequence continues automatically when the timer expires.</item>
///   <item>For Choice nodes: collects a choice from every connected player (same timeout after
///         the first submission). Unanimous picks win outright; conflicting picks are resolved
///         by a random draw from the submitted options only.</item>
///   <item>Cuts to the Cinemachine camera named by <see cref="ScriptedDialogueNode.cameraTrigger"/>
///         before each line plays. An empty trigger deactivates any override and returns to the
///         default suspect cam.</item>
///   <item>Applies a vertex-wobble effect to subtitles when configured.</item>
///   <item>Restores all player state and camera state when the last node finishes.</item>
/// </list>
///
/// Requires a networked scene object that has this component attached. The singleton
/// <see cref="Instance"/> is resolved automatically in <c>Awake</c>.
/// </summary>
public class ScriptedDialogueRunner : NetworkBehaviour
{
    public static ScriptedDialogueRunner Instance { get; private set; }

    /// <summary>
    /// True on all clients while a scripted dialogue sequence is active.
    /// Used by <see cref="DialogueManager.WaitForInputRoutine"/> to route E-key advances
    /// through <see cref="AdvanceScriptedLineServerRpc"/> instead of the standard advance RPC.
    /// </summary>
    public static bool IsScriptedModeActive { get; private set; }

    [Header("Cutscene Cameras")]
    [Tooltip("Maps camera trigger keys (set on ScriptedDialogueNode) to Cinemachine camera GameObjects. " +
             "An empty trigger deactivates overrides and returns to the default suspect cam.")]
    [SerializeField] private ScriptedCameraEntry[] _cameras;

    [Header("Megaphone Speaker")]
    [Tooltip("Speaker name shown in subtitles when playing megaphone scripted dialogue.")]
    [SerializeField] private string _megaphoneSpeakerName = "Megaphone";

    [Tooltip("Name colour for megaphone subtitles.")]
    [SerializeField] private Color _megaphoneSpeakerColor = new Color(1f, 0.65f, 0f);

    [Header("Wobble")]
    [Tooltip("Default wobble profile applied to every scripted dialogue subtitle. " +
             "Individual nodes can override this with a different profile via wobbleProfileOverride.")]
    [SerializeField] private TMPWobbleProfile _defaultWobbleProfile;

    [Tooltip("Additional profiles that nodes can reference as per-line overrides. " +
             "A node's wobbleProfileOverride must appear in this list to be sent over the network. " +
             "Index 0 here maps to RPC profile index 0 (the default is always RPC index -1).")]
    [SerializeField] private TMPWobbleProfile[] _additionalWobbleProfiles;

    [Header("Multi-Player Advance")]
    [Tooltip("Seconds after the first player advances (or submits a choice) before the " +
             "sequence continues automatically without the second player's input.")]
    [SerializeField] private float _advanceTimeoutSeconds = 1.5f;

    [Header("Proximity Join")]
    [Tooltip("Distance (world units) from the speaker within which a player is automatically " +
             "included as a required participant in the advance and choice gates. Players who " +
             "walk into range mid-dialogue are late-joined: their movement locks and suspect " +
             "cam activates, and they are added to the gate from the next line onward.")]
    [SerializeField] private float _joinRadius = 5f;

    // -------------------------------------------------------------------------
    // Server-side state — advance gate
    // -------------------------------------------------------------------------

    private readonly HashSet<ulong> _advanceSet = new();
    private bool _scriptedAdvanceReady;
    private Coroutine _advanceTimerCoroutine;

    // -------------------------------------------------------------------------
    // Server-side state — choice gate
    // -------------------------------------------------------------------------

    private readonly Dictionary<ulong, int> _choiceSubmissions = new();
    private readonly Dictionary<ulong, string> _choicePlayerNames = new();
    private bool _choiceResolved;
    private int _resolvedChoiceIndex;
    private Coroutine _choiceTimerCoroutine;

    // -------------------------------------------------------------------------
    // Server-side state — participant tracking
    // -------------------------------------------------------------------------

    // The set of clientIds who are actively participating in the current dialogue.
    // Seeded at dialogue start by proximity; grows when a player walks into range mid-dialogue.
    // The advance and choice gates use this count instead of ConnectedClientsIds.Count so that
    // a distant player never blocks a line that only the nearby player needs to advance.
    private readonly HashSet<ulong> _participants = new();

    // Transform of the current NPC speaker — used for server-side proximity checks.
    private Transform _currentSpeakerTransform;

    // Throttle for the per-frame proximity scan — removed; cost is negligible for 2 players.

    // -------------------------------------------------------------------------
    // Client-side state
    // -------------------------------------------------------------------------

    // Set true on ALL clients (via ClientRpc) while the server coroutine is waiting
    // for input. Replaces the server-only _awaitingScriptedInput flag for Update() checks
    // so non-host clients can also skip reveals and advance.
    private bool _clientIsWaitingForInput;

    // Server-only: tracks whether any coroutine is currently inside a SayAndWait gate.
    // Used by AdvanceScriptedLineServerRpc to reject stale advances.
    private bool _awaitingScriptedInput;

    // Cached per ShowChoicesClientRpc so local-player callbacks can read the text.
    private string[] _currentChoiceTexts;

    // Tracks the currently active override camera (client-side) so it can be deactivated
    // when the trigger changes or the sequence ends.
    private GameObject _activeOverrideCam;

    // Tracks the last animation trigger fired on the speaker so it can be reset before the
    // next node starts, preventing a fast-skipped trigger from replaying mid-sequence.
    private string _lastAnimTrigger = string.Empty;

    // Tracks the most recently activated camera key on the server so late-joining clients
    // can receive a camera catch-up RPC before LateJoinClientRpc fires.
    private string _currentCameraKey = string.Empty;

    // NetworkObjectId of the current dialogue speaker — set on all clients by EnterScriptedModeClientRpc.
    // Used by SuspectController.ResolveCurrentDialogueSpeakerCam() to find the speaker's per-character cameras.
    // Reset to 0 when exiting scripted mode.
    private ulong _clientSpeakerNetId;

    /// <summary>
    /// NetworkObjectId of the current dialogue speaker on all clients. Zero when no dialogue is active.
    /// Read by <see cref="SuspectController"/> to resolve the speaker's per-character cameras.
    /// </summary>
    public ulong CurrentSpeakerNetId => _clientSpeakerNetId;

    private void Awake() => Instance = this;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="ClientRpcParams"/> that targets only the current
    /// <see cref="_participants"/> set. Must only be called on the server.
    /// </summary>
    private ClientRpcParams BuildParticipantRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new System.Collections.Generic.List<ulong>(_participants)
            }
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the player owned by <paramref name="clientId"/> is an
    /// "inside" player — i.e. their <see cref="PlayerInstance.IsOutside"/> flag is false.
    /// Inside players are always in the booth and should always participate in dialogue,
    /// regardless of their exact distance from the speaker.
    /// </summary>
    private bool IsClientInsidePlayer(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;
        if (client.PlayerObject == null)
            return false;
        var instance = client.PlayerObject.GetComponent<PlayerInstance>();
        return instance != null && !instance.IsOutside;
    }

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
        Action onComplete = null, bool deferExit = false, bool lockOutsidePlayers = false)
    {
        if (!IsServer) return;

        if (speaker == null || dialogue == null ||
            dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunDialogue(speaker, dialogue, onComplete, deferExit, lockOutsidePlayers));
    }

    /// <summary>
    /// Switches the active override camera to the entry matching <paramref name="key"/>.
    /// An empty key deactivates any active override. Must be called on the server.
    /// </summary>
    public void SwitchCamera(string key)
    {
        if (!IsServer) return;
        _currentCameraKey = key ?? string.Empty;
        SetActiveOverrideCamClientRpc(key ?? string.Empty, BuildParticipantRpcParams());
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
        // Clear the server-side flag immediately so CheckProximityJoins stops running.
        IsScriptedModeActive = false;
        _currentSpeakerTransform = null;
        _participants.Clear();
        ExitScriptedModeClientRpc();
    }

    /// <summary>
    /// Deactivates the active suspect camera and any override camera without exiting scripted
    /// mode. Use when transitioning to a cinematic or neutral-camera phase mid-session while
    /// keeping the player locked in scripted mode (e.g. before a cutscene plays mid-sequence).
    /// Must be called on the server.
    /// </summary>
    public void ClearCamerasKeepMode()
    {
        if (!IsServer) return;
        ClearCamerasKeepModeClientRpc();
    }

    [ClientRpc]
    private void ClearCamerasKeepModeClientRpc()
    {
        DeactivateOverrideCam();
        SuspectController.Instance?.SetSuspectCamActive(false);
    }

    /// <summary>
    /// Plays a scripted dialogue sequence using the megaphone speaker identity (no NPC character
    /// required). Lines are displayed with the configured megaphone name and colour, audio is
    /// routed through <see cref="MegaphoneDialogueManager"/>, and the player can click to advance
    /// exactly as with character dialogue. Must be called on the server.
    /// <para>
    /// When <paramref name="unlocked"/> is <c>true</c>, the sequence plays without locking player
    /// movement or activating the suspect camera — the player remains free to move and look around
    /// while the megaphone line plays. Use this for instructional lines that require the player to
    /// physically interact with something (e.g. the lever close-shutter instruction on Day 1).
    /// The advance gate and cam cleanup still run normally.
    /// </para>
    /// </summary>
    public void PlayMegaphoneDialogue(ScriptedDialogue dialogue, Action onComplete = null, bool unlocked = false)
    {
        if (!IsServer) return;

        if (dialogue == null || dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayMegaphoneDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunMegaphoneDialogue(dialogue, onComplete, unlocked));
    }

    // -------------------------------------------------------------------------
    // Input handling — E key and mouse click for all clients
    // -------------------------------------------------------------------------

    private void Update()
    {
        // Server: detect players who have walked into proximity of the active speaker and
        // late-join them so they are counted in the advance / choice gates going forward.
        // For a 2-player session with a single distance check per non-participant this is
        // negligible per-frame cost, so no throttle is needed.
        if (IsServer && IsScriptedModeActive && _currentSpeakerTransform != null)
            CheckProximityJoins();

        // _clientIsWaitingForInput is set on ALL clients via ClientRpc, so both the
        // host and non-host clients can skip reveals and advance the dialogue gate.
        if (!_clientIsWaitingForInput) return;

        // Ignore clicks that land on any UI element (e.g. choice buttons).
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                      UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        bool pressedE     = Input.GetKeyDown(KeyCode.E);
        bool pressedClick = Input.GetMouseButtonDown(0) && !overUI;

        if (!pressedE && !pressedClick) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // First input: complete the typewriter locally without advancing the line.
            // Each player independently skips their own reveal — no network call needed.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second input (or first when typewriter is already done): vote to advance.
        AdvanceScriptedLineServerRpc();
    }

    // -------------------------------------------------------------------------
    // Internal sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue,
        Action onComplete, bool deferExit = false, bool lockOutsidePlayers = false)
    {
        ulong speakerNetId = speaker.GetComponent<NetworkObject>().NetworkObjectId;

        Debug.Log($"[ScriptedDialogueRunner] RunDialogue — IsSpawned={IsSpawned}, IsServer={IsServer}, speakerNetId={speakerNetId}, deferExit={deferExit}");

        _lastAnimTrigger = string.Empty;

        // Set the server-side flag immediately so CheckProximityJoins works even if the
        // host client is not a participant (targeted RPCs won't reach a non-participant host).
        IsScriptedModeActive = true;

        // Seed the participant set before broadcasting EnterScriptedMode so the advance
        // gate is correct from the very first line.
        _currentSpeakerTransform = speaker.transform;
        SeedParticipants(lockOutsidePlayers);

        // Target EnterScriptedMode to participants only — far-away players should NOT be
        // pulled into scripted mode until they walk within _joinRadius (LateJoinClientRpc).
        // Player UI is broadcast to ALL clients so the HUD never overlaps a cutscene shot,
        // even for non-participants who won't receive EnterScriptedModeClientRpc.
        SetPlayerUIVisibleClientRpc(false);
        EnterScriptedModeClientRpc(speakerNetId, lockOutsidePlayers, BuildParticipantRpcParams());
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            // Track the active camera key server-side so late-joining clients can receive
            // a camera catch-up RPC before LateJoinClientRpc fires.
            _currentCameraKey = node.cameraTrigger ?? string.Empty;

            // Camera cut and text effect are set before the line starts so they're
            // visible from the very first word of the subtitle.
            // Target camera RPCs to participants — far-away players must not have their
            // camera hijacked by a dialogue they are not yet part of.
            SetActiveOverrideCamClientRpc(node.cameraTrigger ?? string.Empty, BuildParticipantRpcParams());
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
            // Clear the server-side flag before the client RPC so CheckProximityJoins stops.
            IsScriptedModeActive = false;
            _currentSpeakerTransform = null;
            _participants.Clear();
            ExitScriptedModeClientRpc(lockOutsidePlayers);
            yield return null;
        }

        onComplete?.Invoke();
        Debug.Log($"[ScriptedDialogueRunner] Scripted dialogue complete (deferExit={deferExit}).");
    }

    /// <summary>Says a line and waits for all connected players to advance (or the timeout to expire).</summary>
    private IEnumerator SayAndWait(SuspectCharacter speaker, string text)
    {
        _awaitingScriptedInput = true;
        // Target the "input open" signal only to participants — non-participants must not
        // be able to send advance RPCs that interfere with the gate.
        SetAwaitingInputClientRpc(true, BuildParticipantRpcParams());
        DialogueManager.Instance.SayDialogue(speaker, text, waitForInput: true);
        yield return StartCoroutine(WaitForScriptedAdvance());
        // Broadcast the "input closed" signal so any late-joiner who had _clientIsWaitingForInput
        // set (via LateJoinClientRpc) also has it cleared correctly.
        SetAwaitingInputClientRpc(false);
        _awaitingScriptedInput = false;
    }

    // -------------------------------------------------------------------------
    // Advance gate — server-side
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resets the advance gate and suspends the sequence until <see cref="OpenAdvanceGate"/>
    /// resolves it. Must run on the server.
    /// </summary>
    private IEnumerator WaitForScriptedAdvance()
    {
        _scriptedAdvanceReady = false;
        _advanceSet.Clear();
        yield return new WaitUntil(() => _scriptedAdvanceReady);
    }

    /// <summary>
    /// Received from any client pressing E or clicking to advance a scripted line.
    /// After the first submission a countdown begins; the gate opens when every connected
    /// player has submitted or the countdown expires.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void AdvanceScriptedLineServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_awaitingScriptedInput || _scriptedAdvanceReady) return;

        ulong senderId = rpcParams.Receive.SenderClientId;

        // A player who submits an advance is clearly nearby — add them to the participant
        // set if they somehow weren't included at the proximity-seeding step.
        if (!_participants.Contains(senderId))
        {
            _participants.Add(senderId);
            Debug.Log($"[ScriptedDialogueRunner] Client {senderId} self-joined participants via advance.");
        }

        _advanceSet.Add(senderId);

        int required = Mathf.Max(1, _participants.Count);

        // Only show the countdown timer when multiple participants need to advance.
        // When required == 1 the gate opens immediately below — no timer needed.
        if (_advanceSet.Count == 1 && _advanceTimerCoroutine == null && required > 1)
            _advanceTimerCoroutine = StartCoroutine(AdvanceTimeoutCoroutine());

        if (_advanceSet.Count >= required)
            OpenAdvanceGate();
    }

    private IEnumerator AdvanceTimeoutCoroutine()
    {
        ShowAdvanceTimerClientRpc(_advanceTimeoutSeconds);
        yield return new WaitForSeconds(_advanceTimeoutSeconds);
        OpenAdvanceGate();
    }

    private void OpenAdvanceGate()
    {
        if (_scriptedAdvanceReady) return;
        _scriptedAdvanceReady = true;

        if (_advanceTimerCoroutine != null)
        {
            StopCoroutine(_advanceTimerCoroutine);
            _advanceTimerCoroutine = null;
        }

        HideAdvanceTimerClientRpc();

        // Unblocks WaitForInputRoutine on all clients so subtitles clear correctly.
        DialogueManager.Instance.AdvanceDialogueServerRpc();
    }

    // -------------------------------------------------------------------------
    // Proximity participant management — server-side
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates <see cref="_participants"/> at the start of a dialogue with all connected
    /// clients who should participate in the advance and choice gates.
    ///
    /// Rules (applied in order):
    /// <list type="bullet">
    ///   <item>When <paramref name="lockOutsidePlayers"/> is <c>true</c>, every connected
    ///         client is a participant — outside players are explicitly locked in.</item>
    ///   <item>Inside players (<see cref="PlayerInstance.IsOutside"/> == false) are always
    ///         included regardless of their distance from the speaker, because they are in
    ///         the booth and will always enter dialogue mode.</item>
    ///   <item>Outside players are included only if they are within
    ///         <see cref="_joinRadius"/> of the speaker.</item>
    ///   <item>Fallback: if no one qualifies, include all connected clients so the dialogue
    ///         is never permanently unblockable (e.g. before players reach their positions).</item>
    /// </list>
    /// Must be called on the server.
    /// </summary>
    private void SeedParticipants(bool lockOutsidePlayers = false)
    {
        _participants.Clear();

        // When all players are explicitly locked in, include everyone.
        if (lockOutsidePlayers || _currentSpeakerTransform == null)
        {
            _participants.UnionWith(NetworkManager.Singleton.ConnectedClientsIds);
            Debug.Log($"[ScriptedDialogueRunner] SeedParticipants — all {_participants.Count} clients seeded (lockOutsidePlayers={lockOutsidePlayers}).");
            return;
        }

        Vector3 speakerPos = _currentSpeakerTransform.position;
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // Inside players are always in the booth and will always enter dialogue mode —
            // include them regardless of exact distance to avoid seeding race conditions
            // where a player is slightly outside _joinRadius at the moment of seeding.
            if (IsClientInsidePlayer(clientId) || IsClientWithinJoinRadius(clientId, speakerPos))
                _participants.Add(clientId);
        }

        // Fallback: if no one is close, include all so the dialogue is never unblockable.
        if (_participants.Count == 0)
            _participants.UnionWith(NetworkManager.Singleton.ConnectedClientsIds);

        Debug.Log($"[ScriptedDialogueRunner] SeedParticipants — {_participants.Count}/{NetworkManager.Singleton.ConnectedClientsIds.Count} participants seeded.");
    }

    /// <summary>
    /// Checks every non-participant connected client against the join radius. Players who
    /// have walked close enough are added to <see cref="_participants"/> and receive:
    /// <list type="number">
    ///   <item>A targeted <see cref="SetActiveOverrideCamClientRpc"/> to catch up to the
    ///         current camera state.</item>
    ///   <item>A targeted <see cref="LateJoinClientRpc"/> to lock their controls, activate
    ///         the suspect camera, and set their input-waiting state.</item>
    /// </list>
    /// Called every frame on the server while a dialogue is active.
    /// </summary>
    private void CheckProximityJoins()
    {
        if (_currentSpeakerTransform == null) return;

        Vector3 speakerPos = _currentSpeakerTransform.position;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (_participants.Contains(clientId)) continue;
            if (!IsClientWithinJoinRadius(clientId, speakerPos)) continue;

            _participants.Add(clientId);
            Debug.Log($"[ScriptedDialogueRunner] Client {clientId} joined dialogue via proximity.");

            ulong speakerNetId = _currentSpeakerTransform.GetComponent<NetworkObject>()?.NetworkObjectId ?? 0UL;
            var singleClientRpc = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            // Send the current camera state first so the late-joiner's view snaps to the
            // correct camera before dialogue mode activates (avoids a one-frame camera pop).
            SetActiveOverrideCamClientRpc(_currentCameraKey, singleClientRpc);

            LateJoinClientRpc(speakerNetId, _awaitingScriptedInput, singleClientRpc);
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="clientId"/>'s <c>PlayerObject</c> is within
    /// <see cref="_joinRadius"/> of <paramref name="origin"/>.
    /// </summary>
    private bool IsClientWithinJoinRadius(ulong clientId, Vector3 origin)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;
        if (client.PlayerObject == null) return false;

        return Vector3.Distance(origin, client.PlayerObject.transform.position) <= _joinRadius;
    }

    /// <summary>
    /// Sent to a single client when they enter the join radius mid-dialogue.
    /// Ensures <see cref="IsScriptedModeActive"/> and <see cref="_clientIsWaitingForInput"/>
    /// are set correctly and locks the player into the appropriate dialogue mode.
    /// <para>
    /// Mirrors <see cref="EnterScriptedModeClientRpc"/>: inside players receive
    /// <see cref="DialogueChoiceSystem.EnterScriptedDialogueMode"/> (suspect cam + movement lock),
    /// while outside players receive <see cref="DialogueChoiceSystem.EnterScriptedDialogueModeOutside"/>
    /// (movement lock only — no suspect cam). This prevents the booth-facing cam from being
    /// permanently stuck on outside players when the exit path only restores inside players.
    /// </para>
    /// <para>
    /// <paramref name="isWaitingForInput"/> mirrors the server's <c>_awaitingScriptedInput</c>
    /// flag at the moment of the join, so the late-joiner can immediately advance the current
    /// line if the gate is still open.
    /// </para>
    /// </summary>
    [ClientRpc]
    private void LateJoinClientRpc(ulong speakerNetId, bool isWaitingForInput, ClientRpcParams rpcParams = default)
    {
        IsScriptedModeActive = true;
        _clientIsWaitingForInput = isWaitingForInput;

        UIController.Instance?.ClosePlayerUI();

        if (PlayerInstance.Instance == null) return;

        Transform lookTarget = null;
        if (speakerNetId != 0 &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            lookTarget = netObj.transform;

        // Mirror EnterScriptedModeClientRpc: outside players get movement-locked but must NOT
        // receive the booth suspect-cam activation. Calling EnterScriptedDialogueMode for an
        // outside player activates SuspectController.SetSuspectCamActive(true) and disables the
        // interaction controller, leaving the cam and interaction lock permanently stuck because
        // ExitScriptedModeClientRpc only calls ExitScriptedDialogueMode for inside players.
        if (PlayerInstance.Instance.IsOutsideLocal)
            DialogueChoiceSystem.Instance?.EnterScriptedDialogueModeOutside(lookTarget);
        else
            DialogueChoiceSystem.Instance?.EnterScriptedDialogueMode(lookTarget);

        Debug.Log($"[ScriptedDialogueRunner] LateJoinClientRpc — client {NetworkManager.Singleton.LocalClientId} " +
                  $"entered dialogue mode via proximity (outside={PlayerInstance.Instance.IsOutsideLocal}), " +
                  $"isWaitingForInput={isWaitingForInput}.");
    }

    // -------------------------------------------------------------------------
    // Megaphone sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunMegaphoneDialogue(ScriptedDialogue dialogue, Action onComplete, bool unlocked = false)
    {
        // When unlocked, the player is already free (caller called ExitScriptedMode first).
        // Skipping EnterScriptedModeClientRpc keeps movement and the first-person camera active
        // so the player can reach whatever the instruction is asking them to do.
        if (!unlocked)
            EnterScriptedModeClientRpc(0UL);

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
        SetAwaitingInputClientRpc(true);
        SayMegaphoneLineClientRpc(text);
        yield return StartCoroutine(WaitForScriptedAdvance());
        SetAwaitingInputClientRpc(false);
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

        _choiceSubmissions.Clear();
        _choicePlayerNames.Clear();
        _choiceResolved = false;
        _resolvedChoiceIndex = -1;
        ShowChoicesClientRpc(node.choices[0].playerChoiceText, node.choices[1].playerChoiceText, BuildParticipantRpcParams());

        yield return new WaitUntil(() => _choiceResolved);

        // Broadcast the finalized choice: hide the panel, clear highlights, and show the
        // winning player's spoken line as a subtitle on all clients.
        var chosen = node.choices[_resolvedChoiceIndex];
        FinalizeChoiceClientRpc(chosen.playerChoiceText, FindWinnerName(_resolvedChoiceIndex));
        yield return null; // flush the RPC before playing the NPC response

        ResetAndTriggerAnimationClientRpc(speakerNetId, _lastAnimTrigger, chosen.animationTrigger);
        _lastAnimTrigger = chosen.animationTrigger ?? string.Empty;
        yield return StartCoroutine(SayAndWait(speaker, chosen.npcResponse));
    }

    // -------------------------------------------------------------------------
    // Client RPCs — mode
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void EnterScriptedModeClientRpc(ulong speakerNetId, bool lockOutsidePlayers = false, ClientRpcParams rpcParams = default)
    {
        IsScriptedModeActive = true;

        UIController.Instance?.ClosePlayerUI();

        // Cache the speaker ID on all clients so SuspectController can resolve per-character cameras
        // via CurrentSpeakerNetId — must be stored before any early return below.
        _clientSpeakerNetId = speakerNetId;

        Debug.Log($"[ScriptedDialogueRunner] EnterScriptedModeClientRpc — " +
                  $"PlayerInstance={PlayerInstance.Instance != null}, " +
                  $"IsOutsideLocal={PlayerInstance.Instance?.IsOutsideLocal}, " +
                  $"ChoiceSystemInstance={DialogueChoiceSystem.Instance != null}");

        if (PlayerInstance.Instance == null) return;

        // Resolve the speaker's transform so the player can rotate to face them.
        Transform lookTarget = null;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            lookTarget = netObj.transform;

        if (PlayerInstance.Instance.IsOutsideLocal)
        {
            // Outside players get movement-locked when explicitly requested, but never get the
            // booth suspect-cam activated — camera cuts are handled by SetActiveOverrideCamClientRpc.
            if (lockOutsidePlayers)
                DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(lookTarget);
            return;
        }

        DialogueChoiceSystem.Instance.EnterScriptedDialogueMode(lookTarget);
    }

    [ClientRpc]
    private void ExitScriptedModeClientRpc(bool lockOutsidePlayers = false)
    {
        IsScriptedModeActive = false;

        // Deactivate any override camera before restoring the default cam state.
        DeactivateOverrideCam();

        UIController.Instance?.ShowPlayerUI();

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal)
        {
            // Always call ExitScriptedDialogueModeOutside for outside players — covers both the
            // explicit lockOutsidePlayers path AND the late-join path where LateJoinClientRpc
            // called EnterScriptedDialogueModeOutside without lockOutsidePlayers being set.
            // ExitScriptedDialogueModeOutside is idempotent (bails if not in mode), so calling
            // it when the player was never locked is a safe no-op.
            if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutsideLocal)
                DialogueChoiceSystem.Instance?.ExitScriptedDialogueModeOutside();

            _clientSpeakerNetId = 0;
            return;
        }

        // ExitScriptedDialogueMode calls SuspectController.SetSuspectCamActive(false), which reads
        // _clientSpeakerNetId to find and deactivate the speaker's per-character cam — reset after.
        DialogueChoiceSystem.Instance.ExitScriptedDialogueMode();
        _clientSpeakerNetId = 0;
    }

    /// <summary>
    /// Broadcasts a player-UI visibility change to ALL clients regardless of participant status.
    /// Called at the start of every scripted dialogue sequence so the HUD never overlaps a
    /// cutscene camera shot, even on clients who are not dialogue participants.
    /// Restoration is handled by the existing <see cref="ExitScriptedModeClientRpc"/> broadcast.
    /// </summary>
    [ClientRpc]
    private void SetPlayerUIVisibleClientRpc(bool visible)
    {
        if (visible)
            UIController.Instance?.ShowPlayerUI();
        else
            UIController.Instance?.ClosePlayerUI();
    }

    // -------------------------------------------------------------------------
    // Client RPCs — advance timer UI
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void ShowAdvanceTimerClientRpc(float duration)
    {
        DialogueAdvanceTimer.Instance?.Show(duration);
    }

    [ClientRpc]
    private void HideAdvanceTimerClientRpc()
    {
        DialogueAdvanceTimer.Instance?.Hide();
    }

    // -------------------------------------------------------------------------
    // Client RPCs — input gate sync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tells clients whether the server is currently inside a SayAndWait gate.
    /// Drives <see cref="_clientIsWaitingForInput"/> so clients can process
    /// E-key and mouse-click input the same way the host does.
    ///
    /// Pass <see cref="BuildParticipantRpcParams"/> when setting to <c>true</c> to restrict
    /// the signal to participants only. Setting to <c>false</c> is always broadcast so
    /// any late-joiner who had the flag set also gets it cleared correctly.
    /// </summary>
    [ClientRpc]
    private void SetAwaitingInputClientRpc(bool waiting, ClientRpcParams rpcParams = default)
    {
        _clientIsWaitingForInput = waiting;
    }

    // -------------------------------------------------------------------------
    // Client RPCs — camera
    // -------------------------------------------------------------------------

    /// <summary>
    /// Activates the camera mapped to <paramref name="key"/> and deactivates the previous
    /// override. An empty key defaults to the current speaker's <c>SuspectCam</c> if one is
    /// active; if there is no speaker it simply deactivates any active override.
    /// <para>
    /// Three well-known keys are resolved dynamically from the current dialogue speaker rather
    /// than the static <see cref="_cameras"/> registry:
    /// <list type="bullet">
    ///   <item><term>SuspectCam</term><description>The speaker's <see cref="SuspectCharacter.SuspectCam"/>.</description></item>
    ///   <item><term>SuspectFaceCam / suspect face</term><description>The speaker's <see cref="SuspectCharacter.SuspectFaceCam"/> close-up.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    [ClientRpc]
    private void SetActiveOverrideCamClientRpc(string key, ClientRpcParams rpcParams = default)
    {
        // Always deactivate the current override first.
        if (_activeOverrideCam != null)
        {
            _activeOverrideCam.SetActive(false);
            _activeOverrideCam = null;
        }

        // Empty trigger: default to the speaker's SuspectCam so dialogue nodes without an
        // explicit cameraTrigger still cut to the character rather than showing nothing.
        if (string.IsNullOrEmpty(key))
        {
            if (_clientSpeakerNetId != 0)
            {
                GameObject defaultCam = ResolveSpeakerCamera("SuspectCam");
                if (defaultCam != null)
                {
                    defaultCam.SetActive(true);
                    _activeOverrideCam = defaultCam;
                }
            }
            return;
        }

        // Resolve per-speaker cameras by well-known keys — no static registry entry needed.
        // "suspect face" is an alias for SuspectFaceCam (close-up shot of the current speaker).
        if (key == "SuspectCam" || key == "SuspectFaceCam" || key == "suspect face")
        {
            // If the booth glass is still intact it would obscure a face-cam close-up.
            // Redirect to the wider SuspectCam so the shot makes sense through the glass.
            string resolvedKey = key;
            if ((key == "SuspectFaceCam" || key == "suspect face") &&
                BreakableGlassController.Instance != null &&
                BreakableGlassController.Instance.IsWindowVisible)
            {
                resolvedKey = "SuspectCam";
            }

            GameObject speakerCam = ResolveSpeakerCamera(resolvedKey);
            if (speakerCam != null)
            {
                speakerCam.SetActive(true);
                _activeOverrideCam = speakerCam;
            }
            else
            {
                Debug.LogWarning($"[ScriptedDialogueRunner] Camera key '{key}': speaker not found or has no camera assigned.");
            }
            return;
        }

        // Fall back to the static camera registry.
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

    /// <summary>
    /// Resolves the per-character camera from the current dialogue speaker using
    /// <see cref="_clientSpeakerNetId"/>. Returns null if the speaker is not found or
    /// the requested camera is not assigned.
    /// </summary>
    private GameObject ResolveSpeakerCamera(string key)
    {
        if (_clientSpeakerNetId == 0) return null;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(_clientSpeakerNetId, out var netObj))
            return null;

        var character = netObj.GetComponent<SuspectCharacter>();
        if (character == null) return null;

        return (key == "SuspectFaceCam" || key == "suspect face") ? character.SuspectFaceCam : character.SuspectCam;
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
    private void ShowChoicesClientRpc(string choice0, string choice1, ClientRpcParams rpcParams = default)
    {
        _currentChoiceTexts = new[] { choice0, choice1 };

        // Both the booth player and any observer can submit a choice.
        if (PlayerInstance.Instance == null) return;

        DialogueChoiceSystem.Instance.ShowScriptedChoices(_currentChoiceTexts, OnLocalPlayerPickedChoice);
    }

    private void OnLocalPlayerPickedChoice(int choiceIndex)
    {
        // Highlight the chosen button locally — the panel stays open until both players pick.
        DialogueChoiceSystem.Instance.HighlightChoice(choiceIndex);

        string playerName = GetLocalPlayerName();
        string choiceText = _currentChoiceTexts != null ? _currentChoiceTexts[choiceIndex] : string.Empty;
        SubmitScriptedChoiceServerRpc(choiceIndex, playerName, choiceText);
    }

    // -------------------------------------------------------------------------
    // Server RPCs — choice submission
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sent by a player after selecting a choice. The server collects submissions from all
    /// connected players (with a <see cref="_advanceTimeoutSeconds"/> fallback after the first
    /// submission). When all submissions arrive — or the timeout fires — choices resolve:
    /// unanimous picks win outright; conflicting picks are decided by a random draw from the
    /// submitted options only.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitScriptedChoiceServerRpc(
        int choiceIndex, string playerName, string choiceText,
        ServerRpcParams rpcParams = default)
    {
        if (_choiceResolved) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (_choiceSubmissions.ContainsKey(senderId)) return; // ignore re-submissions

        // Add self-joining participant (same rationale as in AdvanceScriptedLineServerRpc).
        if (!_participants.Contains(senderId))
        {
            _participants.Add(senderId);
            Debug.Log($"[ScriptedDialogueRunner] Client {senderId} self-joined participants via choice submission.");
        }

        _choiceSubmissions[senderId] = choiceIndex;
        _choicePlayerNames[senderId] = playerName;

        // Broadcast highlight to everyone except the sender (they already highlighted locally).
        HighlightChoiceForOthersClientRpc(choiceIndex, senderId);

        int required = Mathf.Max(1, _participants.Count);

        if (_choiceSubmissions.Count == 1)
            _choiceTimerCoroutine = StartCoroutine(ChoiceTimeoutCoroutine());

        if (_choiceSubmissions.Count >= required)
            ResolveChoices();
    }

    private IEnumerator ChoiceTimeoutCoroutine()
    {
        ShowAdvanceTimerClientRpc(_advanceTimeoutSeconds);
        yield return new WaitForSeconds(_advanceTimeoutSeconds);
        ResolveChoices();
    }

    /// <summary>
    /// Resolves submitted choices: unanimous → that choice wins; conflicting → random pick
    /// drawn from the submitted values only (not from all available options).
    /// </summary>
    private void ResolveChoices()
    {
        if (_choiceResolved) return;
        _choiceResolved = true;

        if (_choiceTimerCoroutine != null)
        {
            StopCoroutine(_choiceTimerCoroutine);
            _choiceTimerCoroutine = null;
        }

        HideAdvanceTimerClientRpc();

        var submitted = new List<int>(_choiceSubmissions.Values);

        if (submitted.Count == 0)
        {
            _resolvedChoiceIndex = 0;
            Debug.LogWarning("[ScriptedDialogueRunner] ResolveChoices called with no submissions. Defaulting to choice 0.");
            return;
        }

        bool unanimous = submitted.TrueForAll(v => v == submitted[0]);
        _resolvedChoiceIndex = unanimous
            ? submitted[0]
            : submitted[UnityEngine.Random.Range(0, submitted.Count)];

        Debug.Log($"[ScriptedDialogueRunner] Choice resolved — index={_resolvedChoiceIndex}, " +
                  $"unanimous={unanimous}, submissions={submitted.Count}");
    }

    /// <summary>
    /// Highlights the submitted choice on every client except the sender,
    /// who already highlighted locally in <see cref="OnLocalPlayerPickedChoice"/>.
    /// </summary>
    [ClientRpc]
    private void HighlightChoiceForOthersClientRpc(int choiceIndex, ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        DialogueChoiceSystem.Instance?.HighlightChoice(choiceIndex);
    }

    /// <summary>
    /// Sent after both players have chosen (or the timeout fired). Hides the choice panel,
    /// clears all highlights, then spawns the winning player's spoken line as a subtitle on
    /// all clients before the NPC delivers their response.
    /// </summary>
    [ClientRpc]
    private void FinalizeChoiceClientRpc(string choiceText, string playerName)
    {
        DialogueChoiceSystem.Instance?.ResetChoiceHighlights();
        DialogueChoiceSystem.Instance?.HideChoicePanel();
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.cyan, isPlayer: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the name of the player who submitted <paramref name="winningIndex"/>.
    /// Falls back to a generic label if no submission matches (e.g. timeout with no picks).
    /// </summary>
    private string FindWinnerName(int winningIndex)
    {
        foreach (var kvp in _choiceSubmissions)
        {
            if (kvp.Value == winningIndex && _choicePlayerNames.TryGetValue(kvp.Key, out string name))
                return name;
        }
        return "Detective";
    }

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

        if (!string.IsNullOrEmpty(previousTrigger))
            anim.ResetTrigger(previousTrigger);

        anim.SetTrigger("ForceIdle");

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
