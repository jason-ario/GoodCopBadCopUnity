using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
    // =========================================================================
    // Session slots
    // -------------------------------------------------------------------------
    // Every ScriptedDialogueRunner component on the runner GameObject is one conversation
    // SESSION SLOT. Each component owns a complete, independent copy of the sequence state
    // (speaker, participants, advance/choice gates, camera key, client-side lock/input state) and
    // its own RPC channel, so several conversations with different NPCs run in parallel without
    // touching each other — e.g. the host talking to the Postman outside while Player 2 talks to
    // Ivan at the booth. The FIRST component is the primary: it carries the inspector config
    // (copied to the other slots at runtime) and is the public facade (Instance) that dispatches
    // every PlayDialogue / PlayMegaphoneDialogue to a free slot.
    //
    // Rules enforced by the dispatcher (server):
    //  * One session per speaker: a second sequence for the same NPC waits for the first.
    //  * A player is a participant of at most one session (seeding, proximity/interaction joins
    //    and self-joins skip players engaged in another session or in a world conversation).
    //  * LOCKED megaphone dialogues broadcast to and lock every player, so they run alone: they
    //    wait for every session to finish, and new character sessions wait behind them.
    //  * UNLOCKED megaphone / phone dialogues run in parallel with character sessions: they only
    //    reach players who are free when they start, and a listener who gets pulled into a
    //    character conversation is released from them. One megaphone voice plays at a time.
    //  * Barks never touch gates/participants and are not dispatched.
    // =========================================================================

    public static ScriptedDialogueRunner Instance { get; private set; }

    private static readonly List<ScriptedDialogueRunner> s_slots = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_slots.Clear();
        Instance = null;
        IsMegaphoneBarkActive = false;
    }

    private ScriptedDialogueRunner _primary;
    private bool IsPrimary => _primary == null || _primary == this;

    /// <summary>
    /// True on the local client only while it is a participant in a scripted dialogue
    /// sequence in any session slot (set by the participant-targeted Enter/LateJoin client RPCs).
    /// Never written by server-side logic, so a non-participant host is not treated as a participant.
    /// </summary>
    public static bool IsScriptedModeActive
    {
        get
        {
            for (int i = 0; i < s_slots.Count; i++)
                if (s_slots[i] != null && s_slots[i]._clientIsParticipant) return true;
            return false;
        }
    }

    // Local: this slot put the local player into scripted mode (participant of this session).
    private bool _clientIsParticipant;

    /// <summary>
    /// Local-only. True while a scripted sequence actually holds the local player's dialogue lock
    /// (movement/look). False for participants a sequence deliberately leaves free to move (outside
    /// players in a sequence without lockOutsidePlayers / world camera), even though
    /// <see cref="IsScriptedModeActive"/> is true for them. Control/look re-enable guards use this so
    /// a free participant isn't left stuck after a pause menu (or any other UI) temporarily took control.
    /// </summary>
    public static bool IsLocalPlayerDialogueLocked
    {
        get
        {
            for (int i = 0; i < s_slots.Count; i++)
            {
                var s = s_slots[i];
                if (s != null && s._clientIsParticipant && s._clientEnteredDialogueLock) return true;
            }
            return false;
        }
    }

    // Server-only: true while a scripted sequence is running, independent of whether the host
    // player is a participant. Drives proximity joins and leave/rejoin validation.
    private bool _serverSequenceActive;

    /// <summary>
    /// Server-only. True while any session slot runs, holds (deferExit) or has queued a scripted or
    /// megaphone sequence. Use <see cref="CanStartSpeakerSequenceNow"/> to ask whether a specific
    /// NPC could start a conversation right now without waiting.
    /// </summary>
    public bool IsServerSequenceActive
    {
        get
        {
            if (!IsServer) return false;
            var primary = IsPrimary ? this : _primary;
            if (primary._pending.Count > 0) return true;
            for (int i = 0; i < s_slots.Count; i++)
            {
                var s = s_slots[i];
                if (s != null && (s.IsSlotBusy || s._serverSequenceActive || s._megaphoneSequenceActive)) return true;
            }
            return false;
        }
    }

    // Server-only, per slot: a dispatched sequence coroutine is running on this slot.
    private bool _sequenceRunning;

    // Server-only, per slot: the running sequence is a megaphone dialogue (and whether it locks players).
    private bool _runningMegaphone;
    private bool _runningMegaphoneLocked;

    private bool IsRunningUnlockedMegaphone => _sequenceRunning && _runningMegaphone && !_runningMegaphoneLocked;

    // Server-only, per slot: a deferExit sequence finished on this slot and scripted mode is still
    // held for its participants until ExitScriptedMode or a chained PlayMegaphoneDialogue.
    private bool _deferredExitHold;

    // Server-only, per slot: NetworkObjectId of the NPC this slot is running/holding (0 = none).
    private ulong _reservedSpeakerNetId;

    private bool IsSlotBusy => _sequenceRunning || _deferredExitHold;

    // Server-only, primary: requests waiting for a free slot / their speaker / a megaphone.
    private sealed class PendingSequence
    {
        public string Label;
        public bool IsMegaphone;
        public bool Unlocked;
        public ulong SpeakerNetId;
        public ScriptedDialogueRunner ChainSlot;
        public Func<ScriptedDialogueRunner, IEnumerator> Run;
        public bool LoggedWait;
    }
    private readonly List<PendingSequence> _pending = new();

    // Server-only: true while RunMegaphoneDialogue is running. Megaphone sequences enter
    // scripted mode for every client (broadcast), unlike participant-targeted suspect dialogue.
    private bool _megaphoneSequenceActive;

    /// <summary>
    /// True on every client while a megaphone bark (<see cref="PlayMegaphoneBark"/>) is playing.
    /// A bark never touches the player (no lock, no cutscene flag, no advance input); its only
    /// gameplay effect is that character conversations can't be started until it ends
    /// (see <see cref="SuspectCharacter.Interact"/>).
    /// </summary>
    public static bool IsMegaphoneBarkActive { get; private set; }

    [Header("Cutscene Cameras")]
    [Tooltip("Maps camera trigger keys (set on ScriptedDialogueNode) to Cinemachine camera GameObjects. " +
             "An empty trigger deactivates overrides and returns to the default suspect cam.")]
    [SerializeField] private ScriptedCameraEntry[] _cameras;

    [Header("Megaphone Speaker")]
    [Tooltip("Speaker name shown in subtitles when playing megaphone scripted dialogue.")]
    [SerializeField, LocalizeField] private string _megaphoneSpeakerName = "Megaphone";

    [Tooltip("Name colour for megaphone subtitles.")]
    [SerializeField] private Color _megaphoneSpeakerColor = new Color(1f, 0.65f, 0f);

    [Header("Megaphone Bark")]
    [Tooltip("Seconds a bark line stays on screen after its text has fully revealed before the next line plays.")]
    [SerializeField, Min(0f)] private float _barkLineHoldSeconds = 2f;

    [Tooltip("Safety cap (seconds) on waiting for a bark line's typewriter reveal to finish.")]
    [SerializeField, Min(1f)] private float _barkRevealTimeoutSeconds = 15f;

    [Header("Wobble")]
    [Tooltip("Default wobble profile applied to every scripted dialogue subtitle. " +
             "Individual nodes can override this with a different profile via wobbleProfileOverride.")]
    [SerializeField] private TMPWobbleProfile _defaultWobbleProfile;

    [Tooltip("Additional profiles that nodes can reference as per-line overrides. " +
             "A node's wobbleProfileOverride must appear in this list to be sent over the network. " +
             "Index 0 here maps to RPC profile index 0 (the default is always RPC index -1).")]
    [SerializeField] private TMPWobbleProfile[] _additionalWobbleProfiles;

    [Header("Fonts")]
    [Tooltip("Font assets that nodes can reference as per-line overrides via fontOverride. " +
             "A node's fontOverride must appear in this list to be sent over the network. " +
             "Leave a node's fontOverride null to keep the subtitle prefab's default font.")]
    [SerializeField] private TMPro.TMP_FontAsset[] _additionalFontAssets;

    [Header("Multi-Player Advance")]
    [Tooltip("Seconds after the first player advances (or submits a choice) before the " +
             "sequence continues automatically without the second player's input.")]
    [SerializeField] private float _advanceTimeoutSeconds = 1.5f;

    [Header("Proximity Join")]
    [Tooltip("Distance (world units) from the speaker within which a player is included as a " +
             "required participant when a standard scripted dialogue STARTS. Optional suspect " +
             "intros seed only the interacting player.")]
    [SerializeField] private float _joinRadius = 5f;

    [Tooltip("When false (default), proximity only decides who is pulled in when a conversation " +
             "starts. A player who arrives mid-conversation is NOT forced in — they overhear the " +
             "subtitles and join by interacting with the speaker. When true, walking within the " +
             "join radius mid-conversation pulls the player in automatically (legacy behaviour).")]
    [SerializeField] private bool _proximityJoinMidSequence = false;

    [Header("Glass Bang")]
    [Tooltip("Animator trigger that, when used as a node/choice animationTrigger, also lands a real " +
             "hit on the booth window at the impact frame — the same glass damage / shutter feedback " +
             "a lineup mutant deals — plus a camera impulse.")]
    [SerializeField] private string _glassBangTrigger = "BangOnShutters";

    [Tooltip("Seconds after the trigger fires before the hit lands. Matches the fist-impact " +
             "frame of 'Bang on window.anim' (~0.48s).")]
    [SerializeField] [Min(0f)] private float _glassBangImpactDelay = 0.48f;

    [Tooltip("If false, a dialogue bang never delivers the final blow: on glass one hit from " +
             "shattering it only knocks (sound + shake) instead of smashing the window mid-conversation.")]
    [SerializeField] private bool _glassBangCanSmash = false;

    [Tooltip("Impulse fired on every client when the bang lands (on glass or shutter). Optional.")]
    [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource _glassBangImpulseSource;

    // Server-side: pending glass hit, cancelled if the line is skipped before the impact frame.
    private Coroutine _glassBangCoroutine;

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
    private string[] _activeChoiceTexts;
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

    // ClientIds who explicitly backed out of the current dialogue (Q / back button).
    // Excluded from proximity re-join until they interact with the speaker again.
    private readonly HashSet<ulong> _leftParticipants = new();

    // True on the server while a SayAndWait gate is waiting on the choice submission gate
    // (mirrors _awaitingScriptedInput for the choice flow). Used to know whether to kick off
    // an unattended auto-resolve when the last participant leaves mid-choice.
    private bool _awaitingScriptedChoice;

    // Transform of the current NPC speaker — used for server-side proximity checks.
    private Transform _currentSpeakerTransform;

    // Per-sequence participation policy. Optional suspect intros seed only the interacting
    // player, do not proximity-join the co-op partner, and let either participant back out.
    private bool _joinByInteractionOnly;
    private bool _allowParticipantExit;

    // Client-side: the "leave dialogue" Back-button callback this runner registered, so every exit
    // path (natural end, early leave) removes exactly that callback and nothing it doesn't own.
    private UnityEngine.Events.UnityAction _clientLeaveBackAction;

    private void ShowLeaveBackButtonLocal()
    {
        if (UIController.Instance == null) return;
        if (_clientLeaveBackAction == null)
            _clientLeaveBackAction = () => LeaveScriptedDialogueServerRpc();
        UIController.Instance.RemoveBackButtonCallback(_clientLeaveBackAction); // never stack it twice
        UIController.Instance.ShowBackButton(_clientLeaveBackAction);
    }

    private void HideLeaveBackButtonLocal()
    {
        if (_clientLeaveBackAction != null)
            UIController.Instance?.RemoveBackButtonCallback(_clientLeaveBackAction);
    }

    // Per-sequence: every participant uses the world-dialogue camera (player-camera dolly-in on the
    // speaker, as SuspectWorldDialogue does for scene NPCs) even if they count as inside the booth,
    // instead of the booth suspect cam. For scene-placed NPCs who aren't booth suspects (the postman).
    private bool _useWorldDialogueCamera;

    // Per-sequence (server): the caller passed onAbandoned, so when every participant backs out the
    // sequence stops right there (instead of auto-advancing to the end) and reports onAbandoned
    // rather than onComplete — talking to the speaker again starts the dialogue from the top.
    private bool _abandonWhenEmpty;
    private bool _abandoned;

    // Per-sequence proximity radius. Forced outside conversations (lockOutsidePlayers) seed
    // strictly by distance so an out-of-range co-op partner is never pulled in; they join later
    // by interacting with the speaker (callers pass their trigger radius here).
    private float _activeJoinRadius = -1f;
    private float EffectiveJoinRadius => _activeJoinRadius > 0f ? _activeJoinRadius : _joinRadius;

    // Throttle for the per-frame proximity scan — removed; cost is negligible for 2 players.

    // -------------------------------------------------------------------------
    // Client-side state
    // -------------------------------------------------------------------------

    // Set true on ALL clients (via ClientRpc) while the server coroutine is waiting
    // for input. Replaces the server-only _awaitingScriptedInput flag for Update() checks
    // so non-host clients can also skip reveals and advance.
    private bool _clientIsWaitingForInput;

    // Set on ALL clients alongside _clientIsWaitingForInput: true while the current line is
    // flagged ScriptedDialogueNode.unskippable, so input cannot fast-complete its reveal.
    private bool _clientRevealUnskippable;

    /// <summary>
    /// True while the active scripted line is unskippable and its typewriter is still revealing.
    /// Input sites must neither complete the reveal nor advance until this clears.
    /// </summary>
    public static bool IsUnskippableRevealActive
    {
        get
        {
            if (DialogueManager.Instance == null) return false;
            for (int i = 0; i < s_slots.Count; i++)
            {
                var s = s_slots[i];
                if (s != null && s._clientRevealUnskippable && s._clientIsWaitingForInput)
                    return DialogueManager.Instance.IsAnySubtitleRevealing();
            }
            return false;
        }
    }

    // Local: true once this runner has actually put the local player into DialogueChoiceSystem's
    // dialogue lock (EnterScriptedModeClientRpc / LateJoinClientRpc). False for sequences that
    // leave the player free (unlocked megaphone, unlocked outside players), so their exit knows
    // it doesn't own the lock and must not tear down a world conversation started meanwhile.
    private bool _clientEnteredDialogueLock;

    // Local: the current sequence asked for the world-dialogue camera (see _useWorldDialogueCamera).
    private bool _clientWorldCameraMode;

    /// <summary>Local: this player should take the outside/world-dialogue path (dolly-in camera,
    /// outside lock and exit) — either they're outside, or the sequence forces the world camera.</summary>
    private bool LocalUsesWorldDialoguePath =>
        PlayerInstance.Instance != null && (PlayerInstance.Instance.IsOutsideLocal || _clientWorldCameraMode);

    // Server-only: tracks whether any coroutine is currently inside a SayAndWait gate.
    // Used by AdvanceScriptedLineServerRpc to reject stale advances.
    private bool _awaitingScriptedInput;

    // Cached per ShowChoicesClientRpc so local-player callbacks can read the text.
    private string[] _currentChoiceTexts;

    // Client-side: the choice echo this client showed as a non-participant overhearing the
    // conversation. Cleared when the sequence ends, since ExitScriptedModeClientRpc never reaches it.
    private GameObject _overheardChoiceEcho;

    // Tracks the currently active override camera (client-side) so it can be deactivated
    // when the trigger changes or the sequence ends.
    private GameObject _activeOverrideCam;

    // Tracks the last animation trigger fired on the speaker so it can be reset before the
    // next node starts, preventing a fast-skipped trigger from replaying mid-sequence.
    private string _lastAnimTrigger = string.Empty;

    // Tracks the most recently activated camera key on the server so late-joining clients
    // can catch up to it via LateJoinClientRpc.
    private string _currentCameraKey = string.Empty;

    // NetworkObjectId of the current dialogue speaker — set on participants by
    // EnterScriptedModeClientRpc and on late joiners by LateJoinClientRpc. Reset to 0 on exit/leave.
    private ulong _clientSpeakerNetId;

    /// <summary>
    /// Local client: NetworkObjectId of the NPC the local player is in a scripted conversation with
    /// (any slot), or 0. Read by <see cref="SuspectController"/> / <see cref="DialogueChoiceSystem"/>
    /// to resolve the speaker's per-character cameras. Kept valid through the exit callbacks.
    /// </summary>
    public ulong CurrentSpeakerNetId => LocalSpeakerNetId;

    /// <inheritdoc cref="CurrentSpeakerNetId"/>
    public static ulong LocalSpeakerNetId
    {
        get
        {
            ulong fallback = 0;
            for (int i = 0; i < s_slots.Count; i++)
            {
                var s = s_slots[i];
                if (s == null || s._clientSpeakerNetId == 0) continue;
                if (s._clientIsParticipant) return s._clientSpeakerNetId;
                if (fallback == 0) fallback = s._clientSpeakerNetId;
            }
            return fallback;
        }
    }

    // Set on all clients (via broadcast RPC, regardless of participation) to the NetworkObjectId of
    // the NPC this slot is running a scripted dialogue with, or 0. Stays valid for players who left
    // the dialogue or never joined it, so talking to that NPC again rejoins the right session.
    private ulong _broadcastSpeakerNetId;

    /// <summary>
    /// Any machine: true while <paramref name="speakerNetId"/> is the speaker of a scripted session
    /// (broadcast to every client regardless of participation).
    /// </summary>
    public static bool IsSpeakerInActiveDialogue(ulong speakerNetId) =>
        speakerNetId != 0 && FindSlotForSpeaker(speakerNetId) != null;

    /// <summary>Any machine: true while some scripted session runs with a speaker other than <paramref name="exceptSpeakerNetId"/>.</summary>
    public static bool IsAnyOtherSpeakerInActiveDialogue(ulong exceptSpeakerNetId)
    {
        for (int i = 0; i < s_slots.Count; i++)
        {
            var s = s_slots[i];
            if (s != null && s._broadcastSpeakerNetId != 0 && s._broadcastSpeakerNetId != exceptSpeakerNetId)
                return true;
        }
        return false;
    }

    /// <summary>Any machine: the session slot currently running a scripted dialogue with <paramref name="speakerNetId"/>, or null.</summary>
    public static ScriptedDialogueRunner FindSlotForSpeaker(ulong speakerNetId)
    {
        if (speakerNetId == 0) return null;
        for (int i = 0; i < s_slots.Count; i++)
        {
            var s = s_slots[i];
            if (s != null && s._broadcastSpeakerNetId == speakerNetId) return s;
        }
        return null;
    }

    /// <summary>Local client: true while the local player is a participant of <paramref name="speakerNetId"/>'s scripted session.</summary>
    public static bool IsLocalParticipantOfSpeaker(ulong speakerNetId)
    {
        var slot = FindSlotForSpeaker(speakerNetId);
        return slot != null && slot._clientIsParticipant;
    }

    private void Awake()
    {
        var all = GetComponents<ScriptedDialogueRunner>();
        _primary = all.Length > 0 ? all[0] : this;

        if (IsPrimary)
            Instance = this;
        else
            CopyConfigFrom(_primary);

        if (!s_slots.Contains(this))
        {
            s_slots.Add(this);
            // Keep component order so slot indices match on every machine.
            s_slots.Sort((a, b) => Array.IndexOf(all, a).CompareTo(Array.IndexOf(all, b)));
        }

        if (IsPrimary)
            IsMegaphoneBarkActive = false;
    }

    /// <summary>
    /// Secondary slots are plain extra components with default values: copy the primary's inspector
    /// configuration so every slot resolves cameras, wobble/font indices and timings identically.
    /// </summary>
    private void CopyConfigFrom(ScriptedDialogueRunner src)
    {
        if (src == null || src == this) return;
        _cameras = src._cameras;
        _megaphoneSpeakerName = src._megaphoneSpeakerName;
        _megaphoneSpeakerColor = src._megaphoneSpeakerColor;
        _barkLineHoldSeconds = src._barkLineHoldSeconds;
        _barkRevealTimeoutSeconds = src._barkRevealTimeoutSeconds;
        _defaultWobbleProfile = src._defaultWobbleProfile;
        _additionalWobbleProfiles = src._additionalWobbleProfiles;
        _additionalFontAssets = src._additionalFontAssets;
        _advanceTimeoutSeconds = src._advanceTimeoutSeconds;
        _joinRadius = src._joinRadius;
        _proximityJoinMidSequence = src._proximityJoinMidSequence;
        _glassBangTrigger = src._glassBangTrigger;
        _glassBangImpactDelay = src._glassBangImpactDelay;
        _glassBangCanSmash = src._glassBangCanSmash;
        _glassBangImpulseSource = src._glassBangImpulseSource;
    }

    public override void OnDestroy()
    {
        // Safety net: if this object is destroyed while a scripted dialogue coroutine is still
        // running (e.g. mid-sequence day transition), the coroutine is silently killed and never
        // reaches the normal exit cleanup. Dropping the slot clears its contribution to
        // IsScriptedModeActive so a future ShowPlayerUI() call is never blocked by a leaked lock.
        _clientIsParticipant = false;
        s_slots.Remove(this);
        if (Instance == this)
        {
            Instance = null;
            IsMegaphoneBarkActive = false;
        }
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        _sequenceRunning = false;
        _runningMegaphone = false;
        _runningMegaphoneLocked = false;
        _deferredExitHold = false;
        _reservedSpeakerNetId = 0;
        _pending.Clear();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only. A disconnected client is dropped from the participant set and any vote it
    /// cast is discarded, so the host left alone advances like single-player instead of starting
    /// (or waiting out) the multi-player countdown for a player who is gone.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || NetworkManager.Singleton == null || NetworkManager.Singleton.ShutdownInProgress) return;
        if (clientId == NetworkManager.ServerClientId) return;

        _leftParticipants.Remove(clientId);
        _advanceSet.Remove(clientId);
        _choiceSubmissions.Remove(clientId);
        _choicePlayerNames.Remove(clientId);

        if (!_participants.Remove(clientId)) return;

        Debug.Log($"[ScriptedDialogueRunner] Client {clientId} disconnected " +
                  $"({_participants.Count} participant(s) remaining).");

        if (!_serverSequenceActive && !_megaphoneSequenceActive) return;

        if (_abandonWhenEmpty && _participants.Count == 0)
        {
            AbandonSequence();
            return;
        }

        RecheckGatesAfterParticipantChange();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. True when <paramref name="clientId"/> is already in another conversation: a
    /// participant of a different busy session slot, or in a world (question) conversation with
    /// any NPC. Such a player is never seeded, pulled in or self-joined here — they finish their
    /// own conversation first and can then join this one by interacting with its speaker.
    /// </summary>
    private bool IsClientEngagedElsewhere(ulong clientId)
    {
        if (IsClientInOtherSession(clientId)) return true;
        return SpeakingInteraction.IsClientInAnyWorldConversation(clientId, _currentSpeakerTransform);
    }

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
    /// <para>
    /// <paramref name="participantRadius"/> overrides the inspector join radius for this
    /// sequence only (&lt;= 0 keeps the default). With <paramref name="lockOutsidePlayers"/>,
    /// only players within that radius are locked in at the start; others join (and are
    /// locked) by interacting with the speaker mid-conversation.
    /// </para>
    /// <para>
    /// <paramref name="useWorldDialogueCamera"/> makes every participant use the world-dialogue
    /// camera (player-camera dolly-in on the speaker, like <see cref="SuspectWorldDialogue"/> for scene
    /// NPCs) even when they count as inside, instead of the booth suspect cam.
    /// </para>
    /// <para>
    /// <paramref name="onAbandoned"/> (requires <paramref name="allowParticipantExit"/>): when every
    /// participant has backed out, the sequence stops immediately and this fires instead of
    /// <paramref name="onComplete"/>, so the caller can restart the dialogue from the beginning.
    /// When null, an emptied sequence keeps auto-advancing to the end as before.
    /// </para>
    /// </summary>
    public void PlayDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue,
        Action onComplete = null, bool deferExit = false, bool lockOutsidePlayers = false,
        ulong initialParticipantClientId = ulong.MaxValue, bool joinByInteractionOnly = false,
        bool allowParticipantExit = false, bool hideNonParticipantUI = true,
        float participantRadius = -1f, bool useWorldDialogueCamera = false, Action onAbandoned = null)
    {
        if (!IsServer) return;

        if (speaker == null || dialogue == null ||
            dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        if (!IsPrimary)
        {
            _primary.PlayDialogue(speaker, dialogue, onComplete, deferExit, lockOutsidePlayers,
                initialParticipantClientId, joinByInteractionOnly, allowParticipantExit,
                hideNonParticipantUI, participantRadius, useWorldDialogueCamera, onAbandoned);
            return;
        }

        NetworkObject speakerNetObj = speaker.GetComponent<NetworkObject>();
        Enqueue(new PendingSequence
        {
            Label = dialogue.name,
            IsMegaphone = false,
            SpeakerNetId = speakerNetObj != null ? speakerNetObj.NetworkObjectId : 0UL,
            Run = slot => slot.RunDialogue(
                speaker,
                dialogue,
                onComplete,
                deferExit,
                lockOutsidePlayers,
                initialParticipantClientId,
                joinByInteractionOnly,
                allowParticipantExit,
                hideNonParticipantUI,
                participantRadius,
                useWorldDialogueCamera,
                onAbandoned)
        });
    }

    // -------------------------------------------------------------------------
    // Session dispatch (server, primary)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server, primary only. True when a scripted conversation with <paramref name="speaker"/>
    /// would start immediately: no megaphone dialogue is running or waiting, the speaker has no
    /// running/held/queued session, and a slot is free. Player-initiated optional dialogues
    /// (booth intros, the Postman) check this and fall back instead of being queued.
    /// </summary>
    public bool CanStartSpeakerSequenceNow(SuspectCharacter speaker)
    {
        if (!IsServer || speaker == null) return false;
        if (!IsPrimary) return _primary.CanStartSpeakerSequenceNow(speaker);

        NetworkObject netObj = speaker.GetComponent<NetworkObject>();
        ulong speakerNetId = netObj != null ? netObj.NetworkObjectId : 0UL;

        foreach (var p in _pending)
        {
            if (p.SpeakerNetId == speakerNetId && !p.IsMegaphone) return false;
            if (p.IsMegaphone && (!p.Unlocked || p.ChainSlot != null)) return false;
        }

        bool anyFree = false;
        foreach (var s in s_slots)
        {
            if (s == null) continue;
            if (s._sequenceRunning && s._runningMegaphoneLocked) return false;
            if (s.IsSlotBusy && s._reservedSpeakerNetId == speakerNetId) return false;
            if (!s.IsSlotBusy) anyFree = true;
        }
        return anyFree;
    }

    private void Enqueue(PendingSequence request)
    {
        _pending.Add(request);
        TryStartPending();
    }

    /// <summary>
    /// Server, primary. Starts every pending request that may run now, in request order:
    /// <list type="bullet">
    /// <item>Character sequence: a free slot, no other session for its speaker, and no locked
    ///       megaphone running or waiting.</item>
    /// <item>Locked (or deferExit-chained) megaphone: every slot idle — its own chain slot only has
    ///       to finish its coroutine. While it waits, later character requests wait too so it
    ///       can't be starved.</item>
    /// <item>Unlocked megaphone / phone call: a free slot and no other megaphone playing; runs in
    ///       parallel with character conversations.</item>
    /// </list>
    /// </summary>
    private void TryStartPending()
    {
        if (!IsServer || _pending.Count == 0) return;

        bool anyMegaphoneRunning = false;
        bool lockedMegaphoneRunning = false;
        foreach (var s in s_slots)
        {
            if (s == null || !s._sequenceRunning || !s._runningMegaphone) continue;
            anyMegaphoneRunning = true;
            if (s._runningMegaphoneLocked) lockedMegaphoneRunning = true;
        }

        bool megaphoneWaiting = false;          // any earlier megaphone still queued (FIFO among megaphones)
        bool exclusiveMegaphoneWaiting = false; // an earlier locked/chained megaphone still queued
        var blockedSpeakers = new HashSet<ulong>();

        for (int i = 0; i < _pending.Count; i++)
        {
            PendingSequence p = _pending[i];
            ScriptedDialogueRunner slot = null;

            if (p.IsMegaphone)
            {
                if (p.ChainSlot != null && !p.ChainSlot._deferredExitHold)
                    p.ChainSlot = null; // hold already released (ExitScriptedMode) — run normally

                bool exclusive = !p.Unlocked || p.ChainSlot != null;
                if (exclusive)
                {
                    bool othersIdle = !anyMegaphoneRunning && !megaphoneWaiting;
                    foreach (var s in s_slots)
                    {
                        if (s == null || !othersIdle) continue;
                        othersIdle = s == p.ChainSlot ? !s._sequenceRunning : !s.IsSlotBusy;
                    }
                    if (othersIdle)
                        slot = p.ChainSlot != null ? p.ChainSlot : FirstFreeSlot();
                }
                else if (!anyMegaphoneRunning && !megaphoneWaiting)
                {
                    slot = FirstFreeSlot();
                }

                if (slot == null)
                {
                    megaphoneWaiting = true;
                    if (exclusive) exclusiveMegaphoneWaiting = true;
                    LogWaitOnce(p, exclusive
                        ? "waiting for every conversation to end before the (locking) megaphone plays"
                        : "waiting for another megaphone line or a free conversation slot");
                    continue;
                }
            }
            else
            {
                if (lockedMegaphoneRunning || exclusiveMegaphoneWaiting)
                {
                    LogWaitOnce(p, "waiting behind a megaphone dialogue that locks every player");
                    continue;
                }

                if (blockedSpeakers.Contains(p.SpeakerNetId) || IsSpeakerReserved(p.SpeakerNetId))
                {
                    blockedSpeakers.Add(p.SpeakerNetId);
                    LogWaitOnce(p, "its speaker is already in another conversation");
                    continue;
                }

                slot = FirstFreeSlot();
                if (slot == null)
                {
                    blockedSpeakers.Add(p.SpeakerNetId);
                    LogWaitOnce(p, $"all {s_slots.Count} conversation slots are busy");
                    continue;
                }
            }

            _pending.RemoveAt(i);
            i--;

            slot._sequenceRunning = true;
            slot._runningMegaphone = p.IsMegaphone;
            slot._runningMegaphoneLocked = p.IsMegaphone && (!p.Unlocked || p.ChainSlot == slot);
            if (p.ChainSlot == slot) slot._deferredExitHold = false;
            if (!p.IsMegaphone) slot._reservedSpeakerNetId = p.SpeakerNetId;
            if (p.IsMegaphone)
            {
                anyMegaphoneRunning = true;
                if (slot._runningMegaphoneLocked) lockedMegaphoneRunning = true;
            }

            slot.StartCoroutine(slot.RunSlotSequence(p));
        }
    }

    private static void LogWaitOnce(PendingSequence p, string reason)
    {
        if (p.LoggedWait) return;
        p.LoggedWait = true;
        Debug.Log($"[ScriptedDialogueRunner] '{p.Label}' queued — {reason}.");
    }

    private static ScriptedDialogueRunner FirstFreeSlot()
    {
        foreach (var s in s_slots)
            if (s != null && s.IsSpawned && !s.IsSlotBusy) return s;
        return null;
    }

    private static bool IsSpeakerReserved(ulong speakerNetId)
    {
        if (speakerNetId == 0) return false;
        foreach (var s in s_slots)
            if (s != null && s.IsSlotBusy && s._reservedSpeakerNetId == speakerNetId) return true;
        return false;
    }

    private IEnumerator RunSlotSequence(PendingSequence p)
    {
        Debug.Log($"[ScriptedDialogueRunner] Slot {s_slots.IndexOf(this)} running '{p.Label}'.");
        yield return StartCoroutine(p.Run(this));

        _sequenceRunning = false;
        _runningMegaphone = false;
        _runningMegaphoneLocked = false;
        if (!_deferredExitHold) _reservedSpeakerNetId = 0;

        var primary = IsPrimary ? this : _primary;
        primary.TryStartPending();
    }

    /// <summary>Server, primary: retries queued requests once per frame while any are waiting.</summary>
    private void ServerDispatchTick()
    {
        if (IsServer && IsPrimary && _pending.Count > 0)
            TryStartPending();
    }

    /// <summary>
    /// Switches the active override camera to the entry matching <paramref name="key"/> for every
    /// running session. An empty key deactivates any active override. Must be called on the server.
    /// </summary>
    public void SwitchCamera(string key)
    {
        if (!IsServer) return;
        foreach (var s in s_slots)
            if (s != null && s.IsSlotBusy) s.SwitchCameraLocal(key);
    }

    private void SwitchCameraLocal(string key)
    {
        _currentCameraKey = key ?? string.Empty;
        SetActiveOverrideCamClientRpc(key ?? string.Empty, BuildParticipantRpcParams());
    }

    /// <summary>
    /// Releases every session held open by a <c>deferExit: true</c> <see cref="PlayDialogue"/>
    /// (exits scripted mode for its participants and deactivates its override camera). Sessions of
    /// other conversations are untouched. Must be called on the server.
    /// </summary>
    public void ExitScriptedMode()
    {
        if (!IsServer) return;
        foreach (var s in s_slots.ToArray())
            if (s != null && s._deferredExitHold) s.ExitSlotScriptedMode();
        (IsPrimary ? this : _primary).TryStartPending();
    }

    /// <summary>
    /// Server-only. Force-ends every running or held scripted session for its participants (e.g.
    /// the intro cutscene or end-of-shift report taking over). Running sequences keep going with no
    /// participants and auto-advance to their end, so their completion callbacks still fire.
    /// </summary>
    public void ForceExitAllScriptedModes()
    {
        if (!IsServer) return;
        foreach (var s in s_slots.ToArray())
        {
            if (s == null || !(s.IsSlotBusy || s._serverSequenceActive || s._megaphoneSequenceActive)) continue;
            bool running = s._sequenceRunning;
            s.ExitSlotScriptedMode();
            if (running) s.BeginUnattendedDrain();
        }
    }

    /// <summary>Server-only. Exits scripted mode for this slot's participants and clears its session state.</summary>
    private void ExitSlotScriptedMode()
    {
        // Clear the server-side flag immediately so CheckProximityJoins stops running.
        _serverSequenceActive = false;
        _deferredExitHold = false;
        if (!_sequenceRunning) _reservedSpeakerNetId = 0;
        ulong speakerNetId = _currentSpeakerTransform != null
            ? _currentSpeakerTransform.GetComponent<NetworkObject>()?.NetworkObjectId ?? 0UL
            : 0UL;
        // Only participants were put into scripted mode (and had their HUD hidden), so only
        // they are restored — non-participants are never touched by this sequence. A running
        // megaphone sequence is the exception: it enters scripted mode for every client.
        ClientRpcParams exitRecipients = _megaphoneSequenceActive ? default : BuildParticipantRpcParams();
        _currentSpeakerTransform = null;
        _participants.Clear();
        _leftParticipants.Clear();
        _joinByInteractionOnly = false;
        _allowParticipantExit = false;
        _activeJoinRadius = -1f;
        _useWorldDialogueCamera = false;
        HideInWorldSubtitleClientRpc(speakerNetId);
        SetActiveDialogueSpeakerClientRpc(0);
        ExitScriptedModeClientRpc(false, exitRecipients);
    }

    // Server, per slot: a force-exited sequence auto-advances every remaining gate with nobody in it.
    private bool _drainUnattended;

    private void BeginUnattendedDrain()
    {
        _drainUnattended = true;
        TryAutoAdvanceIfUnattended();
    }

    /// <summary>
    /// Deactivates the active suspect camera and any override camera without exiting scripted
    /// mode, for every session held by deferExit (or, if none, every running session). Use when
    /// transitioning to a cinematic phase mid-session while keeping its players locked.
    /// Must be called on the server.
    /// </summary>
    public void ClearCamerasKeepMode()
    {
        if (!IsServer) return;
        bool anyHeld = false;
        foreach (var s in s_slots)
            if (s != null && s._deferredExitHold) anyHeld = true;

        foreach (var s in s_slots)
        {
            if (s == null) continue;
            if (anyHeld ? s._deferredExitHold : s._sequenceRunning)
                s.ClearCamerasKeepModeClientRpc(s.BuildParticipantRpcParams());
        }
    }

    [ClientRpc]
    private void ClearCamerasKeepModeClientRpc(ClientRpcParams rpcParams = default)
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
    /// <param name="speakerNameOverride">
    /// Optional subtitle speaker name for this sequence only, overriding the configured
    /// <see cref="_megaphoneSpeakerName"/>. Use for a one-off speaker distinct from the usual
    /// megaphone voice (e.g. the unfamiliar new voice that takes over on Day 4). Leave null to
    /// use the default name.
    /// </param>
    /// <param name="speakerColorOverride">
    /// Optional subtitle name colour paired with <paramref name="speakerNameOverride"/>. Leave
    /// null to use the default <see cref="_megaphoneSpeakerColor"/>.
    /// </param>
    /// <param name="useAlternateVoice">
    /// When true, audio is played from <see cref="MegaphoneDialogueManager.AlternateVoiceClips"/>
    /// instead of the default <see cref="MegaphoneDialogueManager.AudioClips"/>, so the one-off
    /// speaker sounds distinct from the usual megaphone voice.
    /// </param>
    /// <param name="useTelephoneAudioSource">
    /// When true, dialogue audio plays through <see cref="Telephone.VoiceAudioSource"/> (the
    /// phone handset speaker, which already carries an "old phone" filter chain) instead of
    /// <see cref="MegaphoneDialogueManager.MegaphoneAudioSource"/> (the world megaphone). Use
    /// for HQ calls answered over the phone rather than an in-world PA announcement. Resolved
    /// locally on each client via <see cref="Telephone.Instance"/> — no live reference is sent
    /// over the network.
    /// </param>
    public void PlayMegaphoneDialogue(
        ScriptedDialogue dialogue,
        Action onComplete = null,
        bool unlocked = false,
        string speakerNameOverride = null,
        Color? speakerColorOverride = null,
        bool useAlternateVoice = false,
        bool useTelephoneAudioSource = false)
    {
        if (!IsServer) return;

        if (dialogue == null || dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayMegaphoneDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        string speakerName = string.IsNullOrEmpty(speakerNameOverride) ? _megaphoneSpeakerName : speakerNameOverride;
        Color speakerColor = speakerColorOverride ?? _megaphoneSpeakerColor;

        if (!IsPrimary)
        {
            _primary.PlayMegaphoneDialogue(dialogue, onComplete, unlocked, speakerNameOverride,
                speakerColorOverride, useAlternateVoice, useTelephoneAudioSource);
            return;
        }

        // A megaphone requested while a deferExit session holds scripted mode is that session's
        // chained continuation (e.g. Day 1 soldier -> post-Alexei megaphone): it runs on the
        // same slot and takes over the hold instead of waiting behind it.
        ScriptedDialogueRunner chainSlot = null;
        foreach (var s in s_slots)
            if (s != null && s._deferredExitHold) { chainSlot = s; break; }

        Enqueue(new PendingSequence
        {
            Label = dialogue.name,
            IsMegaphone = true,
            Unlocked = unlocked,
            ChainSlot = chainSlot,
            Run = slot => slot.RunMegaphoneDialogue(dialogue, onComplete, unlocked, speakerName, speakerColor, useAlternateVoice, useTelephoneAudioSource)
        });
    }

    /// <summary>
    /// Plays a scripted dialogue as a megaphone <b>bark</b>: every line plays on its own, with no
    /// input and no continue chevron. Each line fully reveals, holds for
    /// <see cref="_barkLineHoldSeconds"/>, then the next one plays. The player is left completely
    /// alone: no dialogue lock, no camera change, no cutscene flag (so the hotbar, held items and
    /// zoom views keep working). The only restriction is that character conversations can't be
    /// started while it plays (<see cref="IsMegaphoneBarkActive"/>). Must be called on the server.
    /// Choice nodes and camera triggers on the asset are ignored.
    /// </summary>
    public void PlayMegaphoneBark(
        ScriptedDialogue dialogue,
        Action onComplete = null,
        string speakerNameOverride = null,
        Color? speakerColorOverride = null,
        bool useAlternateVoice = false)
    {
        if (!IsServer) return;

        if (dialogue == null || dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayMegaphoneBark called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        string speakerName = string.IsNullOrEmpty(speakerNameOverride) ? _megaphoneSpeakerName : speakerNameOverride;
        Color speakerColor = speakerColorOverride ?? _megaphoneSpeakerColor;

        StartCoroutine(RunMegaphoneBark(dialogue, onComplete, speakerName, speakerColor, useAlternateVoice));
    }

    // -------------------------------------------------------------------------
    // Input handling — E key and mouse click for all clients
    // -------------------------------------------------------------------------

    private void Update()
    {
        ServerDispatchTick();
        // Server (legacy, opt-in): late-join players who walk into proximity of the active
        // speaker. Off by default — mid-conversation arrivals join by interacting with the
        // speaker instead (SuspectCharacter.Interact -> RequestRejoinScriptedDialogueServerRpc).
        if (IsServer && _proximityJoinMidSequence && _serverSequenceActive && _currentSpeakerTransform != null)
            CheckProximityJoins();

        // _clientIsWaitingForInput is set on ALL clients via ClientRpc, so both the
        // host and non-host clients can skip reveals and advance the dialogue gate.
        if (!_clientIsWaitingForInput) return;

        // A world conversation (SuspectWorldDialogue) owns all dialogue input while the local
        // player is in it — e.g. they walked up and talked to a suspect during an unlocked
        // megaphone sequence. Without this, the same E / click / Back press both drives that
        // conversation AND advances or leaves this sequence, whose exit then tears the
        // conversation's dialogue lock down underneath it.
        if (SuspectWorldDialogue.IsLocalPlayerInConversation) return;

        // In a sequence that leaves the player free to move (not in scripted mode locally),
        // the Interact key belongs to world interaction whenever it's aimed at something
        // interactable — that press must not double as an advance vote.
        bool interactOwnedByWorld = !_clientIsParticipant && LocalPlayerHasWorldInteractTarget();

        // Q or the "Back" button lets the local player leave the scripted dialogue early —
        // checked before the advance/skip input so it never also fires an advance vote.
        // Only outside/bystander players may back out this way: they are never required to
        // be present for a scripted sequence. Booth-inside players are mandatory participants
        // (the sequence may be gating paperwork, tutorial steps, etc.) and must not be able to
        // bail out — this also prevents Q from being misread as "leave dialogue" immediately
        // after it was used to exit a diegetic view (tool locker, quarantine board, etc.) that
        // shares the same exit key, right as the player is pulled into a scripted sequence.
        bool pressedLeave = Input.GetKeyDown(KeyCode.Q)
                             || Input.GetButtonDown("Back")
                             || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
        if (pressedLeave && LocalUsesWorldDialoguePath)
        {
            LeaveScriptedDialogueServerRpc();
            return;
        }

        // Ignore clicks that land on any UI element (e.g. choice buttons).
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                      UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        bool pressedE       = GoodCopBadCop.Input.RebindableInput.GetKeyDown(GoodCopBadCop.Input.GameAction.Interact) && !interactOwnedByWorld;
        bool pressedClick   = Input.GetMouseButtonDown(0) && !overUI;
        bool pressedGamepad = AnyGamepadButtonThisFrame() &&
                              !(interactOwnedByWorld && GoodCopBadCop.Input.RebindableInput.GetGamepadDown(GoodCopBadCop.Input.GameAction.Interact));

        if (!pressedE && !pressedClick && !pressedGamepad) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // Unskippable line: ignore input until the reveal finishes on its own.
            if (_clientRevealUnskippable) return;

            // First input: complete the typewriter locally without advancing the line.
            // Each player independently skips their own reveal — no network call needed.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second input (or first when typewriter is already done): submit an advance vote.
        // The choice winner's echoed line is local presentation, so dismiss it immediately
        // instead of waiting for the other participant (or the advance timeout) to open the gate.
        DialogueManager.Instance.DismissOwnChoiceEchoOnAdvance();
        AdvanceScriptedLineServerRpc();
    }

    // -------------------------------------------------------------------------
    // Internal sequence
    // -------------------------------------------------------------------------

    /// <summary>
    /// True when the local player's reticle is on something the Interact key would act on
    /// (see <see cref="PlayerInteractionController.HasWorldInteractTarget"/>).
    /// </summary>
    private static bool LocalPlayerHasWorldInteractTarget()
    {
        PlayerInstance player = PlayerInstance.Instance;
        if (player == null) return false;
        PlayerInteractionController interaction = player.GetComponent<PlayerInteractionController>();
        return interaction != null && interaction.HasWorldInteractTarget;
    }

    /// <summary>
    /// True when the local client's scripted-mode exit should leave the player's dialogue lock
    /// alone: this sequence never locked the player (e.g. an unlocked megaphone sequence) and a
    /// world conversation they started meanwhile now owns the lock, cursor, choices and HUD.
    /// </summary>
    private bool WorldConversationOwnsLocalDialogueLock() =>
        !_clientEnteredDialogueLock && SuspectWorldDialogue.IsLocalPlayerInConversation;

    /// <summary>
    /// Returns true if any gamepad button was pressed this frame, excluding Start and Select
    /// (reserved for pause / guidebook).
    /// </summary>
    private static bool AnyGamepadButtonThisFrame()
    {
        Gamepad gp = Gamepad.current;
        if (gp == null) return false;
        return gp.buttonSouth.wasPressedThisFrame
            || gp.buttonNorth.wasPressedThisFrame
            || gp.buttonWest.wasPressedThisFrame
            || gp.buttonEast.wasPressedThisFrame
            || gp.leftShoulder.wasPressedThisFrame
            || gp.rightShoulder.wasPressedThisFrame
            || gp.leftTrigger.wasPressedThisFrame
            || gp.rightTrigger.wasPressedThisFrame
            || gp.leftStickButton.wasPressedThisFrame
            || gp.rightStickButton.wasPressedThisFrame
            || gp.dpad.up.wasPressedThisFrame
            || gp.dpad.down.wasPressedThisFrame
            || gp.dpad.left.wasPressedThisFrame
            || gp.dpad.right.wasPressedThisFrame;
    }

    private IEnumerator RunDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue,
        Action onComplete, bool deferExit = false, bool lockOutsidePlayers = false,
        ulong initialParticipantClientId = ulong.MaxValue, bool joinByInteractionOnly = false,
        bool allowParticipantExit = false, bool hideNonParticipantUI = true,
        float participantRadius = -1f, bool useWorldDialogueCamera = false, Action onAbandoned = null)
    {
        // The request may have waited in the dispatch queue — the speaker can be gone by now.
        NetworkObject speakerNetObj = speaker != null ? speaker.GetComponent<NetworkObject>() : null;
        if (speakerNetObj == null || !speakerNetObj.IsSpawned)
        {
            Debug.LogWarning($"[ScriptedDialogueRunner] '{(dialogue != null ? dialogue.name : "null")}' skipped — " +
                             "speaker was destroyed/despawned while the dialogue was queued.");
            try { onComplete?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            yield break;
        }

        ulong speakerNetId = speakerNetObj.NetworkObjectId;
        _activeJoinRadius = participantRadius;
        _useWorldDialogueCamera = useWorldDialogueCamera;
        _abandoned = false;
        _drainUnattended = false;

        Debug.Log($"[ScriptedDialogueRunner] RunDialogue — IsSpawned={IsSpawned}, IsServer={IsServer}, speakerNetId={speakerNetId}, deferExit={deferExit}");

        _lastAnimTrigger = string.Empty;
        _joinByInteractionOnly = joinByInteractionOnly;
        // A forced conversation asset must never let the player back out via the escape/Back
        // button, even if a caller mistakenly passes allowParticipantExit: true for it.
        _allowParticipantExit = allowParticipantExit && !dialogue.isForced;
        _abandonWhenEmpty = _allowParticipantExit && onAbandoned != null;
        // hideNonParticipantUI is retained for API compatibility only: non-participants never
        // lose their HUD any more (see the EnterScriptedModeClientRpc call below).

        // Set the server-side flag immediately so CheckProximityJoins works even if the
        // host client is not a participant (targeted RPCs won't reach a non-participant host).
        // The client-local IsScriptedModeActive is only set by the participant-targeted RPCs.
        _serverSequenceActive = true;

        // Broadcast to every client (regardless of participation) so a player who backed out,
        // or never joined, can still detect that talking to this speaker again should rejoin.
        SetActiveDialogueSpeakerClientRpc(speakerNetId);

        // Seed the participant set before broadcasting EnterScriptedMode so the advance
        // gate is correct from the very first line. Optional suspect intros seed only the
        // player who interacted; their partner can join later by interacting with the suspect.
        _currentSpeakerTransform = speaker.transform;
        SeedParticipants(lockOutsidePlayers, initialParticipantClientId);

        // Only engaged participants lose their HUD (EnterScriptedModeClientRpc is participant-
        // targeted and closes it). Non-participants — including out-of-range players who may be
        // overhearing — keep their HUD and controls intact.
        EnterScriptedModeClientRpc(speakerNetId, lockOutsidePlayers, _allowParticipantExit, _useWorldDialogueCamera, BuildParticipantRpcParams());
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            if (_abandoned) break;

            // Track the active camera key server-side so late-joining clients can catch up
            // to it via LateJoinClientRpc.
            _currentCameraKey = node.cameraTrigger ?? string.Empty;

            // Camera cut and text effect are set before the line starts so they're
            // visible from the very first word of the subtitle.
            // Target camera RPCs to participants — far-away players must not have their
            // camera hijacked by a dialogue they are not yet part of.
            SetActiveOverrideCamClientRpc(node.cameraTrigger ?? string.Empty, BuildParticipantRpcParams());
            SetWobbleClientRpc(ResolveWobbleProfileIndex(node.wobbleProfileOverride), speakerNetId);
            SetFontClientRpc(ResolveFontAssetIndex(node.fontOverride), speakerNetId);

            // Reset the previous trigger and force idle before firing the next animation.
            // This prevents a skipped trigger from continuing to play into the new line.
            TriggerSpeakerAnimationServer(speakerNetId, node.animationTrigger);
            SetSpeakerExpressionClientRpc(speakerNetId, node.expression ?? string.Empty);

            yield return StartCoroutine(SayAndWait(speaker, node.npcLine, node.playLaughSfx, node.unskippable));

            if (node.type == ScriptedDialogueNodeType.Choice && !_abandoned)
                yield return StartCoroutine(PlayChoiceNode(speaker, node, speakerNetId));
        }

        // Every participant backed out of an abandonable sequence: tear it down (they already
        // got their per-client exit) and report it as abandoned, not completed.
        if (_abandoned)
        {
            _abandoned = false;
            _abandonWhenEmpty = false;
            _serverSequenceActive = false;
            _currentSpeakerTransform = null;
            _participants.Clear();
            _leftParticipants.Clear();
            _joinByInteractionOnly = false;
            _allowParticipantExit = false;
            _activeJoinRadius = -1f;
            _useWorldDialogueCamera = false;
            HideInWorldSubtitleClientRpc(speakerNetId);
            SetActiveDialogueSpeakerClientRpc(0);
            yield return null;

            Debug.Log("[ScriptedDialogueRunner] Scripted dialogue abandoned — every participant left.");
            onAbandoned?.Invoke();
            yield break;
        }
        _abandonWhenEmpty = false;

        // When deferExit is true, scripted mode is kept active so the caller can chain
        // further sequences (e.g. a cutscene followed by megaphone dialogue) before
        // returning control to the player. The caller must eventually call ExitScriptedMode
        // or PlayMegaphoneDialogue (which exits mode when it completes).
        if (!deferExit)
        {
            // Only participants were put into scripted mode (and had their HUD hidden), so
            // capture the participant targets before clearing the server set and restore only them.
            ClientRpcParams exitRecipients = BuildParticipantRpcParams();

            // Clear the server-side flag before the client RPC so CheckProximityJoins stops.
            _serverSequenceActive = false;
            _currentSpeakerTransform = null;
            _participants.Clear();
            _leftParticipants.Clear();
            _joinByInteractionOnly = false;
            _allowParticipantExit = false;
            _activeJoinRadius = -1f;
            _useWorldDialogueCamera = false;
            HideInWorldSubtitleClientRpc(speakerNetId);
            SetActiveDialogueSpeakerClientRpc(0);
            ExitScriptedModeClientRpc(lockOutsidePlayers, exitRecipients);
            yield return null;
        }
        else
        {
            // Scripted mode stays held for the caller's chained follow-up; keep other sequences
            // queued until ExitScriptedMode or a chained megaphone releases it.
            _deferredExitHold = true;
        }

        onComplete?.Invoke();
        Debug.Log($"[ScriptedDialogueRunner] Scripted dialogue complete (deferExit={deferExit}).");
    }

    /// <summary>Says a line and waits for all connected players to advance (or the timeout to expire).</summary>
    private IEnumerator SayAndWait(SuspectCharacter speaker, string text, bool playLaughSfx = false, bool unskippable = false)
    {
        if (_abandoned) yield break; // sequence abandoned — don't start another line

        _awaitingScriptedInput = true;
        // Target the "input open" signal only to participants — non-participants must not
        // be able to send advance RPCs that interfere with the gate.
        SetAwaitingInputClientRpc(true, unskippable, BuildParticipantRpcParams());

        // While a bystander is disengaged from an otherwise-active dialogue, mirror the line
        // as a small floating bubble above the suspect's head for everyone (see
        // HasDisengagedBystander) instead of relying on the on-screen box they may not see.
        if (HasDisengagedBystander)
        {
            ulong speakerNetId = speaker.GetComponent<NetworkObject>().NetworkObjectId;
            ShowInWorldSubtitleClientRpc(speakerNetId, text, speaker.Speaking != null ? speaker.Speaking.SpeakerName : speaker.name, false);
        }

        DialogueManager.Instance.SayDialogue(speaker, text, waitForInput: true, playLaughSfx: playLaughSfx);
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
        if (_drainUnattended) TryAutoAdvanceIfUnattended();
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
        // set if they somehow weren't included at the proximity-seeding step. A player who is in
        // a different conversation never votes here (a stale input from them is dropped).
        if (!_participants.Contains(senderId))
        {
            if (IsClientEngagedElsewhere(senderId)) return;
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
        ShowAdvanceTimerClientRpc(_advanceTimeoutSeconds, BuildParticipantRpcParams());
        yield return new WaitForSeconds(_advanceTimeoutSeconds);
        OpenAdvanceGate();
    }

    /// <summary>
    /// Server: the key <see cref="DialogueManager"/> uses to scope a subtitle/voice advance to this
    /// conversation only — this slot's speaker NetworkObjectId, or
    /// <see cref="DialogueManager.MegaphoneSpeakerKey"/> for a megaphone/phone sequence.
    /// </summary>
    private ulong CurrentAdvanceKey
    {
        get
        {
            if (_megaphoneSequenceActive || _runningMegaphone || _currentSpeakerTransform == null)
                return DialogueManager.MegaphoneSpeakerKey;
            NetworkObject netObj = _currentSpeakerTransform.GetComponent<NetworkObject>();
            return netObj != null ? netObj.NetworkObjectId : DialogueManager.MegaphoneSpeakerKey;
        }
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

        HideAdvanceTimerClientRpc(BuildParticipantRpcParams());

        // Unblocks WaitForInputRoutine and clears the waiting line — scoped to THIS conversation's
        // speaker, so a parallel conversation's subtitle/voice on any client is left untouched.
        DialogueManager.Instance.AdvanceDialogueServerRpc(CurrentAdvanceKey);
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
    ///   <item>When <paramref name="lockOutsidePlayers"/> is <c>true</c> (forced outside
    ///         conversation), only clients within <see cref="EffectiveJoinRadius"/> are seeded
    ///         (fallback: the nearest client). Out-of-range players join on arrival.</item>
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
    private void SeedParticipants(bool lockOutsidePlayers = false, ulong initialParticipantClientId = ulong.MaxValue)
    {
        _participants.Clear();
        _leftParticipants.Clear();

        // Players already in another conversation (another session slot or a world conversation)
        // are never pulled in — they can join by interacting with this speaker once they're free.
        var available = new List<ulong>();
        foreach (ulong clientId in DevSpectatorRegistry.PlayerClientIds(NetworkManager.Singleton))
            if (!IsClientEngagedElsewhere(clientId)) available.Add(clientId);

        if (_joinByInteractionOnly && initialParticipantClientId != ulong.MaxValue)
        {
            if (!IsClientEngagedElsewhere(initialParticipantClientId))
            {
                _participants.Add(initialParticipantClientId);
                ReleaseFromUnlockedMegaphones(initialParticipantClientId);
            }
            Debug.Log($"[ScriptedDialogueRunner] SeedParticipants — optional intro started by client {initialParticipantClientId}.");
            return;
        }

        // No speaker to measure against — include everyone free so the dialogue is never unblockable.
        if (_currentSpeakerTransform == null)
        {
            _participants.UnionWith(available);
            LogSeedResult("all free clients seeded (no speaker transform)");
            return;
        }

        Vector3 speakerPos = _currentSpeakerTransform.position;

        // Forced outside conversations: lock in only players within range. An out-of-range
        // partner stays free (overhearing subtitles) and joins by interacting with the speaker.
        if (lockOutsidePlayers)
        {
            foreach (ulong clientId in available)
            {
                if (IsClientWithinJoinRadius(clientId, speakerPos))
                    _participants.Add(clientId);
            }

            // Fallback: nobody in range (e.g. the trigger player stepped back during the
            // settle beat) — take only the nearest free player, never the whole session.
            if (_participants.Count == 0 && TryGetNearestClient(speakerPos, available, out ulong nearest))
                _participants.Add(nearest);

            LogSeedResult($"forced: {_participants.Count} in range (radius={EffectiveJoinRadius})");
            return;
        }

        foreach (ulong clientId in available)
        {
            // Inside players are always in the booth and will always enter dialogue mode —
            // include them regardless of exact distance to avoid seeding race conditions
            // where a player is slightly outside _joinRadius at the moment of seeding.
            if (IsClientInsidePlayer(clientId) || IsClientWithinJoinRadius(clientId, speakerPos))
                _participants.Add(clientId);
        }

        // Fallback: if no free player is close, include every free player so the dialogue is never
        // unblockable.
        if (_participants.Count == 0)
            _participants.UnionWith(available);

        LogSeedResult($"{_participants.Count} participants seeded");
    }

    private void LogSeedResult(string summary)
    {
        foreach (ulong clientId in _participants)
            ReleaseFromUnlockedMegaphones(clientId);

        if (_participants.Count == 0)
            Debug.Log("[ScriptedDialogueRunner] SeedParticipants — every player is busy in another conversation; " +
                      "the dialogue waits on its first line until a player joins by interacting with the speaker.");
        else
            Debug.Log($"[ScriptedDialogueRunner] SeedParticipants — {summary} of {NetworkManager.Singleton.ConnectedClientsIds.Count} clients.");
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
    /// Called every frame on the server while a dialogue is active, only when
    /// <see cref="_proximityJoinMidSequence"/> is enabled.
    /// </summary>
    private void CheckProximityJoins()
    {
        if (_joinByInteractionOnly || _currentSpeakerTransform == null) return;

        Vector3 speakerPos = _currentSpeakerTransform.position;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (_participants.Contains(clientId)) continue;
            // Players who explicitly left must interact with the speaker again to rejoin —
            // proximity alone should not pull them back in.
            if (_leftParticipants.Contains(clientId)) continue;
            if (IsClientEngagedElsewhere(clientId)) continue;
            if (!IsClientWithinJoinRadius(clientId, speakerPos)) continue;

            JoinParticipant(clientId);
        }
    }

    /// <summary>
    /// Adds <paramref name="clientId"/> to <see cref="_participants"/> and sends the same
    /// camera/mode catch-up RPCs a proximity late-joiner receives. Shared by
    /// <see cref="CheckProximityJoins"/> and <see cref="RequestRejoinScriptedDialogueServerRpc"/>.
    /// Must be called on the server while a dialogue is active.
    /// </summary>
    private void JoinParticipant(ulong clientId)
    {
        _leftParticipants.Remove(clientId);
        _participants.Add(clientId);
        ReleaseFromUnlockedMegaphones(clientId);
        Debug.Log($"[ScriptedDialogueRunner] Client {clientId} joined/rejoined the dialogue.");

        ulong speakerNetId = _currentSpeakerTransform != null
            ? _currentSpeakerTransform.GetComponent<NetworkObject>()?.NetworkObjectId ?? 0UL
            : 0UL;
        var singleClientRpc = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        // The current camera key rides along with the join so the client applies it only after
        // caching the speaker ID — per-speaker keys (empty / SuspectCam / SuspectFaceCam) can't
        // resolve without it, and the joiner never received the participant-targeted
        // EnterScriptedModeClientRpc that normally sets it.
        LateJoinClientRpc(speakerNetId, _awaitingScriptedInput, _allowParticipantExit, _currentCameraKey ?? string.Empty, _useWorldDialogueCamera, singleClientRpc);

        // A player who joins while a response choice is open must receive the current panel,
        // not wait until the next choice node to participate.
        if (_awaitingScriptedChoice && _activeChoiceTexts != null && _activeChoiceTexts.Length >= 2)
            ShowChoicesClientRpc(_activeChoiceTexts[0], _activeChoiceTexts[1], singleClientRpc);

        // If everyone had left and the in-world subtitle bubbles were showing, hide the
        // suspect's bubble now that a normal participant is back — future lines resume the
        // standard on-screen dialogue for everyone.
        if (_leftParticipants.Count == 0 && speakerNetId != 0)
            HideInWorldSubtitleClientRpc(speakerNetId);
    }

    // -------------------------------------------------------------------------
    // Leave / rejoin — server-side
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by a client pressing Q or the Back button while a scripted line is awaiting
    /// input. Removes the sender from the advance/choice gates and restores their own movement,
    /// camera, and interaction — the dialogue keeps running for any remaining participants.
    /// If every participant has now left, the sequence auto-advances so it is never stuck.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void LeaveScriptedDialogueServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_serverSequenceActive || !_allowParticipantExit) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!_participants.Contains(senderId)) return;

        _participants.Remove(senderId);
        _leftParticipants.Add(senderId);
        _advanceSet.Remove(senderId);
        _choiceSubmissions.Remove(senderId);
        _choicePlayerNames.Remove(senderId);

        var single = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
        };
        ExitScriptedModeForClientRpc(single);

        Debug.Log($"[ScriptedDialogueRunner] Client {senderId} left the dialogue " +
                  $"({_participants.Count} participant(s) remaining).");

        if (_abandonWhenEmpty && _participants.Count == 0)
        {
            AbandonSequence();
            return;
        }

        RecheckGatesAfterParticipantChange();
    }

    /// <summary>
    /// Server-only. Stops an abandonable sequence (see <see cref="_abandonWhenEmpty"/>) where it is:
    /// releases whichever gate RunDialogue is waiting on without resolving a choice, so the
    /// coroutine falls through to its abandon cleanup.
    /// </summary>
    private void AbandonSequence()
    {
        if (_abandoned) return;
        _abandoned = true;

        if (_choiceTimerCoroutine != null)
        {
            StopCoroutine(_choiceTimerCoroutine);
            _choiceTimerCoroutine = null;
        }
        if (_awaitingScriptedChoice && !_choiceResolved)
        {
            _choiceResolved = true;
            _resolvedChoiceIndex = 0;
            HideAdvanceTimerClientRpc(BuildParticipantRpcParams());
        }

        // Also clears the current subtitle line on every client.
        if (_awaitingScriptedInput && !_scriptedAdvanceReady)
            OpenAdvanceGate();
    }

    /// <summary>
    /// Called by <see cref="SuspectCharacter.Interact"/> (on the slot running that speaker, see
    /// <see cref="FindSlotForSpeaker"/>) when a player who left — or never joined — the speaker's
    /// dialogue interacts with them. Rejoins the advance and choice gates and resumes wherever the
    /// dialogue presently is. Works for any player who is not in another scripted session, so a
    /// player who finished their own conversation can walk over and join a teammate's.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestRejoinScriptedDialogueServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_serverSequenceActive || _abandoned) return; // an abandoned sequence is shutting down

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (_participants.Contains(senderId)) return; // already in
        if (IsClientInOtherSession(senderId))
        {
            Debug.Log($"[ScriptedDialogueRunner] Client {senderId} can't join — already in another scripted conversation.");
            return;
        }

        JoinParticipant(senderId);
    }

    /// <summary>
    /// Server-only. True when <paramref name="clientId"/> is a participant of another busy session
    /// slot. Listeners of an unlocked megaphone/phone line don't count — they're free to move and
    /// get released from it when they join a conversation (<see cref="ReleaseFromUnlockedMegaphones"/>).
    /// </summary>
    private bool IsClientInOtherSession(ulong clientId)
    {
        for (int i = 0; i < s_slots.Count; i++)
        {
            var s = s_slots[i];
            if (s == null || s == this || s.IsRunningUnlockedMegaphone) continue;
            if ((s.IsSlotBusy || s._serverSequenceActive) && s._participants.Contains(clientId)) return true;
        }
        return false;
    }

    /// <summary>
    /// Server-only. <paramref name="clientId"/> just became a participant of this character
    /// session: drop them from any unlocked megaphone/phone line running in parallel, so its gate
    /// stops waiting on them and their input/screen belong to this conversation only.
    /// </summary>
    private void ReleaseFromUnlockedMegaphones(ulong clientId)
    {
        for (int i = 0; i < s_slots.Count; i++)
        {
            var s = s_slots[i];
            if (s == null || s == this || !s.IsRunningUnlockedMegaphone) continue;
            if (!s._participants.Remove(clientId)) continue;

            s._advanceSet.Remove(clientId);
            var single = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            s.SetAwaitingInputClientRpc(false, false, single);
            s.SetActiveOverrideCamClientRpc(string.Empty, single);
            Debug.Log($"[ScriptedDialogueRunner] Client {clientId} released from a parallel megaphone line to join a conversation.");
            s.RecheckGatesAfterParticipantChange();
        }
    }

    /// <summary>
    /// Restores normal gameplay state for a single client that left the dialogue, without
    /// disturbing <see cref="IsScriptedModeActive"/> or camera state for anyone else.
    /// </summary>
    [ClientRpc]
    private void ExitScriptedModeForClientRpc(ClientRpcParams rpcParams = default)
    {
        _clientIsParticipant = false;
        _clientIsWaitingForInput = false;
        _clientRevealUnskippable = false;
        HideLeaveBackButtonLocal();
        DialogueAdvanceTimer.Instance?.Hide();

        DeactivateOverrideCam();
        RestoreSpeakerLook();

        // Same ownership rule as ExitScriptedModeClientRpc: never tear down a world
        // conversation's dialogue lock that this sequence didn't put the player into.
        if (WorldConversationOwnsLocalDialogueLock())
        {
            _clientSpeakerNetId = 0;
            return;
        }
        _clientEnteredDialogueLock = false;

        DialogueChoiceSystem.Instance?.HideChoicePanel();

        // Unconditional clear — mirrors ExitScriptedModeClientRpc so a player who leaves early
        // via Q/Back while never movement-locked (e.g. an unlocked outside sequence) doesn't
        // stay permanently immune/untargetable.
        PlayerInstance.Instance?.SetIsInCutscene(false);

        if (PlayerInstance.Instance == null)
        {
            UIController.Instance?.ShowPlayerUI();
            _clientSpeakerNetId = 0;
            return;
        }

        // The exit reads the speaker (LocalSpeakerNetId) to block re-entry / drop its cam, so the
        // speaker id is only cleared afterwards.
        if (LocalUsesWorldDialoguePath)
            DialogueChoiceSystem.Instance?.ExitScriptedDialogueModeOutside();
        else
            DialogueChoiceSystem.Instance?.ExitScriptedDialogueMode();
        _clientWorldCameraMode = false;
        _clientSpeakerNetId = 0;

        // ExitScriptedDialogueMode clears the dialogue flag that ShowPlayerUI guards against.
        // Restoring the HUD before that exit can leave it hidden after a player leaves early.
        UIController.Instance?.ShowPlayerUI();
    }

    /// <summary>
    /// If no participants remain (every player backed out mid-line or mid-choice), immediately
    /// starts the same auto-advance timer normally kicked off by the first submission, so the
    /// sequence continues on its own instead of waiting forever for input that will never come.
    /// </summary>
    private void TryAutoAdvanceIfUnattended()
    {
        if (_participants.Count > 0) return;

        if (_awaitingScriptedInput && !_scriptedAdvanceReady && _advanceTimerCoroutine == null)
            _advanceTimerCoroutine = StartCoroutine(AdvanceTimeoutCoroutine());

        if (_awaitingScriptedChoice && !_choiceResolved && _choiceTimerCoroutine == null)
            _choiceTimerCoroutine = StartCoroutine(ChoiceTimeoutCoroutine());
    }

    /// <summary>
    /// Re-evaluates the advance and choice gates after <see cref="_participants"/> shrinks
    /// (a player left). Opens a gate immediately if the remaining participants have already
    /// all submitted, and otherwise falls back to <see cref="TryAutoAdvanceIfUnattended"/> for
    /// the fully-unattended case.
    /// </summary>
    private void RecheckGatesAfterParticipantChange()
    {
        int required = Mathf.Max(1, _participants.Count);

        if (_awaitingScriptedInput && !_scriptedAdvanceReady && _advanceSet.Count >= required)
        {
            OpenAdvanceGate();
        }
        else if (_awaitingScriptedChoice && !_choiceResolved && _choiceSubmissions.Count >= required)
        {
            ResolveChoices();
        }
        else if (_participants.Count == 1)
        {
            CancelGateTimersForSolo();
        }
        else
        {
            TryAutoAdvanceIfUnattended();
        }
    }

    /// <summary>
    /// Server-only. Exactly one participant remains and they haven't submitted yet: stop any
    /// countdown the departed player started, so the gate waits for the remaining player's own
    /// input — the same as single-player, where no timer is ever shown.
    /// </summary>
    private void CancelGateTimersForSolo()
    {
        bool hadTimer = false;

        if (_advanceTimerCoroutine != null && !_scriptedAdvanceReady)
        {
            StopCoroutine(_advanceTimerCoroutine);
            _advanceTimerCoroutine = null;
            hadTimer = true;
        }
        if (_choiceTimerCoroutine != null && !_choiceResolved)
        {
            StopCoroutine(_choiceTimerCoroutine);
            _choiceTimerCoroutine = null;
            hadTimer = true;
        }

        if (hadTimer)
            HideAdvanceTimerClientRpc(BuildParticipantRpcParams());
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

        return Vector3.Distance(origin, client.PlayerObject.transform.position) <= EffectiveJoinRadius;
    }

    /// <summary>Finds the candidate client whose PlayerObject is closest to <paramref name="origin"/>.</summary>
    private bool TryGetNearestClient(Vector3 origin, List<ulong> candidates, out ulong nearestClientId)
    {
        nearestClientId = ulong.MaxValue;
        float best = float.MaxValue;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null || !candidates.Contains(client.ClientId)) continue;
            float d = Vector3.Distance(origin, client.PlayerObject.transform.position);
            if (d < best) { best = d; nearestClientId = client.ClientId; }
        }
        return nearestClientId != ulong.MaxValue;
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
    private void LateJoinClientRpc(ulong speakerNetId, bool isWaitingForInput, bool canLeave,
        string cameraKey, bool worldCamera = false, ClientRpcParams rpcParams = default)
    {
        _clientIsParticipant = true;
        _clientIsWaitingForInput = isWaitingForInput;
        _clientWorldCameraMode = worldCamera;

        // Proximity/late join is a forced conversation — cancel any open gameplay Yes/No popup.
        UIController.Instance?.CancelGameplayConfirmation();

        // Late joiners never received the participant-targeted EnterScriptedModeClientRpc, so
        // cache the speaker here. Without it SuspectController.ResolveCurrentDialogueSpeakerCam
        // falls back to the booth's current suspect cam (e.g. joining Vlad's Day 2 tool locker
        // talk inside the booth cut to the booth suspect instead of Vlad).
        _clientSpeakerNetId = speakerNetId;

        // Catch up to the camera the sequence is currently on, now that the speaker is known.
        ApplyOverrideCam(cameraKey);

        // A proximity/late join is also a forced conversation — drop the phone first.
        Telephone.Instance?.ForceHangUpForDialogue();

        // A late join always takes the dialogue lock — release any world conversation first
        // (same reason as in EnterScriptedModeClientRpc).
        if (PlayerInstance.Instance != null)
            SuspectWorldDialogue.EndAllLocalConversations();

        UIController.Instance?.ClosePlayerUI();

        if (PlayerInstance.Instance == null) return;

        Transform lookTarget = null;
        if (speakerNetId != 0 &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            lookTarget = netObj.transform;

        if (canLeave)
            ShowLeaveBackButtonLocal();

        // Mirror EnterScriptedModeClientRpc: outside players get movement-locked but must NOT
        // receive the booth suspect-cam activation. Calling EnterScriptedDialogueMode for an
        // outside player activates SuspectController.SetSuspectCamActive(true) and disables the
        // interaction controller, leaving the cam and interaction lock permanently stuck because
        // ExitScriptedModeClientRpc only calls ExitScriptedDialogueMode for inside players.
        if (LocalUsesWorldDialoguePath)
            EnterOutsideDialogueFacingSpeaker(lookTarget);
        else
            DialogueChoiceSystem.Instance?.EnterScriptedDialogueMode(lookTarget);
        _clientEnteredDialogueLock = true;

        if (_clientWorldCameraMode)
            BeginSpeakerLookAtLocalCamera(lookTarget);

        Debug.Log($"[ScriptedDialogueRunner] LateJoinClientRpc — client {NetworkManager.Singleton.LocalClientId} " +
                  $"entered dialogue mode via proximity (outside={PlayerInstance.Instance.IsOutsideLocal}), " +
                  $"isWaitingForInput={isWaitingForInput}.");
    }

    // -------------------------------------------------------------------------
    // Megaphone sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunMegaphoneDialogue(
        ScriptedDialogue dialogue,
        Action onComplete,
        bool unlocked,
        string speakerName,
        Color speakerColor,
        bool useAlternateVoice,
        bool useTelephoneAudioSource = false)
    {
        // Megaphone lines and the awaiting-input signal reach every listener, so every listener
        // must count toward the advance gate. Without this, _participants is empty (or stale from
        // a previous sequence) for a standalone megaphone sequence, and the first advancer
        // self-joins as the only participant — required == 1 — letting one player skip every line
        // instantly with no countdown for the other. A LOCKED megaphone runs alone (dispatcher) and
        // takes every player; an UNLOCKED one runs in parallel with conversations and only takes
        // players who are free right now — players talking to an NPC are left completely alone.
        _participants.Clear();
        _leftParticipants.Clear();
        foreach (ulong clientId in DevSpectatorRegistry.PlayerClientIds(NetworkManager.Singleton))
            if (!unlocked || !IsClientEngagedElsewhere(clientId)) _participants.Add(clientId);

        // When unlocked, the player is already free (caller called ExitScriptedMode first).
        // Skipping EnterScriptedModeClientRpc keeps movement and the first-person camera active
        // so the player can reach whatever the instruction is asking them to do.
        if (!unlocked)
        {
            _megaphoneSequenceActive = true;
            EnterScriptedModeClientRpc(0UL);
        }
        else
            // Still mark the player as "in cutscene" for mutant-immunity purposes even though
            // movement and the camera stay free — ExitScriptedModeClientRpc below always clears
            // this again once the sequence ends.
            SetCutsceneImmunityClientRpc(true, MegaphoneTargets);

        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            SetActiveOverrideCamClientRpc(node.cameraTrigger ?? string.Empty, MegaphoneTargets);
            SetWobbleClientRpc(ResolveWobbleProfileIndex(node.wobbleProfileOverride), 0UL, MegaphoneTargets);
            SetFontClientRpc(ResolveFontAssetIndex(node.fontOverride), 0UL, MegaphoneTargets);
            yield return StartCoroutine(SayMegaphoneLineAndWait(node.npcLine, speakerName, speakerColor, useAlternateVoice, useTelephoneAudioSource));
            SetSpeakerSpeakingClientRpc(false);
        }

        // The exit below also ends any chained (deferExit) dialogue — including its speaker
        // registration, so that NPC no longer reads as "in a conversation".
        if (_currentSpeakerTransform != null)
        {
            NetworkObject chainedSpeaker = _currentSpeakerTransform.GetComponent<NetworkObject>();
            if (chainedSpeaker != null) HideInWorldSubtitleClientRpc(chainedSpeaker.NetworkObjectId);
            _currentSpeakerTransform = null;
            SetActiveDialogueSpeakerClientRpc(0);
        }
        // Captured before clearing: everyone for a locked megaphone, its listeners for an unlocked one.
        ClientRpcParams exitRecipients = MegaphoneTargets;
        _megaphoneSequenceActive = false;
        _serverSequenceActive = false;
        _deferredExitHold = false;
        _participants.Clear();
        _leftParticipants.Clear();
        ExitScriptedModeClientRpc(false, exitRecipients);
        yield return null;

        onComplete?.Invoke();
        Debug.Log("[ScriptedDialogueRunner] Megaphone dialogue complete.");
    }

    private IEnumerator SayMegaphoneLineAndWait(string text, string speakerName, Color speakerColor, bool useAlternateVoice, bool useTelephoneAudioSource = false)
    {
        _awaitingScriptedInput = true;
        SetAwaitingInputClientRpc(true, false, MegaphoneTargets);
        SayMegaphoneLineClientRpc(text, speakerName, speakerColor, useAlternateVoice, useTelephoneAudioSource,
            waitForInput: true, MegaphoneTargets);
        yield return StartCoroutine(WaitForScriptedAdvance());
        SetAwaitingInputClientRpc(false);
        _awaitingScriptedInput = false;
    }

    /// <summary>
    /// Server: recipients of this slot's megaphone RPCs — every client for a locked megaphone
    /// (it runs alone), only its listeners for an unlocked one running beside conversations.
    /// </summary>
    private ClientRpcParams MegaphoneTargets => _megaphoneSequenceActive ? default : BuildParticipantRpcParams();

    /// <summary>
    /// Server: plays every line of <paramref name="dialogue"/> as an input-free bark (see
    /// <see cref="PlayMegaphoneBark"/>). Deliberately leaves the advance gate, participants,
    /// scripted mode and cutscene state untouched, so it can't interfere with the player or with
    /// any other sequence.
    /// </summary>
    private IEnumerator RunMegaphoneBark(
        ScriptedDialogue dialogue,
        Action onComplete,
        string speakerName,
        Color speakerColor,
        bool useAlternateVoice)
    {
        SetMegaphoneBarkActiveClientRpc(true);
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.npcLine)) continue;

            SetWobbleClientRpc(ResolveWobbleProfileIndex(node.wobbleProfileOverride), BarkSpeakerKey);
            SetFontClientRpc(ResolveFontAssetIndex(node.fontOverride), BarkSpeakerKey);
            SayMegaphoneLineClientRpc(node.npcLine, speakerName, speakerColor, useAlternateVoice,
                useTelephoneAudioSource: false, waitForInput: false);

            yield return StartCoroutine(WaitForBarkLineReveal(node.npcLine.Length));
            yield return new WaitForSeconds(_barkLineHoldSeconds);

            SetSpeakerSpeakingClientRpc(false);
        }

        // The last line's subtitle fades out on its own display timer (waitForInput: false).
        SetMegaphoneBarkActiveClientRpc(false);
        yield return null;

        onComplete?.Invoke();
        Debug.Log("[ScriptedDialogueRunner] Megaphone bark complete.");
    }

    /// <summary>
    /// Server: waits until the bark line just sent has finished its typewriter reveal on this
    /// (host) machine. A short start grace covers the ClientRpc reaching the local client; a
    /// length-based floor and <see cref="_barkRevealTimeoutSeconds"/> keep it bounded if no
    /// subtitle is visible here.
    /// </summary>
    private IEnumerator WaitForBarkLineReveal(int textLength)
    {
        const float startGrace = 0.25f;
        float minWait = Mathf.Max(startGrace, textLength * 0.015f);
        float elapsed = 0f;

        while (elapsed < _barkRevealTimeoutSeconds)
        {
            bool revealing = DialogueManager.Instance != null && DialogueManager.Instance.IsAnySubtitleRevealing();
            if (!revealing && elapsed >= minWait) yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    [ClientRpc]
    private void SetMegaphoneBarkActiveClientRpc(bool active)
    {
        IsMegaphoneBarkActive = active;
    }

    /// <summary>
    /// Displays a subtitle line using the given megaphone speaker identity and plays its audio
    /// through <see cref="MegaphoneDialogueManager"/> (or, when <paramref name="useTelephoneAudioSource"/>
    /// is set, through <see cref="Telephone.VoiceAudioSource"/> instead — e.g. for HQ calls
    /// answered over the phone). Uses the standard dialogue audio slot so the player clicking to
    /// advance stops the audio, matching character dialogue behaviour. With
    /// <paramref name="waitForInput"/> false (barks) the line shows no continue chevron and
    /// fades out on its own display timer.
    /// </summary>
    [ClientRpc]
    private void SayMegaphoneLineClientRpc(string text, string speakerName, Color speakerColor, bool useAlternateVoice,
        bool useTelephoneAudioSource = false, bool waitForInput = true, ClientRpcParams rpcParams = default)
    {
        var mgr = MegaphoneDialogueManager.Instance;
        AudioClip[] clips = mgr != null
            ? (useAlternateVoice ? mgr.AlternateVoiceClips : mgr.AudioClips)
            : System.Array.Empty<AudioClip>();

        AudioSource source;
        if (useTelephoneAudioSource)
        {
            // Phone calls use the handset's own voice clips (when assigned) and play in-ear for
            // whoever is holding the handset; everyone else hears it 2D at a lower volume so the
            // whole team hears the call (subtitles are already shown to every client).
            var phone = Telephone.Instance;
            source = phone != null ? phone.PrepareVoiceSourceForLocalListener(audibleToEveryone: true) : null;
            if (phone != null && phone.ScriptedCallVoiceClips.Length > 0)
                clips = phone.ScriptedCallVoiceClips;
        }
        else
        {
            source = mgr != null ? mgr.MegaphoneAudioSource : null;
        }

        if (!useTelephoneAudioSource)
            mgr?.SetSpeakerSpeaking(true);

        // A bark (no input) is ambient: a player busy in a conversation still hears it, but its
        // subtitle would evict their own conversation's line, so it isn't shown to them.
        bool showSubtitle = waitForInput || !OverhearRange.IsLocalPlayerConversationParticipant;
        if (showSubtitle)
            DialogueManager.Instance.SpawnSubtitles(text, speakerName, speakerColor,
                isPlayer: false, clearHistory: false, waitForInput: waitForInput,
                speakerNetId: DialogueManager.MegaphoneSpeakerKey);

        if (clips.Length > 0 && source != null)
            DialogueManager.Instance.PlayDialogueAudio(text, clips, source, ownerKey: DialogueManager.MegaphoneSpeakerKey);
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
        _awaitingScriptedChoice = true;
        _activeChoiceTexts = new[] { node.choices[0].playerChoiceText, node.choices[1].playerChoiceText };
        ShowChoicesClientRpc(_activeChoiceTexts[0], _activeChoiceTexts[1], BuildParticipantRpcParams());
        if (_drainUnattended) TryAutoAdvanceIfUnattended();

        yield return new WaitUntil(() => _choiceResolved);
        _awaitingScriptedChoice = false;
        _activeChoiceTexts = null;

        // Everyone backed out of an abandonable sequence — no response, no choice echo.
        if (_abandoned) yield break;

        // Broadcast the finalized choice: hide the panel, clear highlights, and show the
        // winning player's spoken line as a subtitle on all clients.
        var chosen = node.choices[_resolvedChoiceIndex];
        string winnerName = FindWinnerName(_resolvedChoiceIndex);
        ulong winnerClientId = FindWinnerClientId(_resolvedChoiceIndex);
        FinalizeChoiceClientRpc(chosen.playerChoiceText, winnerName, winnerClientId);

        // Mirror the chosen line above the winning player's head for any disengaged bystander,
        // as if they'd said it out loud.
        if (HasDisengagedBystander)
        {
            if (winnerClientId != ulong.MaxValue &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(winnerClientId, out var winnerClient) &&
                winnerClient.PlayerObject != null)
            {
                ShowInWorldSubtitleClientRpc(winnerClient.PlayerObject.NetworkObjectId, chosen.playerChoiceText, winnerName, true);
            }
        }

        yield return null; // flush the RPC before playing the NPC response

        TriggerSpeakerAnimationServer(speakerNetId, chosen.animationTrigger);
        SetSpeakerExpressionClientRpc(speakerNetId, chosen.expression ?? string.Empty);

        yield return StartCoroutine(SayAndWait(speaker, chosen.npcResponse, chosen.playLaughSfx));

        if (!string.IsNullOrWhiteSpace(chosen.npcFollowUp))
            yield return StartCoroutine(SayAndWait(speaker, chosen.npcFollowUp));
    }

    // -------------------------------------------------------------------------
    // Client RPCs — mode
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void SetActiveDialogueSpeakerClientRpc(ulong speakerNetId)
    {
        _broadcastSpeakerNetId = speakerNetId;

        // Sequence ended (or a new one started): drop an echo this client only overheard, but
        // never a newer echo that has since replaced it (e.g. from world dialogue).
        if (_overheardChoiceEcho != null && DialogueManager.Instance != null &&
            DialogueManager.Instance.ActiveChoiceEcho == _overheardChoiceEcho)
            DialogueManager.Instance.HideChoiceEcho();
        _overheardChoiceEcho = null;
    }

    /// <summary>
    /// True while at least one participant is still engaged in the dialogue and at least one
    /// other player has backed out. Only in this "mixed" state do the small in-world subtitle
    /// bubbles appear above the suspect's and active player's heads — in single-player, or once
    /// every player has backed out, the dialogue simply continues using the normal on-screen
    /// subtitle box for whoever remains.
    /// </summary>
    private bool HasDisengagedBystander => _leftParticipants.Count > 0 && _participants.Count > 0;

    /// <summary>
    /// Shows a small world-space subtitle bubble above <paramref name="ownerNetId"/>'s head
    /// (a suspect or a player). Only sent while <see cref="HasDisengagedBystander"/> is true.
    /// </summary>
    [ClientRpc]
    private void ShowInWorldSubtitleClientRpc(ulong ownerNetId, string text, string speakerName, bool isPlayerLine)
    {
        if (ownerNetId == 0) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetId, out var netObj)) return;

        // Bystander bubbles are only visible within overhear range of whoever is speaking.
        if (!OverhearRange.IsLocalPlayerWithin(netObj.transform)) return;

        var anchor = netObj.GetComponent<InWorldSubtitleAnchor>();
        anchor?.Subtitle?.ShowLine(text, speakerName, isPlayerLine ? Color.cyan : Color.white);
    }

    [ClientRpc]
    private void HideInWorldSubtitleClientRpc(ulong ownerNetId)
    {
        if (ownerNetId == 0) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetId, out var netObj)) return;

        netObj.GetComponent<InWorldSubtitleAnchor>()?.Subtitle?.Hide();
    }

    /// <summary>
    /// Sets (or clears) mutant-immunity/no-targeting for the local player without touching
    /// movement, camera, or <see cref="DialogueChoiceSystem.IsInDialogueMode"/>. Used for
    /// dialogue sequences that deliberately leave the player free to move (e.g. an unlocked
    /// megaphone instruction) but must still count as "in dialogue" for combat purposes.
    /// </summary>
    [ClientRpc]
    private void SetCutsceneImmunityClientRpc(bool value, ClientRpcParams rpcParams = default)
    {
        PlayerInstance.Instance?.SetIsInCutscene(value);
    }

    [ClientRpc]
    private void EnterScriptedModeClientRpc(ulong speakerNetId, bool lockOutsidePlayers = false,
        bool canLeave = false, bool worldCamera = false, ClientRpcParams rpcParams = default)
    {
        _clientIsParticipant = true;
        _clientWorldCameraMode = worldCamera;

        // A forced conversation cancels any open gameplay Yes/No popup (e.g. trash-can discard)
        // before anything else. Some branches below (outside player, no lock) never reach
        // DialogueChoiceSystem's OnDialogueModeEntering, so cancel explicitly here.
        UIController.Instance?.CancelGameplayConfirmation();

        // Always exit any open diegetic view (tool locker, quarantine board, etc.) immediately,
        // regardless of the inside/outside/lockOutsidePlayers branching below. Some of those
        // branches (outside player + lockOutsidePlayers == false) return early without ever
        // calling DialogueChoiceSystem.EnterScriptedDialogueMode/…Outside — which is the only
        // place that otherwise closes the view — leaving the view's own input handling (mouse
        // interactions, exit key) active underneath the incoming dialogue and blocking it.
        DiegeticViewController.Current?.Close();

        // Hang up the phone (and close the HQ Order Screen) if this participant is holding it.
        // Must run before ClosePlayerUI and the dialogue lock below — the instant put-down
        // restores the HUD/control the order screen had taken, which the lock then re-takes.
        Telephone.Instance?.ForceHangUpForDialogue();

        // If this sequence is about to take the local player's dialogue lock (inside players, or
        // outside players with lockOutsidePlayers), hand any world conversation they're in off
        // cleanly first. Otherwise the lock is taken over underneath it, our exit later releases
        // it (we own it now), and SuspectWorldDialogue is left with _inConversation stuck true:
        // the player can walk around but BeginConversation no-ops forever, the server keeps them
        // in that NPC's participant set (receiving other players' choices and blocking the vote),
        // and IsLocalPlayerInConversation swallows their input for every later sequence.
        if (PlayerInstance.Instance != null && (!PlayerInstance.Instance.IsOutsideLocal || lockOutsidePlayers))
            SuspectWorldDialogue.EndAllLocalConversations();

        UIController.Instance?.ClosePlayerUI();

        // Cache the speaker ID on all clients so SuspectController can resolve per-character cameras
        // via CurrentSpeakerNetId — must be stored before any early return below.
        _clientSpeakerNetId = speakerNetId;

        Debug.Log($"[ScriptedDialogueRunner] EnterScriptedModeClientRpc — " +
                  $"PlayerInstance={PlayerInstance.Instance != null}, " +
                  $"IsOutsideLocal={PlayerInstance.Instance?.IsOutsideLocal}, " +
                  $"ChoiceSystemInstance={DialogueChoiceSystem.Instance != null}");

        if (PlayerInstance.Instance == null) return;

        // Always mark the participant as "in cutscene" regardless of whether their movement
        // gets locked below — mutants must never target or damage a player who is actively
        // participating in a scripted dialogue, even one that leaves them free to walk around
        // (e.g. an unlocked outside sequence). This is separate from DialogueChoiceSystem's
        // movement/camera lock, which only applies in the branches below.
        PlayerInstance.Instance.SetIsInCutscene(true);

        // Resolve the speaker's transform so the player can rotate to face them.
        Transform lookTarget = null;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            lookTarget = netObj.transform;

        if (canLeave)
            ShowLeaveBackButtonLocal();

        if (LocalUsesWorldDialoguePath)
        {
            // Outside players get movement-locked when explicitly requested, but never get the
            // booth suspect-cam activated — camera cuts are handled by SetActiveOverrideCamClientRpc.
            // A world-camera sequence always locks its participants this way.
            if (lockOutsidePlayers || _clientWorldCameraMode)
            {
                EnterOutsideDialogueFacingSpeaker(lookTarget);
                _clientEnteredDialogueLock = true;
            }

            // World-camera sequences behave like a world-character conversation: the speaker's
            // head tracks this client's rendering camera (see SuspectWorldDialogue.StartConversation).
            if (_clientWorldCameraMode)
                BeginSpeakerLookAtLocalCamera(lookTarget);
            return;
        }

        DialogueChoiceSystem.Instance.EnterScriptedDialogueMode(lookTarget);
        _clientEnteredDialogueLock = true;
    }

    /// <summary>
    /// Locks an outside player into dialogue and frames the speaker's head. The speaker's
    /// NetworkObject root sits at its feet, so it must never be passed straight to the camera dolly.
    /// </summary>
    private static void EnterOutsideDialogueFacingSpeaker(Transform speakerRoot)
    {
        if (DialogueChoiceSystem.Instance == null) return;

        // A per-speaker framing override (e.g. a tall mutated form) wins while its form is showing.
        DialogueCameraFraming framing = DialogueCameraFraming.FindActive(speakerRoot);
        if (framing != null)
        {
            Transform aim = framing.ResolveAimTarget();
            Debug.Log($"[ScriptedDialogueRunner] Using DialogueCameraFraming on '{framing.name}' — aim point {aim?.position} " +
                      $"(drop {framing.AimDropBelowTarget}, camera offset {framing.CameraVerticalOffset}, standoff {framing.StandoffDistance}).", framing);
            DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(
                aim, framing.CameraVerticalOffset, framing.AimDropBelowTarget, framing.StandoffDistance);
            return;
        }

        SuspectWorldDialogue worldDialogue = speakerRoot != null ? speakerRoot.GetComponentInChildren<SuspectWorldDialogue>(true) : null;
        DialogueChoiceSystem.Instance.EnterScriptedDialogueModeOutside(
            ResolveSpeakerHead(speakerRoot, worldDialogue),
            worldDialogue != null ? worldDialogue.DialogueCameraVerticalOffset : 0f);
    }

    // Local: the speaker FLookAnimator retargeted at this client's camera by a world-camera
    // sequence, and what it followed before, so ExitScriptedModeClientRpc can hand it back.
    private static FIMSpace.FLook.FLookAnimator _speakerLookAnimator;
    private static Transform _speakerPreviousLookTarget;
    private static Transform _speakerAssignedLookTarget;

    /// <summary>
    /// Points the speaker's <see cref="FIMSpace.FLook.FLookAnimator"/> at the local rendering
    /// camera (Camera.main, i.e. the CinemachineBrain output), mirroring
    /// <see cref="SuspectWorldDialogue"/>'s conversation look. Local-only: each client sees the
    /// speaker look at its own camera. Restored by <see cref="RestoreSpeakerLook"/>.
    /// </summary>
    private static void BeginSpeakerLookAtLocalCamera(Transform speakerRoot)
    {
        if (speakerRoot == null) return;

        SuspectCharacter character = speakerRoot.GetComponent<SuspectCharacter>();
        FIMSpace.FLook.FLookAnimator look = character != null ? character.lookAnimator : null;
        if (look == null) look = speakerRoot.GetComponentInChildren<FIMSpace.FLook.FLookAnimator>();
        if (look == null) return;

        Camera mainCamera = Camera.main;
        Transform cameraTarget = mainCamera != null ? mainCamera.transform
            : (PlayerInstance.Instance != null ? PlayerInstance.Instance.CameraTransform : null);
        if (cameraTarget == null) return;

        // Only capture the "before" target once — a late-join catch-up must not record the camera.
        if (_speakerLookAnimator != look)
        {
            RestoreSpeakerLook();
            _speakerLookAnimator = look;
            _speakerPreviousLookTarget = look.ObjectToFollow;
        }
        look.ObjectToFollow = cameraTarget;
        _speakerAssignedLookTarget = cameraTarget;
    }

    /// <summary>Hands the speaker's look target back to whatever it followed before the sequence.</summary>
    private static void RestoreSpeakerLook()
    {
        // Only hand back if nothing else (e.g. MutantEnemy's chase look) has retargeted it since.
        if (_speakerLookAnimator != null && _speakerLookAnimator.ObjectToFollow == _speakerAssignedLookTarget)
            _speakerLookAnimator.ObjectToFollow = _speakerPreviousLookTarget;

        _speakerLookAnimator = null;
        _speakerPreviousLookTarget = null;
        _speakerAssignedLookTarget = null;
    }

    /// <summary>Head bone of the speaker: SuspectWorldDialogue's look target, then the active
    /// humanoid Animator's head bone (handles mutated swaps), then the root as a last resort.</summary>
    private static Transform ResolveSpeakerHead(Transform speakerRoot, SuspectWorldDialogue worldDialogue)
    {
        if (speakerRoot == null) return null;

        if (worldDialogue != null)
        {
            Transform t = worldDialogue.DialogueLookTarget;
            if (t != null && t != worldDialogue.transform) return t;
        }

        foreach (Animator animator in speakerRoot.GetComponentsInChildren<Animator>())
        {
            if (!animator.isHuman) continue;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }

        SpeakingInteraction speaking = speakerRoot.GetComponentInChildren<SpeakingInteraction>();
        if (speaking != null && speaking.LookTarget != null) return speaking.LookTarget;

        return speakerRoot;
    }

    [ClientRpc]
    private void ExitScriptedModeClientRpc(bool lockOutsidePlayers = false, ClientRpcParams rpcParams = default)
    {
        _clientIsParticipant = false;
        _clientIsWaitingForInput = false;
        _clientRevealUnskippable = false;
        DialogueAdvanceTimer.Instance?.Hide();
        RestoreSpeakerLook();

        // The sequence ended naturally: its "leave" Back button must go with it (the outside/world
        // exit path never hides the Back button on its own).
        HideLeaveBackButtonLocal();

        // This sequence never locked the local player (e.g. an unlocked megaphone sequence) and
        // they've since started a world conversation — that conversation owns the dialogue lock,
        // cursor, choice panel, choice echo, cutscene immunity and HUD. Only release what this
        // sequence itself set up; restoring gameplay here would unlock movement and lock the
        // cursor while the conversation's choices are still on screen.
        if (WorldConversationOwnsLocalDialogueLock())
        {
            DeactivateOverrideCam();
            _clientSpeakerNetId = 0;
            _clientWorldCameraMode = false;
            return;
        }
        _clientEnteredDialogueLock = false;

        // Safety net: clear any lingering choice echo if the sequence ended before its own
        // auto-hide timer fired (e.g. an early exit or disconnect).
        DialogueManager.Instance?.HideChoiceEcho();

        // Deactivate any override camera before restoring the default cam state.
        DeactivateOverrideCam();

        // Mirrors the unconditional set in EnterScriptedModeClientRpc — always clear cutscene
        // immunity here so a player who was never movement-locked (e.g. an unlocked outside
        // sequence) doesn't stay permanently immune/untargetable after the dialogue ends.
        PlayerInstance.Instance?.SetIsInCutscene(false);

        // Clear DialogueChoiceSystem.IsInDialogueMode BEFORE calling ShowPlayerUI() below —
        // ShowPlayerUI() guards against revealing the HUD while that flag is still true, so
        // calling it first (as before) left the guard blocking the restore for outside players
        // whose IsInDialogueMode wasn't cleared until after ShowPlayerUI() had already bailed.
        if (PlayerInstance.Instance == null || LocalUsesWorldDialoguePath)
        {
            // Always call ExitScriptedDialogueModeOutside for outside (or world-camera) players —
            // covers the explicit lockOutsidePlayers path AND the late-join path where LateJoinClientRpc
            // called EnterScriptedDialogueModeOutside without lockOutsidePlayers being set.
            // ExitScriptedDialogueModeOutside is idempotent (bails if not in mode), so calling
            // it when the player was never locked is a safe no-op.
            if (LocalUsesWorldDialoguePath)
                DialogueChoiceSystem.Instance?.ExitScriptedDialogueModeOutside();

            _clientWorldCameraMode = false;
            UIController.Instance?.ShowPlayerUI();
            _clientSpeakerNetId = 0;
            return;
        }

        // ExitScriptedDialogueMode calls SuspectController.SetSuspectCamActive(false), which reads
        // _clientSpeakerNetId to find and deactivate the speaker's per-character cam — reset after.
        DialogueChoiceSystem.Instance.ExitScriptedDialogueMode();
        UIController.Instance?.ShowPlayerUI();
        _clientSpeakerNetId = 0;
    }

    // -------------------------------------------------------------------------
    // Client RPCs — advance timer UI
    // -------------------------------------------------------------------------

    // Targeted to this session's participants so a parallel conversation's timer UI is untouched.
    [ClientRpc]
    private void ShowAdvanceTimerClientRpc(float duration, ClientRpcParams rpcParams = default)
    {
        DialogueAdvanceTimer.Instance?.Show(duration);
    }

    [ClientRpc]
    private void HideAdvanceTimerClientRpc(ClientRpcParams rpcParams = default)
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
    private void SetAwaitingInputClientRpc(bool waiting, bool revealUnskippable = false, ClientRpcParams rpcParams = default)
    {
        _clientIsWaitingForInput = waiting;
        _clientRevealUnskippable = waiting && revealUnskippable;
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
        ApplyOverrideCam(key);
        EnsureLocalDialogueLockForOverrideCam();
    }

    /// <summary>
    /// A dialogue camera cut makes this an active, locked dialogue for the local participant. An
    /// outside participant of a sequence without lockOutsidePlayers / world camera entered scripted
    /// mode free to move (see <see cref="EnterScriptedModeClientRpc"/>), yet still receives the
    /// override cams — by default the speaker's booth SuspectCam. Without this they kept walking and
    /// interacting while the view sat inside the checkpoint, their headless first-person body and
    /// camera-mounted arms rendered from that camera. Lock them through the normal outside path
    /// (movement/look/interaction locked, body/arms/held item and other players' bodies, items and
    /// name tags hidden); every exit path already releases it via ExitScriptedDialogueModeOutside.
    /// Not used by <see cref="LateJoinClientRpc"/>, which takes the lock itself right after.
    /// </summary>
    private void EnsureLocalDialogueLockForOverrideCam()
    {
        if (_activeOverrideCam == null || !_activeOverrideCam.activeInHierarchy) return;

        // Active participants only. IsScriptedModeActive is set solely by the participant-targeted
        // Enter/LateJoin RPCs and cleared when a participant leaves, so overhearing / non-joined /
        // backed-out players never pass this (they also never receive RunDialogue's participant-
        // targeted camera cuts, keep their own camera and keep their body visible).
        if (!_clientIsParticipant || _clientEnteredDialogueLock) return;

        // Speaker dialogues only. Megaphone sequences broadcast their camera RPC to every client
        // with no speaker (_clientSpeakerNetId == 0) — players hearing a megaphone are never locked here.
        if (_clientSpeakerNetId == 0) return;

        if (PlayerInstance.Instance == null || DialogueChoiceSystem.Instance == null) return;

        // A world conversation the player started during the free sequence owns the lock — leave it.
        if (DialogueChoiceSystem.IsInDialogueMode || SuspectWorldDialogue.IsLocalPlayerInConversation) return;

        Transform speakerRoot = null;
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(_clientSpeakerNetId, out var speakerObject))
            speakerRoot = speakerObject.transform;

        // Outside path: never activates SuspectController's cam (the override cam is already live)
        // and is released by the outside exit used for every LocalUsesWorldDialoguePath player.
        EnterOutsideDialogueFacingSpeaker(speakerRoot);
        _clientEnteredDialogueLock = true;

        Debug.Log("[ScriptedDialogueRunner] Free outside participant locked — dialogue camera cut " +
                  $"'{_activeOverrideCam.name}' made this an active locked dialogue.");
    }

    /// <summary>Local body of <see cref="SetActiveOverrideCamClientRpc"/> — also used by
    /// <see cref="LateJoinClientRpc"/> after it caches the speaker ID.</summary>
    private void ApplyOverrideCam(string key)
    {
        // Always deactivate the current override first.
        if (_activeOverrideCam != null)
        {
            _activeOverrideCam.SetActive(false);
            _activeOverrideCam = null;
        }

        // World-camera sequences (e.g. Givi) stay on the player-camera dolly-in: never cut to the
        // speaker's per-character SuspectCam / SuspectFaceCam, whether by default or by key.
        // Explicit registry keys below still apply.
        if (_clientWorldCameraMode &&
            (string.IsNullOrEmpty(key) || key == "SuspectCam" || key == "SuspectFaceCam" || key == "suspect face"))
            return;

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

    /// <summary>
    /// Looks up <paramref name="key"/> in the static <see cref="_cameras"/> registry. Returns null
    /// when the key is empty or not registered. Used by <see cref="SuspectWorldDialogue"/> so its
    /// per-line camera triggers accept the same registry keys as scripted dialogue nodes.
    /// </summary>
    public GameObject GetRegisteredCamera(string key)
    {
        if (string.IsNullOrEmpty(key) || _cameras == null) return null;
        foreach (var entry in _cameras)
        {
            if (entry.key == key && entry.cam != null)
                return entry.cam;
        }
        return null;
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
    private void SetWobbleClientRpc(int profileIndex, ulong speakerNetId, ClientRpcParams rpcParams = default)
    {
        if (!LocalReceivesLinesFrom(speakerNetId)) return;
        DialogueManager.Instance.SetNextLineWobbleProfile(ResolveWobbleProfileByIndex(profileIndex));
    }

    // Speaker key for megaphone barks: shown to every free player, skipped for a player who is in a
    // conversation (the bark's subtitle would evict their own line — see SayMegaphoneLineClientRpc).
    private const ulong BarkSpeakerKey = ulong.MaxValue;

    /// <summary>
    /// Local: whether this client will actually spawn the next subtitle from
    /// <paramref name="speakerNetId"/> (0 = megaphone dialogue, always shown to its recipients;
    /// <see cref="BarkSpeakerKey"/> = bark). Per-line priming (wobble/font) is skipped otherwise,
    /// so it can't leak onto a parallel conversation's line.
    /// </summary>
    private bool LocalReceivesLinesFrom(ulong speakerNetId)
    {
        if (speakerNetId == BarkSpeakerKey) return !OverhearRange.IsLocalPlayerConversationParticipant;
        if (speakerNetId == 0 || _clientIsParticipant) return true;
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            return false;
        return OverhearRange.CanLocalPlayerSee(netObj.transform);
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
    // Client RPCs — fonts
    // -------------------------------------------------------------------------

    /// <summary>
    /// Primes <see cref="DialogueManager"/> with the font asset to use for the next subtitle.
    /// <paramref name="fontIndex"/> of -1 resolves to the subtitle prefab's default font (no
    /// override); 0+ indexes into <see cref="_additionalFontAssets"/>.
    /// </summary>
    [ClientRpc]
    private void SetFontClientRpc(int fontIndex, ulong speakerNetId, ClientRpcParams rpcParams = default)
    {
        if (!LocalReceivesLinesFrom(speakerNetId)) return;
        DialogueManager.Instance.SetNextLineFontOverride(ResolveFontAssetByIndex(fontIndex));
    }

    /// <summary>
    /// Server-side: maps a node's optional font override to an RPC-safe index.
    /// Returns -1 for no override (null), or the index within <see cref="_additionalFontAssets"/>
    /// for a registered override. Logs a warning and falls back to -1 if not registered.
    /// </summary>
    private int ResolveFontAssetIndex(TMPro.TMP_FontAsset overrideFont)
    {
        if (overrideFont == null)
            return -1;

        if (_additionalFontAssets != null)
        {
            for (int i = 0; i < _additionalFontAssets.Length; i++)
            {
                if (_additionalFontAssets[i] == overrideFont)
                    return i;
            }
        }

        Debug.LogWarning($"[ScriptedDialogueRunner] fontOverride '{overrideFont.name}' is not registered " +
                         $"in _additionalFontAssets. Falling back to the default font.");
        return -1;
    }

    /// <summary>
    /// Client-side: resolves an RPC font index back to the corresponding <see cref="TMPro.TMP_FontAsset"/>.
    /// -1 returns null (no override, keep the subtitle prefab's default font); 0+ indexes into
    /// <see cref="_additionalFontAssets"/>.
    /// </summary>
    private TMPro.TMP_FontAsset ResolveFontAssetByIndex(int index)
    {
        if (index < 0)
            return null;

        if (_additionalFontAssets != null && index < _additionalFontAssets.Length)
            return _additionalFontAssets[index];

        Debug.LogWarning($"[ScriptedDialogueRunner] Font index {index} is out of range. Falling back to the default font.");
        return null;
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
            if (IsClientEngagedElsewhere(senderId)) return;
            _participants.Add(senderId);
            Debug.Log($"[ScriptedDialogueRunner] Client {senderId} self-joined participants via choice submission.");
        }

        _choiceSubmissions[senderId] = choiceIndex;
        _choicePlayerNames[senderId] = playerName;

        // Highlight for this session's other participants (the sender already highlighted locally).
        HighlightChoiceForOthersClientRpc(choiceIndex, senderId, BuildParticipantRpcParams());

        int required = Mathf.Max(1, _participants.Count);

        if (_choiceSubmissions.Count == 1 && _choiceTimerCoroutine == null && required > 1)
            _choiceTimerCoroutine = StartCoroutine(ChoiceTimeoutCoroutine());

        if (_choiceSubmissions.Count >= required)
            ResolveChoices();
    }

    private IEnumerator ChoiceTimeoutCoroutine()
    {
        ShowAdvanceTimerClientRpc(_advanceTimeoutSeconds, BuildParticipantRpcParams());
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

        HideAdvanceTimerClientRpc(BuildParticipantRpcParams());

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
    private void HighlightChoiceForOthersClientRpc(int choiceIndex, ulong senderClientId, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        if (!_clientIsParticipant) return;
        DialogueChoiceSystem.Instance?.HighlightChoice(choiceIndex);
    }

    /// <summary>
    /// Sent after both players have chosen (or the timeout fired). For this session's
    /// participants it hides the choice panel and clears highlights; everyone who can see the
    /// conversation then gets the winning player's spoken line as a subtitle before the NPC
    /// delivers their response.
    /// </summary>
    [ClientRpc]
    private void FinalizeChoiceClientRpc(string choiceText, string playerName, ulong winnerClientId)
    {
        // Only participants of THIS scripted dialogue own the choice panel on screen — a player in
        // a parallel conversation keeps their own panel (and their own highlights) untouched.
        bool isParticipant = _clientIsParticipant;
        if (isParticipant)
        {
            DialogueChoiceSystem.Instance?.ResetChoiceHighlights();
            DialogueChoiceSystem.Instance?.HideChoicePanel();
        }

        Transform conversationSource = ResolveActiveDialogueSpeakerTransform();

        if (!isParticipant)
        {
            // Non-participants: fail closed when the speaker can't be located, else overhear
            // range — never while the local player is in a different conversation.
            if (conversationSource == null || !OverhearRange.CanLocalPlayerOverhear(conversationSource)) return;
        }

        bool isLocalWinner = NetworkManager.Singleton != null &&
                             NetworkManager.Singleton.LocalClientId == winnerClientId;
        DialogueManager.Instance.ShowChoiceEcho(choiceText, playerName, Color.white, isLocalWinner,
            overhearSource: conversationSource, participantsAlwaysSee: isParticipant);

        // Non-participants never receive ExitScriptedModeClientRpc (participant-targeted), so
        // remember this echo and clear it when the sequence ends (SetActiveDialogueSpeakerClientRpc(0)).
        _overheardChoiceEcho = isParticipant ? null : DialogueManager.Instance.ActiveChoiceEcho;
    }

    /// <summary>
    /// Client-side lookup of the transform for this slot's broadcast speaker (sent to every client
    /// regardless of participation). Returns null when no speaker is active/spawned.
    /// </summary>
    private Transform ResolveActiveDialogueSpeakerTransform()
    {
        ulong netId = _broadcastSpeakerNetId;
        if (netId == 0 || NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return null;
        return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj)
            ? netObj.transform
            : null;
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
    /// Finds the clientId of the player who submitted <paramref name="winningIndex"/>.
    /// Returns <see cref="ulong.MaxValue"/> if no submission matches (e.g. an unattended timeout).
    /// </summary>
    private ulong FindWinnerClientId(int winningIndex)
    {
        foreach (var kvp in _choiceSubmissions)
        {
            if (kvp.Value == winningIndex)
                return kvp.Key;
        }
        return ulong.MaxValue;
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

    /// <summary>
    /// Applies a node/choice expression key on the speaker's <see cref="DialogueExpressionMaterial"/>
    /// (if any). Broadcast to every client so overhearing players see the same face. Empty restores
    /// the default; the component also reverts on its own when the sequence ends.
    /// </summary>
    [ClientRpc]
    private void SetSpeakerExpressionClientRpc(ulong speakerNetId, string expression)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
            return;

        var expressions = netObj.GetComponentInChildren<DialogueExpressionMaterial>(true);
        if (expressions != null)
            expressions.SetExpression(expression);
    }

    /// <summary>
    /// Server-only. Fires the speaker's animation trigger on every client and, if it is the glass
    /// bang trigger, schedules the window hit. Cancels any bang still pending from a skipped line.
    /// </summary>
    private void TriggerSpeakerAnimationServer(ulong speakerNetId, string trigger)
    {
        ResetAndTriggerAnimationClientRpc(speakerNetId, _lastAnimTrigger, trigger);
        _lastAnimTrigger = trigger ?? string.Empty;

        if (_glassBangCoroutine != null)
        {
            StopCoroutine(_glassBangCoroutine);
            _glassBangCoroutine = null;
        }

        if (!string.IsNullOrEmpty(trigger) && trigger == _glassBangTrigger)
            _glassBangCoroutine = StartCoroutine(GlassBangServerRoutine());
    }

    /// <summary>
    /// Server-only. At the bang's impact frame, lands the hit exactly like
    /// <c>MutantSuspectBehaviour.WindowAttackSequence</c>: closed shutter → shutter feedback;
    /// otherwise <see cref="BreakableGlassController.RegisterHit"/> (replicated + saved) and the
    /// crack/smash feedback on every client. Already-smashed glass gets no hit and no impulse.
    /// </summary>
    private IEnumerator GlassBangServerRoutine()
    {
        yield return new WaitForSeconds(_glassBangImpactDelay);
        _glassBangCoroutine = null;

        var shutter = ShutterController.Instance;
        var glass = BreakableGlassController.Instance;

        if (shutter != null && !shutter.IsOpen)
        {
            GlassBangImpactClientRpc(GlassBangResult.Shutter, 0);
            yield break;
        }

        if (glass == null || glass.IsSmashed)
            yield break;

        if (!_glassBangCanSmash && glass.CurrentHits >= glass.MaxHits - 1)
        {
            GlassBangImpactClientRpc(GlassBangResult.Knock, glass.CurrentHits);
            yield break;
        }

        int newHits = glass.RegisterHit();
        GlassBangImpactClientRpc(glass.IsSmashed ? GlassBangResult.Smash : GlassBangResult.Crack, newHits);
    }

    private enum GlassBangResult : byte { Shutter, Knock, Crack, Smash }

    [ClientRpc]
    private void GlassBangImpactClientRpc(GlassBangResult result, int hitCount)
    {
        switch (result)
        {
            case GlassBangResult.Shutter: ShutterController.Instance?.OnHitByMutant(); break;
            case GlassBangResult.Knock:   BreakableGlassController.Instance?.PlayKnockFeedback(); break;
            case GlassBangResult.Crack:   BreakableGlassController.Instance?.OnHitByMutant(hitCount); break;
            case GlassBangResult.Smash:   BreakableGlassController.Instance?.ApplySmash(); break;
        }

        if (_glassBangImpulseSource != null)
            _glassBangImpulseSource.GenerateImpulse();
    }

    private string GetLocalPlayerName()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
            return Steamworks.SteamClient.Name;

        return $"Player {NetworkManager.Singleton.LocalClientId}";
    }
}
