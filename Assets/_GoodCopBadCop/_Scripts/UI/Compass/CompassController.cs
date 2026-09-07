using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Elder Scrolls-style horizontal compass strip anchored to the bottom of the HUD.
/// Shows N/E/S/W cardinal labels that slide left/right as the player's camera turns and
/// disappear once they rotate outside <see cref="fieldOfViewDegrees"/>, plus a pooled icon
/// for every world-space target currently marked by <see cref="TutorialMarkerManager"/>
/// (tutorial/task objectives such as junk pickups), positioned by real-world bearing
/// relative to the player.
/// </summary>
public class CompassController : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Total horizontal field of view represented by the compass strip, in degrees. " +
             "Elements more than half this value away from the player's facing direction are hidden.")]
    [SerializeField] private float fieldOfViewDegrees = 180f;

    [Tooltip("Pixel width of the visible compass viewport. Should match the masked Viewport RectTransform's width.")]
    [SerializeField] private float viewportWidth = 600f;

    [Header("Cardinal Labels")]
    [SerializeField] private RectTransform northLabel;
    [SerializeField] private RectTransform eastLabel;
    [SerializeField] private RectTransform southLabel;
    [SerializeField] private RectTransform westLabel;

    [Header("Objective Icons")]
    [Tooltip("Inactive template cloned for every world target currently marked by TutorialMarkerManager. Must live under iconContainer.")]
    [SerializeField] private RectTransform iconTemplate;
    [SerializeField] private RectTransform iconContainer;
    [Tooltip("Vertical anchored position (px) objective icons sit at along the strip, above the baseline/cardinal labels.")]
    [SerializeField] private float iconYOffset = 18f;

    private readonly Dictionary<Transform, RectTransform> _activeIcons = new();
    private readonly List<RectTransform> _iconPool = new();
    private readonly List<Transform> _staleTargets = new();

    private void Awake()
    {
        if (iconTemplate != null)
            iconTemplate.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float playerYaw = cam.transform.eulerAngles.y;
        float halfFov = fieldOfViewDegrees * 0.5f;

        PositionCardinal(northLabel, 0f, playerYaw, halfFov);
        PositionCardinal(eastLabel, 90f, playerYaw, halfFov);
        PositionCardinal(southLabel, 180f, playerYaw, halfFov);
        PositionCardinal(westLabel, 270f, playerYaw, halfFov);

        UpdateObjectiveIcons(cam, playerYaw, halfFov);
    }

    // ── Cardinal labels ─────────────────────────────────────────────────

    private void PositionCardinal(RectTransform label, float headingDegrees, float playerYaw, float halfFov)
    {
        if (label == null) return;
        float diff = Mathf.DeltaAngle(playerYaw, headingDegrees);
        SetStripPosition(label, diff, halfFov);
    }

    /// <summary>
    /// Shows/hides and horizontally positions <paramref name="rt"/> along the compass strip
    /// based on the signed angular difference (<paramref name="diff"/>, degrees) between the
    /// player's facing direction and the element's bearing.
    /// </summary>
    private void SetStripPosition(RectTransform rt, float diff, float halfFov)
    {
        bool visible = Mathf.Abs(diff) <= halfFov;
        if (rt.gameObject.activeSelf != visible)
            rt.gameObject.SetActive(visible);

        if (!visible) return;

        Vector2 pos = rt.anchoredPosition;
        pos.x = (diff / halfFov) * (viewportWidth * 0.5f);
        rt.anchoredPosition = pos;
    }

    // ── Objective icons ─────────────────────────────────────────────────

    private void UpdateObjectiveIcons(Camera cam, float playerYaw, float halfFov)
    {
        TutorialMarkerManager mgr = TutorialMarkerManager.Instance;
        if (mgr == null || iconTemplate == null || iconContainer == null)
        {
            ReleaseAllIcons();
            return;
        }

        IReadOnlyCollection<Transform> targets = mgr.GetMarkedTargets();

        // Release icons for targets that are no longer marked (task completed, marker removed, etc.).
        _staleTargets.Clear();
        foreach (KeyValuePair<Transform, RectTransform> kvp in _activeIcons)
        {
            if (kvp.Key == null || !mgr.IsMarked(kvp.Key))
                _staleTargets.Add(kvp.Key);
        }
        foreach (Transform stale in _staleTargets)
            ReleaseIcon(stale);

        Vector3 playerPos = cam.transform.position;

        foreach (Transform target in targets)
        {
            if (target == null) continue;

            if (!_activeIcons.TryGetValue(target, out RectTransform icon))
            {
                icon = GetIconFromPool();
                _activeIcons[target] = icon;
            }

            Vector3 toTarget = target.position - playerPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                icon.gameObject.SetActive(false);
                continue;
            }

            float targetHeading = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(playerYaw, targetHeading);
            SetStripPosition(icon, diff, halfFov);

            if (icon.gameObject.activeSelf)
            {
                Vector2 pos = icon.anchoredPosition;
                pos.y = iconYOffset;
                icon.anchoredPosition = pos;
            }
        }
    }

    private RectTransform GetIconFromPool()
    {
        if (_iconPool.Count > 0)
        {
            RectTransform icon = _iconPool[_iconPool.Count - 1];
            _iconPool.RemoveAt(_iconPool.Count - 1);
            icon.gameObject.SetActive(true);
            return icon;
        }

        RectTransform clone = Instantiate(iconTemplate, iconContainer);
        clone.gameObject.SetActive(true);
        return clone;
    }

    private void ReleaseIcon(Transform target)
    {
        if (target == null) return;
        if (!_activeIcons.TryGetValue(target, out RectTransform icon)) return;

        _activeIcons.Remove(target);
        if (icon != null)
        {
            icon.gameObject.SetActive(false);
            _iconPool.Add(icon);
        }
    }

    private void ReleaseAllIcons()
    {
        foreach (KeyValuePair<Transform, RectTransform> kvp in _activeIcons)
        {
            if (kvp.Value != null)
            {
                kvp.Value.gameObject.SetActive(false);
                _iconPool.Add(kvp.Value);
            }
        }
        _activeIcons.Clear();
    }
}
