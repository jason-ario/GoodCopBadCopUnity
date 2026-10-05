using TMPro;
using UnityEngine;

/// <summary>
/// A physical section tab sticking out of a guidebook sheet. Spawned, positioned and bound by
/// <see cref="GuidebookBuilder"/> — one per visible section (Rules + each unlocked category).
///
/// Clicking flips the book (animated) so the section's opening spread is visible.
/// Clicks arrive through the existing <see cref="IClickable"/> raycast path (<c>ClickDetector</c>).
///
/// While the section holds unseen entries (<see cref="SetNew"/>) a "!" floats above the tab,
/// facing the camera and bobbing up and down.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GuidebookSectionTab : MonoBehaviour, IClickable
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    [SerializeField] private Renderer _renderer;

    [Tooltip("Labels printed on the tab — typically one readable from above (right stack) " +
             "and one readable from below (once its sheet is flipped onto the left stack).")]
    [SerializeField] private TMP_Text[] _labels;

    [Header("New Entries Badge")]
    [Tooltip("Optional \"!\" text. Left empty, one is created at runtime from the first label.")]
    [SerializeField] private TMP_Text _newBadge;
    [SerializeField] private Color    _newBadgeColor    = new Color(0.85f, 0.1f, 0.08f, 1f);
    [SerializeField] private float    _newBadgeFontSize = 1.2f;
    [Tooltip("Distance above the tab (along the camera's up axis), in tab-local units.")]
    [SerializeField] private float    _newBadgeHeight   = 0.2f;
    [Tooltip("Pulls the badge toward the camera so it never sinks into the paper.")]
    [SerializeField] private float    _newBadgeLift     = 0.03f;
    [SerializeField] private float    _bobAmplitude     = 0.025f;
    [Tooltip("Bobs per second.")]
    [SerializeField] private float    _bobFrequency     = 1.6f;

    private GuidebookPageController _controller;
    private int _targetLeftCount;
    private MaterialPropertyBlock _block;
    private bool _isNew;

    /// <summary>Number of sheets that must be on the left stack to show this section.</summary>
    public int TargetLeftCount => _targetLeftCount;

    public void Bind(GuidebookPageController controller, int targetLeftCount, string label, Color color)
    {
        _controller      = controller;
        _targetLeftCount = targetLeftCount;

        if (_labels != null)
            foreach (TMP_Text text in _labels)
                if (text != null) text.text = label;

        if (_renderer != null)
        {
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_block);
        }
    }

    /// <summary>Shows or hides the bobbing "!" that marks a section with unseen entries.</summary>
    public void SetNew(bool isNew)
    {
        _isNew = isNew;
        if (isNew) EnsureBadge();
        if (_newBadge != null)
        {
            _newBadge.gameObject.SetActive(isNew);
            if (isNew) UpdateBadgePose();
        }
    }

    public void OnClick()
    {
        if (_controller == null) return;
        _controller.FlipTo(_targetLeftCount);
    }

    private void LateUpdate()
    {
        if (_isNew && _newBadge != null) UpdateBadgePose();
    }

    private void EnsureBadge()
    {
        if (_newBadge != null) return;

        TMP_Text source = _labels != null && _labels.Length > 0 ? _labels[0] : null;
        if (source == null) return;

        TMP_Text badge = Instantiate(source, transform);
        badge.name = "New Badge";
        badge.text = "!";
        badge.enableAutoSizing = false;
        badge.fontSize = _newBadgeFontSize;
        badge.fontStyle = FontStyles.Bold;
        badge.alignment = TextAlignmentOptions.Center;
        badge.color = _newBadgeColor;
        badge.raycastTarget = false;
        // Rect scales with the glyph so a larger badge never clips or wraps.
        badge.rectTransform.sizeDelta = new Vector2(0.25f, 0.34f) * _newBadgeFontSize;
        badge.transform.localScale = Vector3.one;
        _newBadge = badge;
    }

    /// <summary>Floats the badge above the tab on screen, facing the camera, bobbing along its up axis.</summary>
    private void UpdateBadgePose()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Transform camT = cam.transform;
        float bob = Mathf.Sin(Time.unscaledTime * _bobFrequency * Mathf.PI * 2f) * _bobAmplitude;

        Vector3 up    = transform.InverseTransformDirection(camT.up);
        Vector3 toCam = transform.InverseTransformDirection(-camT.forward);

        Transform badge = _newBadge.transform;
        badge.localPosition = up * (_newBadgeHeight + bob) + toCam * _newBadgeLift;
        badge.rotation      = camT.rotation;
    }
}
