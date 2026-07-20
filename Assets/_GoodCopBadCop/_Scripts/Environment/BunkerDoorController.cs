using DG.Tweening;
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the bunker door's open/closed state and DOTween animation, synced across all clients.
/// Only the local Z euler angle is driven: 0 = closed, 120 = open.
/// X and Y are left untouched so the door never tilts or spins.
/// Requires a <see cref="NetworkObject"/> on this GameObject.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BunkerDoorController : NetworkBehaviour
{
    [Header("Door")]
    [Tooltip("The door Transform to animate.")]
    [SerializeField] private Transform _door;

    [Header("Animation")]
    [Tooltip("Seconds the door takes to swing open.")]
    [SerializeField] private float _openDuration = 2.5f;

    [Tooltip("Ease curve applied to the door-open tween.")]
    [SerializeField] private Ease _openEase = Ease.InOutSine;

    private const float ClosedAngleZ = 0f;
    private const float OpenAngleZ   = 120f;

    private readonly NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Whether the bunker door is currently open (synced across all clients).</summary>
    public bool IsOpen => _isOpen.Value;

    /// <summary>
    /// Fired on every client the moment the door transitions to the open state.
    /// Subscribe server-side to chain tutorial steps after the door is opened.
    /// </summary>
    public static event Action OnDoorOpened;

    // ─── NetworkBehaviour lifecycle ───────────────────────────────────────────

    private void Awake()
    {
        SnapToAngle(ClosedAngleZ);
    }

    public override void OnNetworkSpawn()
    {
        _isOpen.OnValueChanged += OnIsOpenChanged;
        CampaignManager.OnDayChanged += OnDayChanged;

        // Snap to the authoritative state so late-joining clients are correct immediately.
        SnapToAngle(_isOpen.Value ? OpenAngleZ : ClosedAngleZ);
    }

    public override void OnNetworkDespawn()
    {
        _isOpen.OnValueChanged -= OnIsOpenChanged;
        CampaignManager.OnDayChanged -= OnDayChanged;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the bunker door. Can be called from any client; routes through the server.
    /// No-op if already open.
    /// </summary>
    public void Open()
    {
        if (_isOpen.Value) return;

        if (IsServer)
            _isOpen.Value = true;
        else
            OpenServerRpc();
    }

    /// <summary>
    /// Snaps the door back to its closed angle instantly. Can be called from any client;
    /// routes through the server.
    /// </summary>
    public void Reset()
    {
        if (IsServer)
            _isOpen.Value = false;
        else
            ResetServerRpc();
    }

    // ─── Server RPCs ──────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void OpenServerRpc()
    {
        if (!_isOpen.Value)
            _isOpen.Value = true;
    }

    [Rpc(SendTo.Server)]
    private void ResetServerRpc()
    {
        _isOpen.Value = false;
    }

    // ─── Day-change callback ──────────────────────────────────────────────────

    /// <summary>
    /// Resets the door to closed at the start of each new day.
    /// Only the server writes the NetworkVariable; clients receive the state via OnIsOpenChanged.
    /// </summary>
    private void OnDayChanged(int day)
    {
        if (!IsServer) return;
        _isOpen.Value = false;
    }

    // ─── NetworkVariable callbacks ────────────────────────────────────────────

    private void OnIsOpenChanged(bool previous, bool current)
    {
        if (current)
        {
            TweenDoorZ(OpenAngleZ);
            OnDoorOpened?.Invoke();
        }
        else
        {
            if (_door != null) _door.DOKill();
            SnapToAngle(ClosedAngleZ);
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void TweenDoorZ(float targetZ)
    {
        if (_door == null) return;
        _door.DOKill();
        _door.DOLocalRotateQuaternion(Quaternion.Euler(0f, 0f, targetZ), _openDuration)
             .SetEase(_openEase);
    }

    private void SnapToAngle(float z)
    {
        if (_door == null) return;
        _door.localRotation = Quaternion.Euler(0f, 0f, z);
    }
}
