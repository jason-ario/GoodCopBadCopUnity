using System;
using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;

public class Telephone : Interactable
{
    public static Telephone Instance { get; private set; }

    /// <summary>Fired on all clients when a ring.</summary>
    public static event Action OnRingStarted;

    /// <summary>
    /// Fired on ALL clients (via ClientRpc) when a scripted call started with
    /// <see cref="TriggerScriptedCall"/> is answered. Use this to register tasks or
    /// trigger any per-client setup that should happen the moment the handset is lifted.
    /// </summary>
    public static event Action OnScriptedCallAnsweredAllClients;

    /// <summary>
    /// Fired on ALL clients (via ClientRpc) when the server calls
    /// <see cref="NotifyScriptedCallCompleted"/> — i.e. the scripted call's own dialogue has
    /// finished. Use this to grant follow-up tasks on every client, not just the host.
    /// </summary>
    public static event Action OnScriptedCallCompletedAllClients;

    /// <summary>
    /// When true, all incoming calls are silently suppressed — <see cref="TriggerCall"/>
    /// and <see cref="TriggerRandomCall"/> return immediately without ringing.
    /// Set by day-specific controllers (e.g. <see cref="Day_01"/>) that need to keep
    /// the phone quiet during scripted sequences.
    /// </summary>
    public static bool BlockAllCalls = false;

    [SerializeField] private SocketFollow handSet;
    [SerializeField] private Transform _ikTarget;
    [SerializeField] private Transform _camera;
    [SerializeField] private Transform _handsetPos;
    [SerializeField] private AudioSource phoneSound;
    [SerializeField] private AudioClip phoneGrabSound;
    [SerializeField] private AudioClip phonePlaceSound;
    [Tooltip("Forward nudge (metres, camera-parent local Z) applied to the player's camera while the handset is held " +
             "and the HQ Order Screen is open, so the player can't see into their own body.")]
    [SerializeField] private float _holdingCameraForwardOffset = 0.08f;

    [Header("Phone Call")]
    [Tooltip("All tasks HQ can deliver. Picked by TriggerCall(index) or TriggerRandomCall().")]
    [SerializeField] private PhoneTaskData[] _availableTasks;
    [Tooltip("Clip played as a one-shot each time the phone rings.")]
    [SerializeField] private AudioClip _phoneRingClip;
    [Tooltip("Dedicated AudioSource used for the ring one-shots. Should not be the same as phoneSound.")]
    [SerializeField] private AudioSource _ringAudioSource;
    [Tooltip("AudioSource used to play the HQ voice line when the call is answered.")]
    [SerializeField] private AudioSource _voiceAudioSource;

    /// <summary>
    /// The AudioSource used to play HQ voice lines through the phone handset, already carrying
    /// the "old phone" filter chain (low/high-pass + light distortion) on its GameObject.
    /// Exposed so other systems (e.g. <see cref="ScriptedDialogueRunner.PlayMegaphoneDialogue"/>'s
    /// <c>audioSourceOverride</c>) can route their own dialogue audio through the phone instead
    /// of the world megaphone.
    /// </summary>
    public AudioSource VoiceAudioSource => _voiceAudioSource;

    [Header("Phone Voice (In-Ear)")]
    [Tooltip("Voice clips cycled for scripted calls (e.g. Day 3's HQ power-outage call) played through " +
             "the handset. Falls back to the caller's own clips when empty.")]
    [SerializeField] private AudioClip[] _scriptedCallVoiceClips;
    [Tooltip("When true, the phone filter settings below are applied to the voice AudioSource's filter " +
             "chain on Awake (filters are added if missing).")]
    [SerializeField] private bool _applyPhoneFilterPreset = true;
    [SerializeField] private float _phoneHighPassCutoff = 1200f;
    [SerializeField] private float _phoneHighPassResonance = 2.5f;
    [SerializeField] private float _phoneLowPassCutoff = 1800f;
    [SerializeField] private float _phoneLowPassResonance = 3f;
    [SerializeField, Range(0f, 1f)] private float _phoneDistortionLevel = 0.85f;
    [Tooltip("Volume of the voice while the local player holds the handset (played 2D, in-ear).")]
    [SerializeField, Range(0f, 1f)] private float _inEarVolume = 1f;
    [Tooltip("Stereo pan while in-ear. Negative = left ear (the handset is held in the left hand).")]
    [SerializeField, Range(-1f, 1f)] private float _inEarStereoPan = -0.25f;
    [Tooltip("Volume for players NOT holding the handset during scripted calls (e.g. Day 3's HQ call). " +
             "Scripted calls play 2D for everyone so the whole team hears them, just quieter than in-ear.")]
    [SerializeField, Range(0f, 1f)] private float _scriptedCallBystanderVolume = 0.6f;

    /// <summary>Voice clips for scripted calls; empty when none are assigned.</summary>
    public AudioClip[] ScriptedCallVoiceClips => _scriptedCallVoiceClips ?? Array.Empty<AudioClip>();

    // Original 3D settings of the voice source, restored for players not holding the handset.
    private float _voiceDefaultSpatialBlend;
    private int _voiceDefaultPriority;
    private float _voiceDefaultVolume;
    private bool _voiceDefaultBypassReverb;

    [Tooltip("Speaker name displayed in the subtitle bar during the voice line.")]
    [SerializeField] private string _hqSpeakerName = "HQ";
    [Tooltip("Animator driving the phone ringing animation. Optional.")]
    [SerializeField] private Animator _phoneAnimator;
    [Tooltip("Trigger parameter name on the Animator that fires a single ring animation cycle.")]
    [SerializeField] private string _ringAnimTriggerName = "Ring";
    [Tooltip("How long each ring lasts (seconds). Should match the ring animation/clip length.")]
    [SerializeField] private float _ringDuration = 1.5f;
    [Tooltip("Silent pause between rings (seconds).")]
    [SerializeField] private float _ringPauseDuration = 2f;
    [Tooltip("Seconds before an unanswered call times out and the task is missed.")]
    [SerializeField] private float _ringTimeout = 20f;

