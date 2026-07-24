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

    [Header("Slam")]
    [Tooltip("Seconds the door takes to slam shut when a player interacts with it while open.")]
    [SerializeField] private float _slamDuration = 0.4f;

    [Tooltip("Ease curve applied to the slam-shut tween.")]
    [SerializeField] private Ease _slamEase = Ease.InQuad;

    [Tooltip("Audio source used to play the open and slam sounds. Falls back to no sound if unassigned.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Sound played when the door swings open. Stops early if the door is slammed shut mid-play.")]
    [SerializeField] private AudioClip _openSound;

    [Tooltip("Sound played when the door is slammed shut by a player. Cuts off the open sound if it's still playing.")]
    [SerializeField] private AudioClip _slamSound;

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
            OpenInternal();
        else
            OpenServerRpc();
    }

    /// <summary>
    /// Slams the bunker door shut with a fast animated tween and a slam sound.
    /// Can be called from any client; routes through the server. No-op if already closed.
    /// </summary>
    public void Close()
    {
        if (!_isOpen.Value) return;

        if (IsServer)
            CloseInternal();
        else
            CloseServerRpc();
    }

    /// <summary>
    /// Snaps the door back to its closed angle instantly and silently. Can be called from
    /// any client; routes through the server.
    /// </summary>
    public void Reset()
    {
        if (IsServer)
            ResetInternal();
        else
            ResetServerRpc();
    }

    // ─── Server RPCs ──────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void OpenServerRpc()
    {
        if (!_isOpen.Value)
            OpenInternal();
    }

    [Rpc(SendTo.Server)]
    private void CloseServerRpc()
    {
        if (_isOpen.Value)
            CloseInternal();
    }

    [Rpc(SendTo.Server)]
    private void ResetServerRpc() => ResetInternal();

    // ─── Server-only state changes ────────────────────────────────────────────

    private void OpenInternal()
    {
        _isOpen.Value = true;
        PlayOpenClientRpc();
    }

    private void CloseInternal()
    {
        _isOpen.Value = false;
        PlaySlamClientRpc();
    }

    private void ResetInternal()
    {
        _isOpen.Value = false;
        ResetVisualsClientRpc();
    }

    [ClientRpc]
    private void PlayOpenClientRpc()
    {
        TweenDoorZ(OpenAngleZ, _openDuration, _openEase);

        // Played on the main track (not PlayOneShot) so a mid-play Close() can cut it off.
        if (_audioSource != null && _openSound != null)
        {
            _audioSource.loop = false;
            _audioSource.clip = _openSound;
            _audioSource.Play();
        }

        OnDoorOpened?.Invoke();
    }

    [ClientRpc]
    private void PlaySlamClientRpc()
    {
        TweenDoorZ(ClosedAngleZ, _slamDuration, _slamEase);

        if (_audioSource != null)
        {
            // Cancel the open sound if it's still playing, then layer the slam on top.
            _audioSource.Stop();

            if (_slamSound != null)
                _audioSource.PlayOneShot(_slamSound);
        }
    }

    [ClientRpc]
    private void ResetVisualsClientRpc()
    {
        if (_door != null) _door.DOKill();
        SnapToAngle(ClosedAngleZ);

        if (_audioSource != null)
            _audioSource.Stop();
    }

    // ─── Day-change callback ──────────────────────────────────────────────────

    /// <summary>
    /// Resets the door to closed at the start of each new day.
    /// Only the server writes the NetworkVariable; clients receive the state via ResetInternal.
    /// </summary>
    private void OnDayChanged(int day)
    {
        if (!IsServer) return;
        ResetInternal();
    }

    // ─── NetworkVariable callbacks ────────────────────────────────────────────

    /// <summary>
    /// Intentionally a no-op: all visuals (open tween, slam tween + sound, instant reset)
    /// are driven explicitly via the ClientRpcs above so late-joining clients don't
    /// re-trigger animations. Late joiners are synced instantly via OnNetworkSpawn's
    /// SnapToAngle call instead.
    /// </summary>
    private void OnIsOpenChanged(bool previous, bool current) { }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void TweenDoorZ(float targetZ, float duration, Ease ease)
    {
        if (_door == null) return;
        _door.DOKill();
        _door.DOLocalRotateQuaternion(Quaternion.Euler(0f, 0f, targetZ), duration)
             .SetEase(ease);
    }

    private void SnapToAngle(float z)
    {
        if (_door == null) return;
        _door.localRotation = Quaternion.Euler(0f, 0f, z);
    }
}
