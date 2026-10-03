using TMPro;
using UnityEngine;

/// <summary>
/// A physical section tab sticking out of a guidebook sheet. Spawned, positioned and bound by
/// <see cref="GuidebookBuilder"/> — one per visible section (Rules + each unlocked category).
///
/// Clicking flips the book (animated) so the section's opening spread is visible.
/// Clicks arrive through the existing <see cref="IClickable"/> raycast path (<c>ClickDetector</c>).
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

    private GuidebookPageController _controller;
    private int _targetLeftCount;
    private MaterialPropertyBlock _block;

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

    public void OnClick()
    {
        if (_controller == null) return;
        _controller.FlipTo(_targetLeftCount);
    }
}