    [Header("Debug Call")]
    [Tooltip("Voice line shown as subtitles when the debug ring (F4) is answered.")]
    [SerializeField, TextArea(2, 4)] private string _debugVoiceLine =
        "Trash is piling up around your booth! Get on it! What are we paying you for??";
    [Tooltip("Audio clips cycled for the debug voice line. Leave empty for text-only subtitles.")]
    [SerializeField] private AudioClip[] _debugVoiceAudioClips;
    [Tooltip("Task name registered in the guidebook when the debug call is answered.")]
    [SerializeField] private string _debugTaskName = "Take Out the Trash";
    [Tooltip("Task description registered in the guidebook when the debug call is answered.")]
    [SerializeField] private string _debugTaskDescription = "Clean up the trash bags piling up around the booth.";

    // Only the server writes these; all clients can read them.
    private NetworkVariable<bool> _isGrabbed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<ulong> _grabbingClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _isRinging = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-only: index into _availableTasks for the current incoming call.
    private int _pendingTaskIndex = -1;
    private Coroutine _ringTimeoutCoroutine;

    // Server-only: scripted call state. Set by TriggerScriptedCall.
    private bool _isScriptedCall = false;
    private Action _scriptedCallAnsweredCallback;

    // Server-only: a scripted call requested while the phone couldn't ring (handset held, e.g. the
    // HQ Order Screen, or BlockAllCalls). Rings automatically once the phone is free again —
    // see TickQueuedScriptedCallServer. Scripted calls are story-critical and must never be dropped.
    private bool _hasQueuedScriptedCall = false;
    private Action _queuedScriptedCallback;
    private float _queuedScriptedCallEarliestTime;
    private const float QueuedScriptedCallDelayAfterPutDown = 1f;

    // Client-only: drives the ring cycle (animation + one-shot audio).
    private Coroutine _ringCycleCoroutine;

    // Client-only: why the LOCAL player is holding the handset (None when not holding it).
    // Used by ForceHangUpForDialogue to tear the phone down instantly when a forced
    // conversation starts. Scripted calls are exempt — their own dialogue drives the hang-up.
    private enum LocalHoldMode { None, OrderScreen, Call, ScriptedCall }
    private LocalHoldMode _localHoldMode = LocalHoldMode.None;
    private bool _localOrderScreenOpen;
    private Coroutine _localSequenceCoroutine;

