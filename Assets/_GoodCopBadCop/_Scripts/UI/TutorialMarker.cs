using System.Collections;
using UnityEngine;

/// <summary>
/// A 3D world-space arrow that hovers above a target Transform and bobs up and down.
/// Managed exclusively by <see cref="TutorialMarkerManager"/>. Do not instantiate directly.
/// </summary>
public class TutorialMarker : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Positioning")]
    [Tooltip("World-space height above the target's pivot.")]
    [SerializeField] private float hoverHeight = 1.5f;

    [Header("Bob Animation")]
    [Tooltip("Total peak-to-peak distance of the bob.")]
    [SerializeField] private float bobAmplitude = 0.15f;
    [Tooltip("Full cycles per second.")]
    [SerializeField] private float bobFrequency = 1.2f;

    [Header("Fade")]
    [Tooltip("Seconds to fade in / out when shown or hidden.")]
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Distance Scaling")]
    [Tooltip("Camera distance at which the marker renders at its original authored size (1x).")]
    [SerializeField] private float referenceDistance = 10f;
    [Tooltip("Smallest allowed scale multiplier, applied when the camera is very close.")]
    [SerializeField] private float minScaleMultiplier = 0.5f;
    [Tooltip("Largest allowed scale multiplier, applied when the camera is far away. Keeps the marker from becoming absurdly huge at long range.")]
    [SerializeField] private float maxScaleMultiplier = 2.5f;

    // ── Private state ────────────────────────────────────────────────────────
    private Transform _target;
    private Renderer[] _renderers;
    private float _bobOffset;   // per-instance phase offset to desync multiple markers
    private Vector3 _baseLocalScale; // authored scale at referenceDistance, before distance scaling

    // ── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _baseLocalScale = transform.localScale;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 camPos = cam.transform.position;
            Vector3 direction = camPos - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0f)
                transform.rotation = Quaternion.LookRotation(direction);

            // Keep the marker at a roughly consistent on-screen size regardless of
            // distance, clamped so it never shrinks to nothing up close or balloons
            // to an unreasonable size far away.
            float distance = Vector3.Distance(camPos, transform.position);
            float scaleMultiplier = Mathf.Clamp(distance / referenceDistance, minScaleMultiplier, maxScaleMultiplier);
            transform.localScale = _baseLocalScale * scaleMultiplier;
        }

        if (_target == null) return;

        float bob = Mathf.Sin((Time.time + _bobOffset) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.position = _target.position + Vector3.up * (hoverHeight + bob);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Overrides the world-space hover height above the target's pivot.</summary>
    public void SetHoverHeight(float height) => hoverHeight = height;

    /// <summary>Attaches the marker to <paramref name="target"/> and fades it in.</summary>
    public void Show(Transform target)
    {
        _target = target;
        _bobOffset = Random.Range(0f, 1f);
        gameObject.SetActive(true);
    }

    /// <summary>Fades the marker out, then deactivates it and clears the target.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

}
