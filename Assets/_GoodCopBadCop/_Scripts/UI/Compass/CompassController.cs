using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Elder Scrolls-style horizontal compass strip anchored to the bottom of the HUD.
/// Shows N/E/S/W cardinal labels that slide left/right as the player's camera turns and
/// disappear once they rotate outside <see cref="fieldOfViewDegrees"/>, plus a pooled, colored
/// icon for every world-space target currently marked by <see cref="TutorialMarkerManager"/>
/// (hand-scripted tutorial call-outs) or registered in <see cref="CompassMarkerRegistry"/>
/// (junk, graffiti, broken fences, blood — see <see cref="CompassMarkerCategory"/>), positioned
/// by real-world bearing relative to the player.
/// </summary>
public class CompassController : MonoBehaviour
{
    /// <summary>Icon tint per marker category. Same icon shape for every category for now — see project audit.</summary>
    private static readonly Dictionary<CompassMarkerCategory, Color> CategoryColors = new()
    {
        { CompassMarkerCategory.Tutorial, new Color(0.98f, 0.82f, 0.15f) }, // amber
        { CompassMarkerCategory.Junk,     new Color(0.80f, 0.62f, 0.35f) }, // tan/brown
        { CompassMarkerCategory.Graffiti, new Color(0.95f, 0.40f, 0.10f) }, // orange
        { CompassMarkerCategory.Fence,    new Color(0.60f, 0.65f, 0.70f) }, // steel gray
        { CompassMarkerCategory.Blood,    new Color(0.75f, 0.05f, 0.05f) }, // dark red
    };

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

    /// <summary>Reused every frame to avoid allocating a fresh collection per target while iterating two sources.</summary>
    private readonly Dictionary<Transform, CompassMarkerCategory> _wantedTargets = new();

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
        if (iconTemplate == null || iconContainer == null)
        {
            ReleaseAllIcons();
            return;
        }

        // Gather this frame's full target set from both sources. Tutorial call-outs are added
        // first and take priority if a target were somehow tracked by both.
        _wantedTargets.Clear();

        TutorialMarkerManager tutorialMgr = TutorialMarkerManager.Instance;
        if (tutorialMgr != null)
        {
            foreach (Transform target in tutorialMgr.GetMarkedTargets())
            {
                if (target != null)
                    _wantedTargets[target] = CompassMarkerCategory.Tutorial;
            }
        }

        foreach (KeyValuePair<Transform, CompassMarkerCategory> kvp in CompassMarkerRegistry.Markers)
        {
            if (kvp.Key != null && !_wantedTargets.ContainsKey(kvp.Key) && IsCategoryTaskActive(kvp.Value))
                _wantedTargets[kvp.Key] = kvp.Value;
        }

        // Release icons for targets that are no longer wanted (task completed, marker removed, etc.).
        _staleTargets.Clear();
        foreach (Transform tracked in _activeIcons.Keys)
        {
            if (tracked == null || !_wantedTargets.ContainsKey(tracked))
                _staleTargets.Add(tracked);
        }
        foreach (Transform stale in _staleTargets)
            ReleaseIcon(stale);

        Vector3 playerPos = cam.transform.position;

        foreach (KeyValuePair<Transform, CompassMarkerCategory> kvp in _wantedTargets)
        {
            Transform target = kvp.Key;

            if (!_activeIcons.TryGetValue(target, out RectTransform icon))
            {
                icon = GetIconFromPool();
                _activeIcons[target] = icon;
            }

            ApplyCategoryColor(icon, kvp.Value);

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

    private static void ApplyCategoryColor(RectTransform icon, CompassMarkerCategory category)
    {
        if (icon == null) return;

        Image image = icon.GetComponent<Image>();
        if (image == null) return;

        image.color = CategoryColors.TryGetValue(category, out Color color) ? color : Color.white;
    }

    /// <summary>
    /// Whether the daily task that owns <paramref name="category"/> is currently active. A marker
    /// is only ever registered in <see cref="CompassMarkerRegistry"/> because the underlying item
    /// is in some raw state (collectible/unscrubbed/broken) — this is the second gate that keeps
    /// the compass showing only markers tied to a task the player is actually being asked to do
    /// right now, e.g. a broken fence between a mutant breach and <see cref="FenceRepairTask"/>
    /// actually being triggered must not show. <see cref="CompassMarkerCategory.Tutorial"/> never
    /// reaches this — it comes from <see cref="TutorialMarkerManager"/>, not the registry.
    /// </summary>
    private static bool IsCategoryTaskActive(CompassMarkerCategory category)
    {
        switch (category)
        {
            case CompassMarkerCategory.Junk:
                return (TakeOutTrashTask.Instance != null && TakeOutTrashTask.Instance.IsActive)
                    || (CleanBoothMessTask.Instance != null && CleanBoothMessTask.Instance.IsActive);
            case CompassMarkerCategory.Graffiti:
                return CleanGraffitiTask.Instance != null && CleanGraffitiTask.Instance.IsActive;
            case CompassMarkerCategory.Fence:
                return FenceRepairTask.Instance != null && FenceRepairTask.Instance.IsActive;
            case CompassMarkerCategory.Blood:
                return CleanBloodTask.Instance != null && CleanBloodTask.Instance.IsActive;
            default:
                return true;
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
        // A destroyed Unity Object compares equal to null but is still the dictionary key that
        // owns a pooled icon. Do not early-out on Unity's overloaded null check here, otherwise
        // a remote player's despawn can leave that icon visible forever.
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
