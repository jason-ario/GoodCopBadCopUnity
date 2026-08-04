using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable for the small locker door on the "Mail Slot" prefab ("Door Hinge/Locker Door").
/// Lives on the door leaf's own collider so it is resolved as a raycast target completely
/// separate from the slot's <see cref="PlacementSlot"/> trigger (see "Mail Slot"): while the door
/// is closed its mesh physically blocks the opening, so aiming anywhere at the slot hits this
/// door first and highlights it; once open, the leaf swings clear of the opening so aiming at the
/// slot instead hits the <see cref="PlacementSlot"/> trigger for placing a package, and this door
/// is only targeted/highlighted again if the player aims directly back at the open leaf.
///
/// Setup (per "Locker Door" instance):
///   - Attach to the same GameObject as the door leaf's own (non-trigger) Collider, so raycasts
///     hitting the leaf resolve straight to this component without needing an
///     <see cref="InteractableCollider"/> indirection.
///   - Assign <see cref="_hingeAnimator"/> to the Animator on the parent "Door Hinge" object
///     (drives its "LockerOpen" bool parameter — see the "Door Hinge" AnimatorController). Falls
///     back to <c>GetComponentInParent&lt;Animator&gt;()</c> if unassigned.
///   - Assign <see cref="_audioSource"/>, <see cref="_openClip"/> and <see cref="_closeClip"/> for
///     the open/close sounds. Falls back to <c>GetComponent&lt;AudioSource&gt;()</c> if
///     <see cref="_audioSource"/> is unassigned.
///
/// Implements <see cref="IHeldItemPassthrough"/> so the door still opens/closes while the
/// player is holding an item (e.g. the mail package they're about to place through the slot) —
/// without it, PlayerInteractionController.TryItemUse only forwards to InteractWithItem/held-item
/// checks and this door isn't a match for either, so LMB/E silently did nothing whenever the
/// player had something in hand.
/// </summary>
public class LockerDoorInteractable : Interactable, IHeldItemPassthrough
{
    private static readonly int LockerOpenParam = Animator.StringToHash("LockerOpen");

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

    /// <summary>True while the locker door is open.</summary>
    public bool IsOpen => _isOpen.Value;

    protected override void Awake()
    {
        base.Awake();

        if (_hingeAnimator == null)
            _hingeAnimator = GetComponentInParent<Animator>();

        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isOpen.OnValueChanged += OnOpenStateChanged;

        // Sync visuals immediately (covers late joiners — OnValueChanged does not fire
        // retroactively for the value already present at spawn time).
        ApplyVisuals(_isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isOpen.OnValueChanged -= OnOpenStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        bool willBeOpen = !_isOpen.Value;

        // Predict locally so the door swing and sound respond immediately for the interacting
        // client instead of waiting on the server round-trip.
        ApplyVisuals(willBeOpen);
        PlaySound(willBeOpen);

        ToggleServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleServerRpc(ulong senderClientId)
    {
        _isOpen.Value = !_isOpen.Value;
        BroadcastVisualsClientRpc(_isOpen.Value, senderClientId);
    }

    /// <summary>Applies visuals/sound on every client except the one that already predicted it.</summary>
    [ClientRpc]
    private void BroadcastVisualsClientRpc(bool isOpen, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        ApplyVisuals(isOpen);
        PlaySound(isOpen);
    }

    private void OnOpenStateChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastVisualsClientRpc — the
        // initial ApplyVisuals(_isOpen.Value) call in OnNetworkSpawn already covers that case.
    }

    private void ApplyVisuals(bool isOpen)
    {
        if (_hingeAnimator != null)
            _hingeAnimator.SetBool(LockerOpenParam, isOpen);
    }

    private void PlaySound(bool isOpen)
    {
        AudioClip clip = isOpen ? _openClip : _closeClip;
        if (_audioSource != null && clip != null)
            _audioSource.PlayOneShot(clip);
    }
}
