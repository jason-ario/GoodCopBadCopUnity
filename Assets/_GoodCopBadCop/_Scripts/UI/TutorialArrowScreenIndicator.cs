using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Screen-edge indicator for active tutorial arrows. For every world-space <see cref="TutorialMarker"/>
/// currently enabled in the scene (see <see cref="TutorialMarker.ActiveInstances"/> — covers both markers
/// shown via <see cref="TutorialMarkerManager"/>'s pool and pre-placed scene arrows toggled directly by
/// Day scripts), pools a 2D UI arrow that:
///  - Hides itself while the marker's arrow is inside the camera's view.
///  - Otherwise clamps itself to the edge of the screen and rotates to point toward the marker's
///    on-screen direction, so the player always knows which way to look/travel to find it.
/// Tracks each marker's <see cref="TutorialMarker.StableAnchorPosition"/> (bottom of its bob range)
/// rather than its live, bobbing transform position, so the indicator doesn't jitter every frame.
/// Lives on the Player HUD canvas, sibling to <see cref="CompassController"/>. Purely additive —
/// does not read or modify <see cref="TutorialMarker"/>/<see cref="TutorialMarkerManager"/> state, only observes it.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TutorialArrowScreenIndicator : MonoBehaviour
{
    [Tooltip("Inactive template cloned for every active tutorial marker. Must contain an Image/Graphic " +
             "whose authored 'down' (-Y) direction is the arrow's pointing direction (matches the " +
             "project's Arrow.png sprite).")]
    [SerializeField] private RectTransform arrowTemplate;

    [Tooltip("Container the pooled arrows are parented to and whose rect defines the screen bounds the " +
             "arrows clamp to. Defaults to this object's own RectTransform (should be a full-screen HUD " +
             "layer) if left empty.")]
    [SerializeField] private RectTransform indicatorContainer;

    [Tooltip("Pixels kept clear between the arrow's pivot and the container's edge, so the arrow doesn't clip off-canvas.")]
    [SerializeField] private float edgeMargin = 64f;

    private readonly Dictionary<TutorialMarker, RectTransform> _activeArrows = new();
    private readonly List<RectTransform> _pool = new();
    private readonly List<TutorialMarker> _staleMarkers = new();

    private void Awake()
    {
        if (indicatorContainer == null)
            indicatorContainer = (RectTransform)transform;

        if (arrowTemplate == null)
        {
            Debug.LogError("[TutorialArrowScreenIndicator] arrowTemplate is not assigned. Assign a UI arrow prefab/child in the Inspector.", this);
            return;
        }

        arrowTemplate.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (arrowTemplate == null || indicatorContainer == null) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            ReleaseAll();
            return;
        }

        IReadOnlyCollection<TutorialMarker> wanted = TutorialMarker.ActiveInstances;

        ReleaseStaleArrows(wanted);

        if (wanted.Count == 0) return;

        Vector2 halfSize = indicatorContainer.rect.size * 0.5f - new Vector2(edgeMargin, edgeMargin);
        halfSize.x = Mathf.Max(halfSize.x, 1f);
        halfSize.y = Mathf.Max(halfSize.y, 1f);

        foreach (TutorialMarker marker in wanted)
        {
            if (marker == null || !marker.gameObject.activeInHierarchy) continue;
            UpdateArrow(marker, cam, halfSize);
        }
    }

    // ── Per-marker update ────────────────────────────────────────────────────

    private void UpdateArrow(TutorialMarker marker, Camera cam, Vector2 halfSize)
    {
        Vector3 viewportPoint = cam.WorldToViewportPoint(marker.StableAnchorPosition);
        bool behindCamera = viewportPoint.z <= 0f;

        bool onScreen = !behindCamera
            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;

        if (onScreen)
        {
            ReleaseArrow(marker);
            return;
        }

        if (!_activeArrows.TryGetValue(marker, out RectTransform arrow))
        {
            arrow = GetFromPool();
            _activeArrows[marker] = arrow;
        }

        // Mirror the projection for points behind the camera so the arrow points the short way
        // around the screen toward the target instead of flipping to the diametrically opposite side.
        if (behindCamera)
        {
            viewportPoint.x = 1f - viewportPoint.x;
            viewportPoint.y = 1f - viewportPoint.y;
        }

        Vector2 dirFromCenter = new Vector2(viewportPoint.x - 0.5f, viewportPoint.y - 0.5f);
        if (dirFromCenter.sqrMagnitude < 0.0001f)
            dirFromCenter = Vector2.up;

        arrow.anchoredPosition = ClampToRectEdge(dirFromCenter, halfSize);

        // +90 (rather than -90) because the authored arrow sprite points down (-Y) by default.
        float angle = Mathf.Atan2(dirFromCenter.y, dirFromCenter.x) * Mathf.Rad2Deg + 90f;
        arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>Projects a direction from the container's center out to the edge of a rectangle of the given half-size.</summary>
    private static Vector2 ClampToRectEdge(Vector2 dir, Vector2 halfSize)
    {
        float tx = Mathf.Abs(dir.x) > 0.0001f ? halfSize.x / Mathf.Abs(dir.x) : float.PositiveInfinity;
        float ty = Mathf.Abs(dir.y) > 0.0001f ? halfSize.y / Mathf.Abs(dir.y) : float.PositiveInfinity;
        float t = Mathf.Min(tx, ty);
        return dir * t;
    }

    // ── Pooling ──────────────────────────────────────────────────────────────

    private void ReleaseStaleArrows(IReadOnlyCollection<TutorialMarker> wanted)
    {
        _staleMarkers.Clear();
        foreach (TutorialMarker tracked in _activeArrows.Keys)
        {
            bool stillWanted = tracked != null && tracked.gameObject.activeInHierarchy && Contains(wanted, tracked);
            if (!stillWanted)
                _staleMarkers.Add(tracked);
        }

        foreach (TutorialMarker stale in _staleMarkers)
            ReleaseArrow(stale);
    }

    private static bool Contains(IReadOnlyCollection<TutorialMarker> collection, TutorialMarker marker)
    {
        foreach (TutorialMarker m in collection)
            if (m == marker) return true;
        return false;
    }

    private RectTransform GetFromPool()
    {
        if (_pool.Count > 0)
        {
            RectTransform arrow = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            arrow.gameObject.SetActive(true);
            return arrow;
        }

        RectTransform clone = Instantiate(arrowTemplate, indicatorContainer);
        clone.gameObject.SetActive(true);
        return clone;
    }

    private void ReleaseArrow(TutorialMarker marker)
    {
        if (!_activeArrows.TryGetValue(marker, out RectTransform arrow)) return;

        _activeArrows.Remove(marker);
        if (arrow != null)
        {
            arrow.gameObject.SetActive(false);
            _pool.Add(arrow);
        }
    }

    private void ReleaseAll()
    {
        foreach (KeyValuePair<TutorialMarker, RectTransform> kvp in _activeArrows)
        {
            if (kvp.Value != null)
            {
                kvp.Value.gameObject.SetActive(false);
                _pool.Add(kvp.Value);
            }
        }
        _activeArrows.Clear();
    }
}
