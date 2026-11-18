using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable for the small locker door on the "Mail Slot" prefab ("Door Hinge/Locker Door").
/// Lives on the door leaf's own collider so it is resolved as a raycast target completely
/// separate from the slot's <see cref="PlacementSlot"/> trigger (see "Mail Slot"): while the door
/// is closed its mesh physically blocks the opening, so aiming anywhere at the slot hits this
/// door first and highlights it; once open, the leaf swings clear of the opening so aiming at the
/// slot instead hits the <see cref="PlacementSlot"/> trigger for placing a package.
///
/// Networking model (single source of truth):
///   - <see cref="_isOpen"/> (server-written NetworkVariable) is the only authoritative state.
///     Every peer drives its visuals from it via <see cref="OnOpenStateChanged"/>.
///   - The interacting client predicts locally, then sends the ABSOLUTE desired state
///     (<see cref="RequestSetOpenServerRpc"/>), never a relative "toggle" — a toggle flips
///     whatever the server currently has, so any drift on the requesting peer inverted the
///     result instead of correcting it.
///   - While a local request is in flight (<see cref="_pendingRequests"/>), intermediate
///     NetworkVariable updates are ignored so rapid clicks don't flicker; the server's targeted
///     ack (<see cref="AckSetOpenClientRpc"/>) then reconciles to the authoritative value.
///   - The parent "Mail Slot" is SetActive(false/true) by <see cref="MailCubbyManager"/> every
///     day. Re-activating an Animator resets its parameters (door closed), and SetBool on an
///     inactive Animator is dropped. <see cref="OnEnable"/> therefore re-snaps the hinge to the
///     current state every time the door becomes active again.
///
/// Setup (per "Locker Door" instance):
///   - Attach to the same GameObject as the door leaf's own (non-trigger) Collider.
///   - Assign <see cref="_hingeAnimator"/> to the Animator on the parent "Door Hinge" object
///     ("LockerOpen" bool, "Locker Door Open"/"Locker Door Closed" states). Falls back to
///     <c>GetComponentInParent&lt;Animator&gt;()</c> if unassigned.
///   - Assign <see cref="_audioSource"/>, <see cref="_openClip"/> and <see cref="_closeClip"/>.
/// </summary>
public class LockerDoorInteractable : Interactable
{
    private static readonly int LockerOpenParam = Animator.StringToHash("LockerOpen");
    private static readonly int OpenStateHash = Animator.StringToHash("Locker Door Open");
    private static readonly int ClosedStateHash = Animator.StringToHash("Locker Door Closed");

    [Tooltip("Animator on the parent 'Door Hinge' object that drives the open/close swing via its 'LockerOpen' bool parameter. Falls back to GetComponentInParent<Animator>() if unassigned.")]
    [SerializeField] private Animator _hingeAnimator;

    [Tooltip("Falls back to GetComponent<AudioSource>() if unassigned.")]
    [SerializeField] private AudioSource _audioSource;

    [SerializeField] private AudioClip _openClip;
    [SerializeField] private AudioClip _closeClip;

    private readonly NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>True while the locker door is open (authoritative networked value).</summary>
    public bool IsOpen => _isOpen.Value;

    /// <summary>
    /// True while the door is open as this client currently shows it (local prediction while a
    /// request is in flight, otherwise the authoritative value). Used by <see cref="MailCubbySlot"/>
    /// for placement/pickup gating so it always matches the visible door.
    /// </summary>
    public bool IsOpenLocally => _visualOpen;

    /// <summary>The state this peer's hinge is displaying / should display.</summary>
    private bool _visualOpen;

    /// <summary>Number of this peer's own open/close requests still awaiting a server ack.</summary>
    private int _pendingRequests;

    /// <summary>Set when the hinge needs re-syncing but its Animator was inactive at the time.</summary>
    private bool _snapPending;

