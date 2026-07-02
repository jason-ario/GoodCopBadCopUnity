using DG.Tweening;
using UnityEngine;

/// <summary>
/// Manages the bunker door's open/closed state and DOTween animation.
/// Only the local X euler angle is driven: 0 = closed, -120 = open.
/// Y and Z are left untouched so the door never twists.
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

    private const float ClosedAngleX = 0f;
    private const float OpenAngleX   = -120f;

    /// <summary>Whether the bunker door is currently open.</summary>
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        SnapToAngle(ClosedAngleX);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Tweens the door to its open angle. No-op if already open.</summary>
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        TweenDoorX(OpenAngleX);
    }

    /// <summary>Snaps the door back to its closed angle instantly.</summary>
    public void Reset()
    {
        IsOpen = false;
        if (_door != null) _door.DOKill();
        SnapToAngle(ClosedAngleX);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void TweenDoorX(float targetX)
    {
        if (_door == null) return;
        _door.DOKill();
        _door.DOLocalRotateQuaternion(Quaternion.Euler(targetX, 0f, 0f), _openDuration)
             .SetEase(_openEase);
    }

    private void SnapToAngle(float x)
    {
        if (_door == null) return;
        _door.localRotation = Quaternion.Euler(x, 0f, 0f);
    }
}
