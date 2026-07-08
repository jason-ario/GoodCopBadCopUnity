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

    [Header("Phone Call")]
    [Tooltip("All tasks HQ can deliver. Picked by TriggerCall(index) or TriggerRandomCall().")]
    [SerializeField] private PhoneTaskData[] _availableTasks;
    [Tooltip("Clip played as a one-shot each time the phone rings.")]
    [SerializeField] private AudioClip _phoneRingClip;
    [Tooltip("Dedicated AudioSource used for the ring one-shots. Should not be the same as phoneSound.")]
    [SerializeField] private AudioSource _ringAudioSource;
    [Tooltip("AudioSource used to play the HQ voice line when the call is answered.")]
    [SerializeField] private AudioSource _voiceAudioSource;
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

    // Client-only: drives the ring cycle (animation + one-shot audio).
    private Coroutine _ringCycleCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

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
    /// Does nothing if the phone is already ringing or currently grabbed.
    /// </summary>
    public void TriggerScriptedCall(Action onAnswered)
    {
        if (!IsServer) return;
        if (BlockAllCalls) return;
        if (_isRinging.Value || _isGrabbed.Value) return;

        _isScriptedCall = true;
        _scriptedCallAnsweredCallback = onAnswered;
        _pendingTaskIndex = -2; // sentinel: scripted call — no task or debug voice delivered
        _isRinging.Value = true;

        StartRingingClientRpc();
        _ringTimeoutCoroutine = StartCoroutine(RingTimeoutRoutine());
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
            StartCoroutine(AnswerCallSequence(player, taskIndex));
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
    /// </summary>
    private IEnumerator AnswerCallSequence(PlayerInteractionController player, int taskIndex)
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

        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        // Stream HQ voice line.
        if (taskIndex >= 0 && _availableTasks != null && taskIndex < _availableTasks.Length
            && _availableTasks[taskIndex] != null)
        {
            PhoneTaskData data = _availableTasks[taskIndex];

            DialogueManager.Instance.SpawnSubtitles(data.VoiceLine, _hqSpeakerName, Color.white);

            if (_voiceAudioSource != null && data.VoiceAudioClips != null && data.VoiceAudioClips.Length > 0)
            {
                DialogueManager.Instance.PlayDialogueAudio(data.VoiceLine, data.VoiceAudioClips, _voiceAudioSource);
            }
        }
        else if (taskIndex == -1)
        {
            // Debug call — show the hardcoded line and play debug audio.
            DialogueManager.Instance.SpawnSubtitles(_debugVoiceLine, _hqSpeakerName, Color.white);

            if (_voiceAudioSource != null && _debugVoiceAudioClips != null && _debugVoiceAudioClips.Length > 0)
            {
                DialogueManager.Instance.PlayDialogueAudio(
                    _debugVoiceLine, _debugVoiceAudioClips, _voiceAudioSource);
            }
        }
        // taskIndex == -2: scripted call — voice/subtitles handled by ScriptedDialogueRunner.
        // Show only a hang-up back button so the player can put the phone down when done.
        if (taskIndex == -2)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            UIController.Instance?.ShowBackButton(() => HangUp(localClientId));
        }
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

    // ── Regular grab / put-down ───────────────────────────────────────────────

    /// <summary>
    /// Requests the server to hang up the phone on behalf of the given client.
    /// Only succeeds if that client is the one currently holding the handset.
    /// </summary>
    public void HangUp(ulong clientId)
    {
        RequestPutDownServerRpc(clientId);
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

        _isGrabbed.Value = false;
        _grabbingClientId.Value = ulong.MaxValue;

        ExecutePutDownSequenceClientRpc(clientId);
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
            StartCoroutine(GrabPhoneSequence(player));
        else
            StartCoroutine(ObserverGrabConstraintSequence(player));
    }

    [ClientRpc]
    private void ExecutePutDownSequenceClientRpc(ulong clientId)
    {
        bool isLocalPlayer = NetworkManager.Singleton.LocalClientId == clientId;
        PlayerInteractionController player = isLocalPlayer
            ? NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerInteractionController>()
            : FindPlayerByClientId(clientId);

        if (player == null) return;

        if (isLocalPlayer)
            StartCoroutine(PutPhoneDownSequence(player));
        else
            StartCoroutine(ObserverPutDownConstraintSequence());
    }

    // ── Observer sequences ────────────────────────────────────────────────────

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the grab animation timing then
    /// attaches the SocketFollow to the remote player's body left-arm container.
    /// </summary>
    private IEnumerator ObserverGrabConstraintSequence(PlayerInteractionController player)
    {
        yield return new WaitForSeconds(.25f);
        handSet.SetTarget(GetHandSocketForClient(player, isLocalPlayer: false));
        handSet.enabled = true;
    }

    /// <summary>
    /// Run on all non-grabbing clients: waits to match the put-down animation timing then
    /// detaches the SocketFollow and resets the handset to its resting position.
    /// </summary>
    private IEnumerator ObserverPutDownConstraintSequence()
    {
        yield return new WaitForSeconds(.25f);
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

        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);
        player.playerAnimationController.CamLeftArmRigIKTarget = null;
        player.playerAnimationController.LeftArmIKTarget = null;
        player.playerMovementController.SetCanControl(true);

        UIController.Instance.OpenHQOrderScreen();
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

        UIController.Instance.CloseHQOrderScreen();
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
    private PlayerInteractionController FindPlayerByClientId(ulong clientId)
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == clientId && client.PlayerObject != null)
                return client.PlayerObject.GetComponent<PlayerInteractionController>();
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