    protected override void Awake()
    {
        base.Awake();

        if (_hingeAnimator == null)
            _hingeAnimator = GetComponentInParent<Animator>();

        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();

        // Keep the hinge pose/parameters when the parent Mail Slot is deactivated by the daily
        // cubby reshuffle, instead of resetting to the default (closed) state on re-enable.
        if (_hingeAnimator != null)
            _hingeAnimator.keepAnimatorStateOnDisable = true;
    }

    private void OnEnable()
    {
        SnapVisuals();
    }

    private void Update()
    {
        if (_snapPending)
            SnapVisuals();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isOpen.OnValueChanged += OnOpenStateChanged;

        // Late joiners: OnValueChanged doesn't fire for the value already present at spawn.
        _pendingRequests = 0;
        _visualOpen = _isOpen.Value;
        SnapVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _isOpen.OnValueChanged -= OnOpenStateChanged;
        _pendingRequests = 0;
        base.OnNetworkDespawn();
    }

    public override string GetInteractVerb(PlayerInteractionController player)
    {
        if (!string.IsNullOrEmpty(interactVerb)) return interactVerb;
        return _visualOpen ? Loc.N("Close") : Loc.N("Open");
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        bool willBeOpen = !_visualOpen;

        // Predict locally so the swing and sound respond immediately for the interacting client.
        SetVisualState(willBeOpen, playSound: true);

        _pendingRequests++;
        RequestSetOpenServerRpc(willBeOpen);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSetOpenServerRpc(bool open, ServerRpcParams rpcParams = default)
    {
        if (_isOpen.Value != open)
            _isOpen.Value = open;

        var ackParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
        };
        AckSetOpenClientRpc(_isOpen.Value, ackParams);
    }

    /// <summary>
    /// Sent only to the requesting client once the server has applied its request. Reconciles
    /// the local prediction to the authoritative value once none of its requests are pending.
    /// </summary>
    [ClientRpc]
    private void AckSetOpenClientRpc(bool authoritativeOpen, ClientRpcParams rpcParams = default)
    {
        if (_pendingRequests > 0)
            _pendingRequests--;

        if (_pendingRequests == 0)
            SetVisualState(authoritativeOpen, playSound: false);
    }

    private void OnOpenStateChanged(bool oldValue, bool newValue)
    {
        // Our own prediction stands until the server acks it (prevents flicker on rapid clicks).
        if (_pendingRequests > 0) return;

        // Remote change (another player): animate and play the matching sound.
        SetVisualState(newValue, playSound: true);
    }

    private void SetVisualState(bool open, bool playSound)
    {
        if (_visualOpen == open) return;

        _visualOpen = open;
        ApplyVisuals(open);

        if (playSound)
            PlaySound(open);
    }

    private void ApplyVisuals(bool isOpen)
    {
        if (_hingeAnimator == null) return;

        if (!_hingeAnimator.isActiveAndEnabled)
        {
            // SetBool on an inactive Animator is dropped — snap once it's active again.
            _snapPending = true;
            return;
        }

        _hingeAnimator.SetBool(LockerOpenParam, isOpen);
    }

    /// <summary>Jumps the hinge straight to the current state without playing the swing.</summary>
    private void SnapVisuals()
    {
        if (_hingeAnimator == null || _hingeAnimator.runtimeAnimatorController == null)
        {
            _snapPending = false;
            return;
        }

        if (!_hingeAnimator.isActiveAndEnabled)
        {
            _snapPending = true;
            return;
        }

        _snapPending = false;
        _hingeAnimator.SetBool(LockerOpenParam, _visualOpen);
        _hingeAnimator.Play(_visualOpen ? OpenStateHash : ClosedStateHash, 0, 0f);
        _hingeAnimator.Update(0f);
    }

    private void PlaySound(bool isOpen)
    {
        AudioClip clip = isOpen ? _openClip : _closeClip;
        if (_audioSource != null && clip != null && _audioSource.isActiveAndEnabled)
            _audioSource.PlayOneShot(clip);
    }
}
