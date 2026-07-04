using DG.Tweening;
using UnityEngine;

/// <summary>
/// Manages the bunker door's open/closed state and DOTween animation.
/// Only the local Z euler angle is driven: 0 = closed, 120 = open.
/// X and Y are left untouched so the door never tilts or spins.
/// </summary>
public class BunkerDoorController : MonoBehaviour
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

    /// <summary>Whether the bunker door is currently open.</summary>
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        SnapToAngle(ClosedAngleZ);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Tweens the door to its open angle. No-op if already open.</summary>
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        TweenDoorZ(OpenAngleZ);
    }

    /// <summary>Snaps the door back to its closed angle instantly.</summary>
    public void Reset()
    {
        IsOpen = false;
        if (_door != null) _door.DOKill();
        SnapToAngle(ClosedAngleZ);
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