    // Client-only (observers): the remote player the handset is attached to, and whether it is
    // currently following their camera-arm socket (spectated) instead of the body-arm socket.
    private PlayerInteractionController _observedHolder;
    private bool _observedHolderUsesCamSocket;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        InitVoiceSource();
    }

    /// <summary>
    /// Observers only: while the remote holder is being spectated, the spectator sees that
    /// player's first-person camera rig, so the handset must follow the camera left-arm socket
    /// (same as the owner) instead of the lagging, shadow-only body arm. Runs in Update so the
    /// retarget happens before SocketFollow's LateUpdate.
    /// </summary>
    private void Update()
    {
        ReleaseHandsetIfHolderInactiveServer();
        TickQueuedScriptedCallServer();

        if (_observedHolder == null || !handSet.enabled) return;

        bool spectated = IsSpectatedLocally(_observedHolder);
        if (spectated == _observedHolderUsesCamSocket) return;

        _observedHolderUsesCamSocket = spectated;
        handSet.SetTarget(GetHandSocketForClient(_observedHolder, isLocalPlayer: spectated));
    }

    private static bool IsSpectatedLocally(PlayerInteractionController player)
    {
        SpectateManager sm = SpectateManager.Instance;
        return sm != null && sm.IsSpectating && sm.CurrentTarget != null
            && sm.CurrentTarget.gameObject == player.gameObject;
    }

    // ── Phone voice ──────────────────────────────────────────────────────────

    private void InitVoiceSource()
    {
        if (_voiceAudioSource == null) return;

        _voiceDefaultSpatialBlend = _voiceAudioSource.spatialBlend;
        _voiceDefaultPriority = _voiceAudioSource.priority;
        _voiceDefaultVolume = _voiceAudioSource.volume;
        _voiceDefaultBypassReverb = _voiceAudioSource.bypassReverbZones;

        if (!_applyPhoneFilterPreset) return;

        GameObject go = _voiceAudioSource.gameObject;

        var highPass = GetOrAdd<AudioHighPassFilter>(go);
        highPass.enabled = true;
        highPass.cutoffFrequency = _phoneHighPassCutoff;
        highPass.highpassResonanceQ = _phoneHighPassResonance;

        var lowPass = GetOrAdd<AudioLowPassFilter>(go);
        lowPass.enabled = true;
        // The custom curve overrides cutoffFrequency, so flatten it to the target cutoff.
        lowPass.customCutoffCurve = AnimationCurve.Constant(0f, 1f, Mathf.Clamp01(_phoneLowPassCutoff / 22000f));
        lowPass.cutoffFrequency = _phoneLowPassCutoff;
        lowPass.lowpassResonanceQ = _phoneLowPassResonance;

        var distortion = GetOrAdd<AudioDistortionFilter>(go);
        distortion.enabled = true;
        distortion.distortionLevel = _phoneDistortionLevel;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        return go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
    }

    /// <summary>
    /// Local, per client. Configures <see cref="VoiceAudioSource"/> for the local listener and
    /// returns it: when the local player is holding the handset the voice plays 2D at top
    /// priority, full volume, bypassing reverb (right in the player's ear); everyone else hears
    /// it with the source's original 3D settings from the phone's position — unless
    /// <paramref name="audibleToEveryone"/> is set (scripted calls), in which case non-holders
    /// also hear it 2D at <see cref="_scriptedCallBystanderVolume"/> regardless of distance.
    /// </summary>
    public AudioSource PrepareVoiceSourceForLocalListener(bool audibleToEveryone = false)
    {
        if (_voiceAudioSource == null) return null;

        var nm = NetworkManager.Singleton;
        bool localHolding = _localHoldMode != LocalHoldMode.None ||
                            (nm != null && _isGrabbed.Value && _grabbingClientId.Value == nm.LocalClientId);

        if (localHolding)
        {
            _voiceAudioSource.spatialBlend = 0f;
            _voiceAudioSource.priority = 0;
            _voiceAudioSource.volume = _inEarVolume;
            _voiceAudioSource.panStereo = _inEarStereoPan;
            _voiceAudioSource.bypassReverbZones = true;
            _voiceAudioSource.dopplerLevel = 0f;
        }
        else if (audibleToEveryone)
        {
            _voiceAudioSource.spatialBlend = 0f;
            _voiceAudioSource.priority = 0;
            _voiceAudioSource.volume = _scriptedCallBystanderVolume;
            _voiceAudioSource.panStereo = 0f;
            _voiceAudioSource.bypassReverbZones = true;
            _voiceAudioSource.dopplerLevel = 0f;
        }
        else
        {
            _voiceAudioSource.spatialBlend = _voiceDefaultSpatialBlend;
            _voiceAudioSource.priority = _voiceDefaultPriority;
            _voiceAudioSource.volume = _voiceDefaultVolume;
            _voiceAudioSource.panStereo = 0f;
            _voiceAudioSource.bypassReverbZones = _voiceDefaultBypassReverb;
        }

        return _voiceAudioSource;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    public override string GetInteractVerb(PlayerInteractionController player)
    {
        if (!string.IsNullOrEmpty(interactVerb)) return interactVerb;
        if (_isRinging.Value && !_isGrabbed.Value) return "Answer";
        if (!_isGrabbed.Value) return "Pick up";
        if (player != null && _grabbingClientId.Value == player.OwnerClientId) return "Hang up";
        return "Use";
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_isRinging.Value && !_isGrabbed.Value)
        {
            // Picking up the ringing phone answers the call.
            RequestAnswerCallServerRpc(player.OwnerClientId);
            return;
        }

        if (_isGrabbed.Value == false)
        {
            RequestGrabServerRpc(player.OwnerClientId);
        }
        else if (_grabbingClientId.Value == player.OwnerClientId)
        {
            RequestPutDownServerRpc(player.OwnerClientId);
        }
    }

    // ── Phone call – public API ───────────────────────────────────────────────

    /// <summary>
    /// Server-only. Triggers an incoming call that will deliver the task at
    /// <paramref name="taskIndex"/> in <c>_availableTasks</c> when answered.
    /// Does nothing if the phone is already ringing or currently grabbed.
    /// </summary>
    public void TriggerCall(int taskIndex)
    {
        if (!IsServer) return;
        if (BlockAllCalls) return;
        if (_isRinging.Value || _isGrabbed.Value) return;
        if (_availableTasks == null || taskIndex < 0 || taskIndex >= _availableTasks.Length)
        {
            Debug.LogWarning($"[Telephone] TriggerCall: taskIndex {taskIndex} is out of range.");
            return;
        }

        _pendingTaskIndex = taskIndex;
        _isRinging.Value = true;

        StartRingingClientRpc();
        _ringTimeoutCoroutine = StartCoroutine(RingTimeoutRoutine());
    }

    /// <summary>
    /// Server-only. Triggers an incoming phone call for a scripted sequence.
    /// The normal task and voice-line delivery are suppressed — <paramref name="onAnswered"/>
    /// fires on the server as soon as the player picks up the handset.
    /// Callers should wait ~1.5 s inside <paramref name="onAnswered"/> before starting any
    /// <see cref="ScriptedDialogueRunner"/> sequence so the grab animation can finish first.
    /// <para>
    /// Scripted calls are never dropped: if the handset is currently held (e.g. a player has the
    /// HQ Order Screen open) or <see cref="BlockAllCalls"/> is set, the call is queued and rings
    /// as soon as the phone is free. A ringing regular call is pre-empted. Scripted calls do not
    /// time out — they ring until answered or <see cref="CancelScriptedCall"/> is called.
    /// </para>
    /// </summary>
    public void TriggerScriptedCall(Action onAnswered)
    {
        if (!IsServer) return;

        if (_isRinging.Value && _isScriptedCall)
        {
            Debug.LogWarning("[Telephone] TriggerScriptedCall: a scripted call is already ringing -- ignoring duplicate.");
            return;
        }

        if (CanStartScriptedRingServer())
        {
            StartScriptedRingServer(onAnswered);
            return;
        }

        _hasQueuedScriptedCall = true;
        _queuedScriptedCallback = onAnswered;
        _queuedScriptedCallEarliestTime = Time.time + QueuedScriptedCallDelayAfterPutDown;
        Debug.Log("[Telephone] TriggerScriptedCall: phone busy -- scripted call queued, will ring once the handset is free.");
    }

    /// <summary>
    /// Server-only. Cancels a queued or currently ringing scripted call (e.g. when the day that
    /// requested it is deactivated). Does not affect a scripted call that was already answered.
    /// </summary>
    public void CancelScriptedCall()
    {
        if (!IsServer) return;

        _hasQueuedScriptedCall = false;
        _queuedScriptedCallback = null;

        if (_isRinging.Value && _isScriptedCall)
        {
            StopRingTimeout();
            _isRinging.Value = false;
            _pendingTaskIndex = -1;
            _isScriptedCall = false;
            _scriptedCallAnsweredCallback = null;
            StopRingingClientRpc();
        }
    }

    private bool CanStartScriptedRingServer()
    {
        return !BlockAllCalls && !_isGrabbed.Value && !(_isRinging.Value && _isScriptedCall);
    }

    private void StartScriptedRingServer(Action onAnswered)
    {
        // Pre-empt a ringing regular call — its task is simply never delivered.
        StopRingTimeout();

        _isScriptedCall = true;
        _scriptedCallAnsweredCallback = onAnswered;
        _pendingTaskIndex = -2; // sentinel: scripted call — no task or debug voice delivered
        _isRinging.Value = true;

        StartRingingClientRpc();
    }

    /// <summary>
    /// Server-only, every frame. Rings a queued scripted call once the handset has been free for
    /// <see cref="QueuedScriptedCallDelayAfterPutDown"/> seconds (lets the put-down sequence finish).
    /// </summary>
    private void TickQueuedScriptedCallServer()
    {
        if (!_hasQueuedScriptedCall || !IsServer || !IsSpawned) return;

        if (!CanStartScriptedRingServer())
        {
            _queuedScriptedCallEarliestTime = Time.time + QueuedScriptedCallDelayAfterPutDown;
            return;
        }

        if (Time.time < _queuedScriptedCallEarliestTime) return;

        Action callback = _queuedScriptedCallback;
        _hasQueuedScriptedCall = false;
        _queuedScriptedCallback = null;
        StartScriptedRingServer(callback);
    }

    /// <summary>
    /// Server-only. Broadcasts <see cref="OnScriptedCallCompletedAllClients"/> to every client so
    /// tasks granted at the end of a scripted call stay in sync across players.
    /// </summary>
    public void NotifyScriptedCallCompleted()
    {
        if (!IsServer) return;
        ScriptedCallCompletedClientRpc();
    }

    [ClientRpc]
    private void ScriptedCallCompletedClientRpc()
    {
        OnScriptedCallCompletedAllClients?.Invoke();
    }

    /// <summary>
    /// Triggers an incoming call from any client. Routes to the server automatically.
    /// Idempotent — silently ignored if the phone is already ringing or grabbed.
    /// </summary>
    public void TriggerCallSynced(int taskIndex)
    {
        if (IsServer)
            TriggerCall(taskIndex);
        else
            TriggerCallServerRpc(taskIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TriggerCallServerRpc(int taskIndex)
    {
        TriggerCall(taskIndex);
    }

    /// <summary>
    /// Server-only. Triggers an incoming call with a randomly selected task.
    /// </summary>
    public void TriggerRandomCall()
    {
        if (!IsServer) return;
        if (BlockAllCalls) return;
        if (_availableTasks == null || _availableTasks.Length == 0)
        {
            Debug.LogWarning("[Telephone] TriggerRandomCall: no tasks assigned.");
            return;
        }
        TriggerCall(UnityEngine.Random.Range(0, _availableTasks.Length));
    }

    /// <summary>
    /// Debug only — starts the ring cycle immediately on all clients, bypassing task validation.
    /// Sets <c>_isRinging</c> and <c>_pendingTaskIndex</c> correctly so picking up the phone
    /// stops the ring normally. No task is delivered since there is no task data.
    /// </summary>
    public void DebugStartRing()
    {
        if (IsServer)
            DebugStartRingInternal();
        else
            DebugStartRingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void DebugStartRingServerRpc() => DebugStartRingInternal();

    private void DebugStartRingInternal()
    {
        if (_isRinging.Value || _isGrabbed.Value) return;

        _pendingTaskIndex = -1;
        _isRinging.Value = true;

        StartRingingClientRpc();
        _ringTimeoutCoroutine = StartCoroutine(RingTimeoutRoutine());
    }

    // ── Phone call – server flow ──────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestAnswerCallServerRpc(ulong clientId)
    {
        if (!_isRinging.Value || _isGrabbed.Value) return;

        StopRingTimeout();
        _isRinging.Value = false;
        _isGrabbed.Value = true;
        _grabbingClientId.Value = clientId;

        int taskIndex = _pendingTaskIndex;
        _pendingTaskIndex = -1;

        // Capture and clear scripted-call state before the RPC so callers
        // can safely re-trigger a call inside the callback without collision.
        bool wasScriptedCall = _isScriptedCall;
        Action scriptedCallback = _scriptedCallAnsweredCallback;
        _isScriptedCall = false;
        _scriptedCallAnsweredCallback = null;

        PhoneAnsweredClientRpc(clientId, taskIndex);

        if (wasScriptedCall)
            scriptedCallback?.Invoke();
    }

    private IEnumerator RingTimeoutRoutine()
    {
        yield return new WaitForSeconds(_ringTimeout);

        if (_isRinging.Value)
        {
            _isRinging.Value = false;
            _pendingTaskIndex = -1;
            _isScriptedCall = false;
            _scriptedCallAnsweredCallback = null;
            StopRingingClientRpc();
            Debug.Log("[Telephone] Call missed — no one answered in time.");
        }

        _ringTimeoutCoroutine = null;
    }

    private void StopRingTimeout()
    {
        if (_ringTimeoutCoroutine == null) return;
        StopCoroutine(_ringTimeoutCoroutine);
        _ringTimeoutCoroutine = null;
    }

    // ── Phone call – client RPCs ──────────────────────────────────────────────

    [ClientRpc]
    private void StartRingingClientRpc()
    {
        if (_ringCycleCoroutine != null)
            StopCoroutine(_ringCycleCoroutine);

        _ringCycleCoroutine = StartCoroutine(RingCycleRoutine());
        OnRingStarted?.Invoke();
    }

    [ClientRpc]
    private void StopRingingClientRpc()
    {
        StopRingEffects();
    }

    /// <summary>
    /// Plays a single ring (animation trigger + one-shot audio), pauses, then repeats.
    /// Runs locally on every client and is stopped by <see cref="StopRingEffects"/>.
    /// </summary>
    private IEnumerator RingCycleRoutine()
    {
        while (true)
        {
            if (_phoneAnimator != null)
                _phoneAnimator.SetTrigger(_ringAnimTriggerName);

            if (_ringAudioSource != null && _phoneRingClip != null)
                _ringAudioSource.PlayOneShot(_phoneRingClip);

            yield return new WaitForSeconds(_ringDuration);
            yield return new WaitForSeconds(_ringPauseDuration);
        }
    }

    /// <summary>
    /// Called on all clients when a player answers the call.
    /// All clients register the task in their local registry; only the answering client
    /// plays the grab animation and voice line.
    /// <para>
    /// If <see cref="PhoneTaskData.LinkedTask"/> is set, the pre-existing
    /// <see cref="IBetweenShiftTask"/> is reset and registered so the existing networked
    /// completion tracking in <see cref="BetweenShiftTaskManager"/> is preserved.
    /// Otherwise a new <see cref="PhoneCallTask"/> is created from the data.
    /// </para>
    /// </summary>
    [ClientRpc]
    private void PhoneAnsweredClientRpc(ulong clientId, int taskIndex)
    {
        StopRingEffects();

        if (taskIndex >= 0 && _availableTasks != null && taskIndex < _availableTasks.Length
            && _availableTasks[taskIndex] != null)
        {
            PhoneTaskData data = _availableTasks[taskIndex];
            IBetweenShiftTask linkedTask = data.LinkedTask;

            if (linkedTask != null)
            {
                // Pre-registered task (e.g. TakeOutTrashTask): reset physics + register in task registry.
                // ResetTask is server-guarded internally; calling it on all clients is safe.
                BetweenShiftTaskManager.Instance?.ResetTaskPhysics();
                TaskRegistry.Instance.AddTask(linkedTask);
            }
            else
            {
                // Dynamic task: create a new PhoneCallTask from the ScriptableObject data.
                PhoneCallTask task = new PhoneCallTask(data.TaskName, data.TaskDescription, data.CouponReward);
                TaskRegistry.Instance.AddTask(task);
            }
        }
        else if (taskIndex == -1)
        {
            // Debug call: register a placeholder task on all clients.
            PhoneCallTask debugTask = new PhoneCallTask(_debugTaskName, _debugTaskDescription, 0);
            TaskRegistry.Instance?.AddTask(debugTask);
        }
        // taskIndex == -2: scripted call — task and voice are handled externally by the caller.
        // Notify all clients so they can register their own tasks for this scripted call.
        if (taskIndex == -2)
            OnScriptedCallAnsweredAllClients?.Invoke();

        // Determine ownership before lookup. For the answering client, use LocalClient.PlayerObject
        // directly — ConnectedClientsList is only fully populated on the server/host, so iterating
        // it on a non-host client can return null even for the client's own entry.
        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;
        PlayerInteractionController player = isLocalPlayer
            ? NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>()
            : FindPlayerByClientId(clientId);

        if (player == null) return;

        if (isLocalPlayer)
        {
            StopLocalSequence();
            _localHoldMode = taskIndex == -2 ? LocalHoldMode.ScriptedCall : LocalHoldMode.Call;
            _localSequenceCoroutine = StartCoroutine(AnswerCallSequence(player, taskIndex));
        }
        else
        {
            StartCoroutine(ObserverGrabConstraintSequence(player));
        }
    }

    // ── Phone call – client sequences ─────────────────────────────────────────

    /// <summary>
    /// Plays the grab animation, then streams the HQ voice line before returning
    /// control to the player. The player manually puts the phone down when done.
    /// For scripted calls (<paramref name="taskIndex"/> == -2) the same grab camera move plays,
    /// the HUD is swapped for the dim phone backdrop, and movement stays locked for the whole
    /// call — the caller's own <see cref="ScriptedDialogueRunner"/>
    /// sequence handles subtitle/voice presentation and calls <see cref="HangUpCurrentCaller"/>
    /// once it finishes.
    /// </summary>
    private IEnumerator AnswerCallSequence(PlayerInteractionController player, int taskIndex)
    {
        bool isScriptedCall = taskIndex == -2;

        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmIKTarget = _ikTarget;

        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        player.playerAnimationController.EnableLeftArmMask();
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f, .25f);
        player.playerAnimationController.SetAnimBool("HoldingPhone", true);
        yield return new WaitForSeconds(.25f);

        phoneSound.PlayOneShot(phoneGrabSound);
        handSet.SetTarget(GetHandSocketForClient(player, isLocalPlayer: true));
        handSet.enabled = true;

        yield return new WaitForSeconds(.25f);

        // Scripted calls stay on the handset for the whole conversation, so apply the same
        // holding-camera nudge the HQ Order Screen grab uses (keeps the body out of view).
        if (isScriptedCall)
            player.playerMovementController.ResetCameraPos(false, .25f, null, Vector3.forward * _holdingCameraForwardOffset);
        else
            player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmIKTarget = null;

        // Scripted calls (e.g. Day 3's HQ power-outage call) keep movement locked for the whole
        // conversation and hide the HUD behind the same dim backdrop as the HQ Order Screen.
        // Control, HUD and backdrop are restored in PutPhoneDownSequence once the caller's own
        // dialogue sequence has finished and it hangs up.
        if (isScriptedCall)
            UIController.Instance?.ShowPhoneCallBackdrop();
        else
            player.playerMovementController.SetCanControl(true);

        // Stream HQ voice line.
        if (taskIndex >= 0 && _availableTasks != null && taskIndex < _availableTasks.Length
            && _availableTasks[taskIndex] != null)
        {
            PhoneTaskData data = _availableTasks[taskIndex];

            DialogueManager.Instance.SpawnSubtitles(data.VoiceLine, _hqSpeakerName, Color.white);

            if (_voiceAudioSource != null && data.VoiceAudioClips != null && data.VoiceAudioClips.Length > 0)
            {
                DialogueManager.Instance.PlayDialogueAudio(data.VoiceLine, data.VoiceAudioClips, PrepareVoiceSourceForLocalListener());
            }
        }
        else if (taskIndex == -1)
        {
            // Debug call — show the hardcoded line and play debug audio.
            DialogueManager.Instance.SpawnSubtitles(_debugVoiceLine, _hqSpeakerName, Color.white);

            if (_voiceAudioSource != null && _debugVoiceAudioClips != null && _debugVoiceAudioClips.Length > 0)
            {
                DialogueManager.Instance.PlayDialogueAudio(
                    _debugVoiceLine, _debugVoiceAudioClips, PrepareVoiceSourceForLocalListener());
            }
        }
        // taskIndex == -2: scripted call — voice/subtitles are handled externally by the
        // caller's own scripted dialogue sequence (see Telephone.TriggerScriptedCall's doc
        // comment) via ScriptedDialogueRunner.PlayMegaphoneDialogue(unlocked: true), which keeps
        // the player's own free-look camera instead of cutting to a cutscene cam. Movement stays
        // locked (see above) until the caller calls HangUpCurrentCaller() when that dialogue
        // finishes — no back button needed here.
    }

    private void StopRingEffects()
    {
        if (_ringCycleCoroutine != null)
        {
            StopCoroutine(_ringCycleCoroutine);
            _ringCycleCoroutine = null;
        }

        if (_phoneAnimator != null)
            _phoneAnimator.ResetTrigger(_ringAnimTriggerName);

        if (_ringAudioSource != null)
            _ringAudioSource.Stop();
    }

    // ── Inactive holder release (server) ──────────────────────────────────────

    /// <summary>
    /// Server-only. Frees the handset when its holder can no longer put it down themselves:
    /// they died while holding it, their PlayerObject was replaced/despawned (revive), or they
    /// disconnected. Without this the phone stayed grabbed by the dead client forever, so no one
    /// else could pick it up — e.g. to call in backup for that same dead teammate.
    /// </summary>
    private void ReleaseHandsetIfHolderInactiveServer()
    {
        if (!IsServer || !IsSpawned || !_isGrabbed.Value)
            return;

        ulong holderId = _grabbingClientId.Value;
        if (IsHolderActiveServer(holderId))
            return;

        Debug.Log($"[Telephone] Releasing handset held by client {holderId} — holder is dead, despawned or disconnected.");

        _hangUpLocked = false;
        _isGrabbed.Value = false;
        _grabbingClientId.Value = ulong.MaxValue;

        ReleaseForInactiveHolderClientRpc(holderId);
    }

    private bool IsHolderActiveServer(ulong clientId)
    {
        NetworkManager nm = NetworkManager;
        if (nm == null || !nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return false;

        if (client.PlayerObject == null)
            return false;

        PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
        return health == null || !health.IsDead;
    }

    /// <summary>
    /// Resets the handset on every peer after the server released it from an inactive holder.
    /// The (dead) holder gets a local teardown that closes the HQ Order Screen WITHOUT restoring
    /// movement/interaction/HUD — the death flow owns the player's state from here.
    /// </summary>
    [ClientRpc]
    private void ReleaseForInactiveHolderClientRpc(ulong holderId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool isLocalHolder = nm != null && nm.LocalClientId == holderId;

        if (!isLocalHolder)
        {
            StartCoroutine(ObserverPutDownConstraintSequence());
            return;
        }

        StopLocalSequence();
        bool closeOrderScreen = _localOrderScreenOpen;
        _localHoldMode = LocalHoldMode.None;
        _localOrderScreenOpen = false;

        _voiceAudioSource?.Stop();

        handSet.enabled = false;
        handSet.transform.position = _handsetPos.position;
        handSet.transform.rotation = _handsetPos.rotation;

        PlayerInteractionController player = nm.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>();
        if (player != null)
        {
            player.playerAnimationController.SetAnimBool("HoldingPhone", false);
            player.playerAnimationController.DisableLeftArmMask();
            player.playerAnimationController.CamLeftArmRigIKTarget = null;
            player.playerAnimationController.LeftArmIKTarget = null;
            player.playerMovementController.CameraTransform.DOKill();
        }

        if (closeOrderScreen)
            UIController.Instance?.HideHQOrderScreenOnly();
        UIController.Instance?.HidePhoneCallBackdrop();
        UIController.Instance?.HideBackButton();
    }

    // ── Regular grab / put-down ───────────────────────────────────────────────

    /// <summary>
    /// Requests the server to hang up the phone on behalf of the given client.
    /// Only succeeds if that client is the one currently holding the handset.
    /// </summary>
    public void HangUp(ulong clientId)
    {
        RequestPutDownServerRpc(clientId);
    }

    /// <summary>
    /// Server-only. Hangs up the phone on behalf of whichever client is currently holding it.
    /// Used to auto-put-down the handset once a scripted call's own dialogue has finished (e.g.
    /// Day 3's power-outage call) without requiring the player to interact again. No-op if the
    /// phone isn't currently grabbed. Always clears <see cref="_hangUpLocked"/> first so the
    /// system's own auto-hangup is never blocked by a lock it (or its caller) set earlier.
    /// </summary>
    public void HangUpCurrentCaller()
    {
        if (!IsServer || !_isGrabbed.Value) return;
        _hangUpLocked = false;
        RequestPutDownServerRpc(_grabbingClientId.Value);
    }

    // Server-only: true while manual hang-up (via Interact) should be ignored — e.g. while a
    // scripted call's own dialogue is still playing. Set/cleared via SetHangUpLocked.
    private bool _hangUpLocked = false;

    /// <summary>
    /// Server-only. Blocks or unblocks manual hang-up (pressing E on the phone) while a scripted
    /// call's own dialogue sequence is in progress — e.g. Day 3's HQ power-outage call, so the
    /// player can't put the phone down early and skip the rest of the conversation. Does not
    /// affect <see cref="HangUpCurrentCaller"/>, which always clears the lock itself first.
    /// </summary>
    public void SetHangUpLocked(bool locked)
    {
        if (!IsServer) return;
        _hangUpLocked = locked;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestGrabServerRpc(ulong clientId)
    {
        if (_isGrabbed.Value) return;

        _isGrabbed.Value = true;
        _grabbingClientId.Value = clientId;

        ExecuteGrabSequenceClientRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPutDownServerRpc(ulong clientId)
    {
        if (!_isGrabbed.Value || _grabbingClientId.Value != clientId) return;

        if (_hangUpLocked)
        {
            Debug.Log("[Telephone] RequestPutDownServerRpc: hang-up blocked -- scripted call dialogue still in progress.");
            return;
        }

        _isGrabbed.Value = false;
        _grabbingClientId.Value = ulong.MaxValue;

        ExecutePutDownSequenceClientRpc(clientId);
    }

    /// <summary>
    /// Server-side forced hang-up requested by the holding client when a forced conversation
    /// starts. Bypasses <see cref="_hangUpLocked"/> — the client only sends this for non-scripted
    /// holds (see <see cref="ForceHangUpForDialogue"/>).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ForceHangUpServerRpc(ulong clientId)
    {
        if (!_isGrabbed.Value || _grabbingClientId.Value != clientId) return;

        _isGrabbed.Value = false;
        _grabbingClientId.Value = ulong.MaxValue;

        ExecutePutDownSequenceClientRpc(clientId, forced: true);
    }

    [ClientRpc]
    private void ExecuteGrabSequenceClientRpc(ulong clientId)
    {
        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;
        PlayerInteractionController player = isLocalPlayer
            ? NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>()
            : FindPlayerByClientId(clientId);

        if (player == null) return;

        if (isLocalPlayer)
        {
            StopLocalSequence();
            _localHoldMode = LocalHoldMode.OrderScreen;
            _localSequenceCoroutine = StartCoroutine(GrabPhoneSequence(player));
        }
        else
            StartCoroutine(ObserverGrabConstraintSequence(player));
    }

    [ClientRpc]
    private void ExecutePutDownSequenceClientRpc(ulong clientId, bool forced = false)
    {
        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;
        PlayerInteractionController player = isLocalPlayer
            ? NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>()
            : FindPlayerByClientId(clientId);

        if (isLocalPlayer)
        {
            // A forced hang-up was already torn down locally in ForceHangUpForDialogue; this
            // only catches the case where local state is still live (e.g. mismatched timing).
            if (forced)
            {
                if (_localHoldMode != LocalHoldMode.None)
                    InstantLocalPutDown(player);
                return;
            }

            if (player == null) return;
            StopLocalSequence();
            _localHoldMode = LocalHoldMode.None;
            _localSequenceCoroutine = StartCoroutine(PutPhoneDownSequence(player));
        }
        else
        {
            // Reset the handset even if the holder can't be resolved (e.g. despawned).
            StartCoroutine(ObserverPutDownConstraintSequence());
        }
    }

    // ── Forced hang-up (dialogue takeover) ────────────────────────────────────

    /// <summary>
    /// Client-side. Instantly hangs up the phone if the LOCAL player is holding it, closing the
    /// HQ Order Screen and dropping the handset without the usual put-down camera/control
    /// sequence (which would otherwise fight the incoming dialogue's camera and movement lock).
    /// Call this at the start of any forced conversation, BEFORE the dialogue locks the player.
    /// No-op when not holding the phone, or during a scripted call (its own dialogue is the call).
    /// </summary>
    public void ForceHangUpForDialogue()
    {
        if (_localHoldMode == LocalHoldMode.None || _localHoldMode == LocalHoldMode.ScriptedCall)
            return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;

        PlayerInteractionController player = nm.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>();
        InstantLocalPutDown(player);

        Debug.Log("[Telephone] Forced hang-up — local player entered a forced conversation while holding the phone.");
        ForceHangUpServerRpc(nm.LocalClientId);
    }

    private void StopLocalSequence()
    {
        if (_localSequenceCoroutine != null)
        {
            StopCoroutine(_localSequenceCoroutine);
            _localSequenceCoroutine = null;
        }
    }

    /// <summary>
    /// Local-only immediate teardown: stops any in-flight grab/answer sequence, silences the HQ
    /// voice line, resets the handset, clears the arm IK/anim state, snaps the camera back to
    /// its rest pose, and closes the HQ Order Screen if it was opened. Does not restore
    /// movement beyond what closing the order screen does — the dialogue takes over control.
    /// </summary>
    private void InstantLocalPutDown(PlayerInteractionController player)
    {
        StopLocalSequence();
        bool closeOrderScreen = _localOrderScreenOpen;
        _localHoldMode = LocalHoldMode.None;
        _localOrderScreenOpen = false;

        _voiceAudioSource?.Stop();
        if (phoneSound != null && phonePlaceSound != null)
            phoneSound.PlayOneShot(phonePlaceSound);

        handSet.enabled = false;
        handSet.transform.position = _handsetPos.position;
        handSet.transform.rotation = _handsetPos.rotation;

        if (player != null)
        {
            player.playerAnimationController.SetAnimBool("HoldingPhone", false);
            player.playerAnimationController.DisableLeftArmMask();
            player.playerAnimationController.CamLeftArmRigIKTarget = null;
            player.playerAnimationController.LeftArmIKTarget = null;

            player.playerMovementController.CameraTransform.DOKill();
            player.playerMovementController.ResetCameraPos(instant: true);
        }

        if (closeOrderScreen)
            UIController.Instance?.CloseHQOrderScreen();
        UIController.Instance?.HidePhoneCallBackdrop();
        UIController.Instance?.HideBackButton();
    }

    // ── Observer sequences ────────────────────────────────────────────────────

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the grab animation timing then
    /// attaches the SocketFollow to the remote player's body left-arm container.
    /// </summary>
    private IEnumerator ObserverGrabConstraintSequence(PlayerInteractionController player)
    {
        yield return new WaitForSeconds(.25f);
        if (player == null) yield break;

        // Spectated holder → camera arm socket (what the spectator actually sees); otherwise body arm.
        _observedHolder = player;
        _observedHolderUsesCamSocket = IsSpectatedLocally(player);
        handSet.SetTarget(GetHandSocketForClient(player, isLocalPlayer: _observedHolderUsesCamSocket));
        handSet.enabled = true;
    }

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the put-down animation timing then
    /// detaches the SocketFollow and resets the handset to its resting position.
    /// </summary>
    private IEnumerator ObserverPutDownConstraintSequence()
    {
        yield return new WaitForSeconds(.25f);
        _observedHolder = null;
        _observedHolderUsesCamSocket = false;
        handSet.enabled = false;
        handSet.transform.position = _handsetPos.position;
        handSet.transform.rotation = _handsetPos.rotation;
    }

    // ── Grab / put-down sequences ─────────────────────────────────────────────

    private IEnumerator GrabPhoneSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmIKTarget = _ikTarget;

        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        player.playerAnimationController.EnableLeftArmMask();
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f, .25f);
        player.playerAnimationController.SetAnimBool("HoldingPhone", true);
        yield return new WaitForSeconds(.25f);

        phoneSound.PlayOneShot(phoneGrabSound);

        handSet.SetTarget(GetHandSocketForClient(player, isLocalPlayer: true));
        handSet.enabled = true;

        yield return new WaitForSeconds(.25f);

        player.playerMovementController.ResetCameraPos(false, .25f, null, Vector3.forward * _holdingCameraForwardOffset);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        UIController.Instance.OpenHQOrderScreen();
        _localOrderScreenOpen = true;
        _localSequenceCoroutine = null;
    }

    private IEnumerator PutPhoneDownSequence(PlayerInteractionController player)
    {
        // Stop any in-flight HQ voice line if the player hangs up early.
        _voiceAudioSource?.Stop();

        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.CamLeftArmRigIKTarget = _ikTarget;
        player.playerAnimationController.LeftArmIKTarget = _ikTarget;

        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);
        player.playerAnimationController.DisableLeftArmMask();
        player.playerAnimationController.TurnLeftRigOnAndOff(.2f, .25f);
        player.playerAnimationController.SetAnimBool("HoldingPhone", false);
        yield return new WaitForSeconds(.25f);

        phoneSound.PlayOneShot(phonePlaceSound);
        handSet.enabled = false;
        handSet.transform.position = _handsetPos.position;
        handSet.transform.rotation = _handsetPos.rotation;
        yield return new WaitForSeconds(.25f);

        UIController.Instance?.HidePhoneCallBackdrop();
        UIController.Instance.CloseHQOrderScreen();
        _localOrderScreenOpen = false;
        // Always hide the back button — it may have been shown for a scripted call hang-up.
        UIController.Instance?.HideBackButton();
        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmIKTarget = null;
        player.playerMovementController.SetCanControl(true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Finds the PlayerInteractionController belonging to the given client ID.</summary>
    /// <remarks>
    /// ConnectedClientsList is only reliable on the server/host, so non-host observers fall back to
    /// the spawn manager's player-object table and finally a scan of spawned player objects.
    /// </remarks>
    private PlayerInteractionController FindPlayerByClientId(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return null;

        if (nm.IsServer)
        {
            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.ClientId == clientId && client.PlayerObject != null)
                    return client.PlayerObject.GetComponent<PlayerInteractionController>();
            }
        }

        var playerObjects = nm.SpawnManager?.GetPlayerNetworkObjects(clientId);
        if (playerObjects != null)
        {
            foreach (NetworkObject obj in playerObjects)
            {
                if (obj != null && obj.TryGetComponent(out PlayerInteractionController pic))
                    return pic;
            }
        }

        foreach (PlayerInteractionController pic in FindObjectsByType<PlayerInteractionController>(FindObjectsSortMode.None))
        {
            if (pic.IsSpawned && pic.NetworkObject.IsPlayerObject && pic.OwnerClientId == clientId)
                return pic;
        }
        return null;
    }

    /// <summary>
    /// Returns the correct hand socket transform for the SocketFollow target.
    /// Local players use the cam left-arm container; observers use the body left-arm container.
    /// </summary>
    private Transform GetHandSocketForClient(PlayerInteractionController player, bool isLocalPlayer)
    {
        return isLocalPlayer
            ? player.pickupController.LeftArmCamObjectContainer.transform
            : player.pickupController.LeftArmBodyObjectContainer.transform;
    }
}
