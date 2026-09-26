using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A 3D world-space arrow that hovers above a target Transform and bobs up and down.
/// Usually managed by <see cref="TutorialMarkerManager"/>'s pool, but some tutorial beats
/// (e.g. Day 1's clock-in and lever arrows) instead pre-place a <see cref="TutorialMarker"/>
/// directly in the scene and toggle it with plain <c>SetActive</c> calls. Every enabled instance —
/// pooled or pre-placed — self-registers in <see cref="ActiveInstances"/> so systems like
/// <see cref="TutorialArrowScreenIndicator"/> can find all of them uniformly.
///
/// Design rule: anything an arrow points at that has an <see cref="Interactable"/> is highlighted
/// (authored default profile) for exactly as long as the arrow is enabled. The marker owns this —
/// callers never pair arrows with manual <c>SetForceHighlight</c> calls. Targets come from:
/// <list type="number">
/// <item>the <see cref="Interactable"/> on / above / below the Transform passed to <see cref="Show"/>;</item>
/// <item>if that Transform is itself a pre-placed <see cref="TutorialMarker"/> (Day 1's anchors),
/// that marker's <c>highlightTargets</c>;</item>
/// <item>this marker's own <c>highlightTargets</c> (pre-placed arrows toggled via <c>SetActive</c>);</item>
/// <item>runtime additions via <see cref="AddHighlightTarget"/> /
/// <see cref="TutorialMarkerManager.AddHighlight"/> for network-spawned objects (docs, coupons).</item>
/// </list>
/// Claims are reference-counted across markers and use <see cref="HighlightHold.TutorialArrow"/>, so
/// they never clobber other holds. A <see cref="PickableObject"/> stops glowing while it is held.
/// </summary>
public class TutorialMarker : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Highlight")]
    [Tooltip("Interactables highlighted while this arrow is shown. Required for pre-placed arrows (and pre-placed " +
             "arrow anchors passed to TutorialMarkerManager.Mark) since they are not parented to what they point at. " +
             "Pooled arrows marking an Interactable's Transform resolve it automatically.")]
    [SerializeField] private Interactable[] highlightTargets;

    [Header("Positioning")]
    [Tooltip("World-space height above the target's pivot.")]
    [SerializeField] private float hoverHeight = 1.5f;

    [Header("Bob Animation")]
    [Tooltip("Total peak-to-peak distance of the bob.")]
    [SerializeField] private float bobAmplitude = 0.15f;
    [Tooltip("Full cycles per second.")]
    [SerializeField] private float bobFrequency = 1.2f;

    [Header("Fade")]
    [Tooltip("Seconds to fade in / out when shown or when crossing the visibility range.")]
    [SerializeField] private float fadeDuration = 0.3f;
    [Tooltip("The marker is only visible while the camera is within this many meters. It fades out beyond it and fades back in on return.")]
    [SerializeField] private float visibleRange = 20f;

    [Header("Distance Scaling")]
    [Tooltip("Camera distance at which the marker renders at its original authored size (1x).")]
    [SerializeField] private float referenceDistance = 10f;
    [Tooltip("Smallest allowed scale multiplier, applied when the camera is very close.")]
    [SerializeField] private float minScaleMultiplier = 0.5f;
    [Tooltip("Largest allowed scale multiplier, applied when the camera is far away. Keeps the marker from becoming absurdly huge at long range.")]
    [SerializeField] private float maxScaleMultiplier = 2.5f;

    // ── Private state ────────────────────────────────────────────────────────
    private Transform _target;
    private SpriteRenderer[] _renderers;
    private Color[] _baseColors;     // authored colors; fade multiplies their alpha
    private float _alpha;            // current fade value, 0 = hidden, 1 = fully visible
    private float _bobOffset;   // per-instance phase offset to desync multiple markers
    private Vector3 _baseLocalScale; // authored scale at referenceDistance, before distance scaling

    private static readonly HashSet<TutorialMarker> _activeInstances = new();

    // Highlight ownership (see class summary).
    private readonly List<Interactable> _highlightSet = new();   // everything this showing wants lit
    private readonly HashSet<Interactable> _claimed = new();      // subset currently claimed by this marker
    private static readonly Dictionary<Interactable, int> _claimCounts = new(); // claims across all markers

    /// <summary>
    /// Every <see cref="TutorialMarker"/> currently enabled in the scene, whether it was shown via
    /// <see cref="TutorialMarkerManager"/>'s pool or is a pre-placed scene arrow toggled directly by
    /// a Day script. Do not mutate the returned collection.
    /// </summary>
    public static IReadOnlyCollection<TutorialMarker> ActiveInstances => _activeInstances;

    /// <summary>
    /// World-space point to aim at that ignores the bob animation, so dependents like
    /// <see cref="TutorialArrowScreenIndicator"/> don't jitter every frame. For a marker tracking a
    /// moving target, this is the bottom of its bob range (target pivot + hoverHeight - bobAmplitude).
    /// For a pre-placed marker with no target, this is just its authored (unanimated) transform
    /// position, since the bob for those comes from an Animator clip on a child mesh, not this root.
    /// </summary>
    public Vector3 StableAnchorPosition => _target != null
        ? _target.position + Vector3.up * (hoverHeight - bobAmplitude)
        : transform.position;

    /// <summary>
    /// True if <paramref name="viewerPosition"/> is within <c>visibleRange</c> of this marker's
    /// <see cref="StableAnchorPosition"/>. Shared with <see cref="TutorialArrowScreenIndicator"/> so the
    /// world arrow and its screen-edge arrow use the same range rule.
    /// </summary>
    public bool IsWithinVisibleRange(Vector3 viewerPosition) =>
        (viewerPosition - StableAnchorPosition).sqrMagnitude <= visibleRange * visibleRange;

    // ── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _baseColors[i] = _renderers[i].color;
        _baseLocalScale = transform.localScale;
    }

    private void OnEnable()
    {
        _activeInstances.Add(this);

        // Start hidden so the marker fades in when shown (if the camera is in range).
        _alpha = 0f;
        ApplyAlpha();

        RebuildHighlightSet();
        SyncHighlights();
    }

    private void OnDisable()
    {
        _activeInstances.Remove(this);
        ReleaseAllHighlights();
        _highlightSet.Clear();
    }

    private void LateUpdate()
    {
        // Cheap per-frame re-check so held pickables stop glowing and dropped ones resume.
        SyncHighlights();

        Camera cam = Camera.main;
        if (cam != null)
        {
            // Fade based on camera distance to the marker's stable (non-bobbing) anchor.
            bool inRange = IsWithinVisibleRange(cam.transform.position);
            float targetAlpha = inRange ? 1f : 0f;
            if (!Mathf.Approximately(_alpha, targetAlpha))
            {
                float step = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
                _alpha = Mathf.MoveTowards(_alpha, targetAlpha, step);
                ApplyAlpha();
            }

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
        _target = null;
    }

    /// <summary>
    /// Adds a runtime <see cref="Interactable"/> (e.g. a network-spawned document or coupon) to this
    /// arrow's highlight set for the current showing. Cleared automatically when the arrow hides.
    /// </summary>
    public void AddHighlightTarget(Interactable interactable)
    {
        if (interactable == null || _highlightSet.Contains(interactable)) return;

        _highlightSet.Add(interactable);
        if (isActiveAndEnabled) SyncHighlights();
    }

    // ── Highlight ownership ──────────────────────────────────────────────────

    private void RebuildHighlightSet()
    {
        _highlightSet.Clear();
        AddRange(highlightTargets);

        if (_target == null) return;

        // Day 1 marks pre-placed arrow anchors; their authored targets define what gets lit.
        if (_target.TryGetComponent(out TutorialMarker anchor))
        {
            if (anchor != this) AddRange(anchor.highlightTargets);
            return;
        }

        Interactable found = _target.GetComponentInParent<Interactable>();
        if (found == null) found = _target.GetComponentInChildren<Interactable>();
        if (found != null && !_highlightSet.Contains(found)) _highlightSet.Add(found);
    }

    private void AddRange(Interactable[] source)
    {
        if (source == null) return;
        foreach (Interactable i in source)
            if (i != null && !_highlightSet.Contains(i)) _highlightSet.Add(i);
    }

    private void SyncHighlights()
    {
        for (int i = 0; i < _highlightSet.Count; i++)
        {
            Interactable target = _highlightSet[i];
            bool want = target != null
                        && target.gameObject.activeInHierarchy
                        && !(target is PickableObject pickable && pickable.IsHeld);
            bool has = _claimed.Contains(target);

            if (want && !has) Claim(target);
            else if (!want && has) Release(target);
        }
    }

    private void ReleaseAllHighlights()
    {
        foreach (Interactable target in new List<Interactable>(_claimed))
            Release(target);
        _claimed.Clear();
    }

    private void Claim(Interactable target)
    {
        _claimed.Add(target);
        _claimCounts.TryGetValue(target, out int count);
        _claimCounts[target] = count + 1;
        if (count == 0) target.SetForceHighlight(true, HighlightHold.TutorialArrow);
    }

    private void Release(Interactable target)
    {
        _claimed.Remove(target);
        if (!_claimCounts.TryGetValue(target, out int count)) return;

        if (count > 1)
        {
            _claimCounts[target] = count - 1;
            return;
        }

        _claimCounts.Remove(target);
        if (target != null) target.SetForceHighlight(false, HighlightHold.TutorialArrow);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ApplyAlpha()
    {
        if (_renderers == null) return;

        bool visible = _alpha > 0f;
        for (int i = 0; i < _renderers.Length; i++)
        {
            SpriteRenderer sr = _renderers[i];
            if (sr == null) continue;

            Color c = _baseColors[i];
            c.a *= _alpha;
            sr.color = c;
            sr.enabled = visible; // skip drawing entirely when fully faded out
        }
    }

}
