using HighlightPlus;
using UnityEngine;

/// <summary>
/// Local (per-client) tutorial call-out for a world object: an optional pooled
/// <see cref="TutorialMarker"/> arrow above the object plus an optional persistent outline
/// (the object's authored <see cref="HighlightEffect"/>). Toggled by gameplay code through
/// <see cref="SetActive"/> — e.g. <see cref="SortMailTask.ShowSortingTutorialCallouts"/>.
///
/// Highlight routing (keeps the "Tutorial arrow ⇒ highlight" rule):
/// <list type="bullet">
/// <item>On an <see cref="Interactable"/> with the arrow on, the arrow owns the glow via
/// <see cref="HighlightHold.TutorialArrow"/> (the runtime anchor is a child, so the pooled marker
/// resolves the Interactable on its parent).</item>
/// <item>On an <see cref="Interactable"/> with the arrow off, it claims <see cref="HighlightHold.Tutorial"/>.</item>
/// <item>On a plain object (no Interactable — e.g. the Mail Cubbies stands, Prohibited Goods Sign)
/// it drives the <see cref="HighlightEffect"/> itself with the same range gate, fade and opacity
/// pulse as Interactable holds.</item>
/// </list>
/// The arrow points at a runtime "Tutorial Arrow Anchor" child placed at the top-centre of the
/// object's active renderers, so targets that contain child Interactables (the cubby stands' locker
/// doors) never get one random child highlighted by the pooled marker.
/// </summary>
[DisallowMultipleComponent]
public class TutorialCallout : MonoBehaviour
{
    [Header("Arrow")]
    [Tooltip("Show a pooled tutorial arrow above this object while the call-out is active.")]
    [SerializeField] private bool _showArrow = true;
    [Tooltip("Arrow height above the top of this object's combined renderer bounds.")]
    [SerializeField, Min(0f)] private float _arrowHoverHeight = 0.5f;

    [Header("Highlight")]
    [Tooltip("Outline this object while the call-out is active.")]
    [SerializeField] private bool _highlight = true;
    [Tooltip("Outline used for plain (non-Interactable) objects. Falls back to GetComponent<HighlightEffect>().")]
    [SerializeField] private HighlightEffect _highlightEffect;

    [Header("Plain-Object Highlight (ignored on Interactables)")]
    [Tooltip("Outline is only visible while the camera is within this many meters. 0 = unlimited.")]
    [SerializeField, Min(0f)] private float _visibleRange = 50f;
    [Tooltip("Seconds to fade the outline in / out when crossing the visible range.")]
    [SerializeField, Min(0f)] private float _rangeFadeDuration = 0.5f;
    [Tooltip("Full pulse cycles per second. 0 = steady.")]
    [SerializeField, Min(0f)] private float _pulseSpeed = 0.6f;
    [Tooltip("Outline opacity at the faintest point of the pulse.")]
    [SerializeField, Range(0.05f, 1f)] private float _pulseMinOpacity = 0.4f;

    private const float RangeHysteresis = 1f;

    private Interactable _interactable;
    private Transform _arrowAnchor;
    private bool _active;
    private bool _inRange;
    private bool _fadeCached;
    private float _authoredFadeIn;
    private float _authoredFadeOut;

    /// <summary>True while the call-out is shown.</summary>
    public bool IsActive => _active;

    private bool DrivesEffectDirectly => _highlight && _interactable == null && _highlightEffect != null;

    private void Awake()
    {
        _interactable = GetComponent<Interactable>();
        if (_highlightEffect == null) _highlightEffect = GetComponent<HighlightEffect>();
    }

    private void OnDisable() => SetActive(false);

    /// <summary>Shows or hides the arrow and outline. Idempotent.</summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;

        if (_showArrow)
        {
            if (active) ShowArrow();
            else HideArrow();
        }

        if (!_highlight) return;

        if (_interactable != null)
        {
            // With the arrow on, the arrow's TutorialArrow hold already lights this Interactable.
            if (!_showArrow) _interactable.SetForceHighlight(active, HighlightHold.Tutorial);
            return;
        }

        if (_highlightEffect == null) return;

        if (active)
        {
            _inRange = IsCameraInRange(false);
            ApplyEffect(_inRange, false);
        }
        else
        {
            _highlightEffect.outlineOpacity = 1f;
            ApplyEffect(false, false);
        }
    }

    private void LateUpdate()
    {
        if (!_active || !DrivesEffectDirectly) return;

        bool inRange = IsCameraInRange(_inRange);
        if (inRange != _inRange)
        {
            _inRange = inRange;
            ApplyEffect(inRange, true);
        }

        if (_pulseSpeed > 0f)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * _pulseSpeed * Mathf.PI * 2f);
            _highlightEffect.outlineOpacity = Mathf.Lerp(_pulseMinOpacity, 1f, wave);
        }
    }

    // ── Arrow ────────────────────────────────────────────────────────────────

    private void ShowArrow()
    {
        TutorialMarkerManager manager = TutorialMarkerManager.Instance;
        if (manager == null) return;

        if (_arrowAnchor == null)
        {
            _arrowAnchor = new GameObject("Tutorial Arrow Anchor").transform;
            _arrowAnchor.SetParent(transform, false);
        }

        _arrowAnchor.position = ComputeTopCenter();
        manager.Mark(_arrowAnchor, _arrowHoverHeight);
    }

    private void HideArrow()
    {
        if (_arrowAnchor != null)
            TutorialMarkerManager.Instance?.Unmark(_arrowAnchor);
    }

    private Vector3 ComputeTopCenter()
    {
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return hasBounds
            ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
            : transform.position;
    }

    // ── Plain-object highlight ───────────────────────────────────────────────

    private bool IsCameraInRange(bool currentlyInRange)
    {
        if (_visibleRange <= 0f) return true;

        Camera cam = Camera.main;
        if (cam == null) return true;

        float range = _visibleRange + (currentlyInRange ? RangeHysteresis : 0f);
        return (cam.transform.position - transform.position).sqrMagnitude <= range * range;
    }

    private void ApplyEffect(bool on, bool rangeFade)
    {
        if (!_fadeCached)
        {
            _authoredFadeIn = _highlightEffect.fadeInDuration;
            _authoredFadeOut = _highlightEffect.fadeOutDuration;
            _fadeCached = true;
        }

        _highlightEffect.enabled = true;
        _highlightEffect.fadeInDuration = rangeFade ? _rangeFadeDuration : _authoredFadeIn;
        _highlightEffect.fadeOutDuration = rangeFade ? _rangeFadeDuration : _authoredFadeOut;
        _highlightEffect.highlighted = on;
    }
}
