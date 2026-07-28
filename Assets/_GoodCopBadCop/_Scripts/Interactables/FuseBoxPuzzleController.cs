using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the fuse-box door animation and tracks puzzle readiness.
///
/// Puzzle readiness: all assigned <see cref="FuseSlot"/> instances must have a fuse
/// inserted. When ready, the status light turns green. The <see cref="PowerSwitch"/>
/// reads <see cref="IsReady"/> server-side before calling
/// <see cref="ElectricityController.PowerOn"/>.
///
/// Also exposes <see cref="OnBoxInteracted"/> and <see cref="OnFuseCountChanged"/>,
/// broadcast on all clients, so callers (e.g. <c>Day_03</c>) can drive a step-by-step
/// tutorial objective list without polling.
///
/// Setup notes:
///   - Assign the three child <see cref="FuseSlot"/> components to <see cref="_fuseSlots"/>.
///   - Assign the fuse-box status <see cref="Light"/> to <see cref="_statusLight"/>.
///   - Optionally tune <see cref="_readyColor"/> / <see cref="_notReadyColor"/>.
/// </summary>
public class FuseBoxPuzzleController : Interactable
{
    // ── Door ──────────────────────────────────────────────────────────────────

    [Header("Door")]
    [SerializeField] private Transform _doorHinge;
    [SerializeField] private float _doorOpenAngle  = -45f;
    [SerializeField] private float _doorClosedAngle = 90f;
    [SerializeField] private float _doorAnimDuration = 0.4f;

    [Header("Door Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _doorOpenClip;
    [SerializeField] private AudioClip _doorCloseClip;

    private NetworkVariable<bool> _doorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool _isAnimating = false;

    // ── Puzzle ────────────────────────────────────────────────────────────────

    [Header("Puzzle")]
    [Tooltip("The three FuseSlot children — all must be filled before the box is ready.")]
    [SerializeField] private FuseSlot[] _fuseSlots;

    [Header("Status Light")]
    [Tooltip("Point light on the fuse box panel. Turns green when all slots are filled.")]
    [SerializeField] private Light _statusLight;
    [SerializeField] private Color _readyColor    = Color.green;
    [SerializeField] private Color _notReadyColor = Color.red;

    /// <summary>
    /// True on all clients when every assigned fuse slot contains a fuse.
    /// Computed locally from each slot's server-authoritative NetworkVariable.
    /// </summary>
    public bool IsReady => _fuseSlots != null && _fuseSlots.Length > 0
                           && _fuseSlots.All(s => s != null && s.IsFilled);

    /// <summary>Total number of assigned fuse slots.</summary>
    public int FuseSlotCount => _fuseSlots?.Length ?? 0;

    /// <summary>Number of assigned fuse slots that currently hold a fuse.</summary>
    public int FilledSlotCount => _fuseSlots == null ? 0 : _fuseSlots.Count(s => s != null && s.IsFilled);

    /// <summary>
    /// Fired on all clients whenever the box door is toggled (opened or closed) via
    /// <see cref="Interact"/>. Used to drive the "find the fuse box" objective —
    /// listeners should unsubscribe after the first invocation if they only care about
    /// the initial discovery.
    /// </summary>
    public event Action OnBoxInteracted;

    /// <summary>
    /// Fired on all clients whenever a fuse is inserted into or extracted from any slot.
    /// Passes the current filled-slot count and the total slot count.
    /// </summary>
    public event Action<int, int> OnFuseCountChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        // Subscribe before network spawn so no state change is ever missed.
        if (_fuseSlots != null)
        {
            foreach (var slot in _fuseSlots)
            {
                if (slot == null) continue;
                slot.OnFuseInserted  += OnSlotStateChanged;
                slot.OnFuseExtracted += OnSlotStateChanged;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _doorOpen.OnValueChanged += OnDoorStateChanged;

        ApplyDoorInstant(_doorOpen.Value);
        UpdateStatusLight();
    }

    public override void OnNetworkDespawn()
    {
        _doorOpen.OnValueChanged -= OnDoorStateChanged;

        if (_fuseSlots != null)
        {
            foreach (var slot in _fuseSlots)
            {
                if (slot == null) continue;
                slot.OnFuseInserted  -= OnSlotStateChanged;
                slot.OnFuseExtracted -= OnSlotStateChanged;
            }
        }
    }

    private void OnSlotStateChanged()
    {
        UpdateStatusLight();
        OnFuseCountChanged?.Invoke(FilledSlotCount, FuseSlotCount);
    }

    private void UpdateStatusLight()
    {
        if (_statusLight == null) return;
        _statusLight.color = IsReady ? _readyColor : _notReadyColor;
    }

    // ── Door interaction ──────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (!_isAnimating)
            ToggleDoorServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDoorServerRpc()
    {
        _doorOpen.Value = !_doorOpen.Value;
        BroadcastDoorStateClientRpc(_doorOpen.Value);
    }

    [ClientRpc]
    private void BroadcastDoorStateClientRpc(bool isOpen)
    {
        OnBoxInteracted?.Invoke();
        PlayDoorSound(isOpen);
        StartCoroutine(AnimateDoor(isOpen));
    }

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        // Handled by BroadcastDoorStateClientRpc for active clients.
        // Fires for late-joiners already synced via OnNetworkSpawn.
    }

    private void PlayDoorSound(bool isOpen)
    {
        if (_audioSource == null) return;
        AudioClip clip = isOpen ? _doorOpenClip : _doorCloseClip;
        if (clip != null)
            _audioSource.PlayOneShot(clip);
    }

    private IEnumerator AnimateDoor(bool isOpen)
    {
        _isAnimating = true;

        float startAngle = _doorHinge.localEulerAngles.y;
        if (startAngle > 180f) startAngle -= 360f;

        float targetAngle = isOpen ? _doorOpenAngle : _doorClosedAngle;
        float elapsed = 0f;

        while (elapsed < _doorAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _doorAnimDuration);
            _doorHinge.localEulerAngles = new Vector3(
                _doorHinge.localEulerAngles.x,
                Mathf.Lerp(startAngle, targetAngle, t),
                _doorHinge.localEulerAngles.z
            );
            yield return null;
        }

        _doorHinge.localEulerAngles = new Vector3(
            _doorHinge.localEulerAngles.x,
            targetAngle,
            _doorHinge.localEulerAngles.z
        );

        _isAnimating = false;
    }

    private void ApplyDoorInstant(bool isOpen)
    {
        if (_doorHinge == null) return;
        float angle = isOpen ? _doorOpenAngle : _doorClosedAngle;
        _doorHinge.localEulerAngles = new Vector3(
            _doorHinge.localEulerAngles.x,
            angle,
            _doorHinge.localEulerAngles.z
        );
    }
}
