using UnityEngine;

/// <summary>
/// Marks a renderer as an internal part (bone or organ) that only the X-ray machine draws.
/// Put it on the renderer's GameObject and set that object to the XRaySkeleton layer so normal
/// cameras never see it. <see cref="XRayCamera"/> collects these from nearby suspects and
/// <see cref="XRayBodyFeature"/> draws them with the current scan mode's skeleton / organ material.
/// Organs are tinted with <see cref="Color"/>.
/// Used by <see cref="XRayPlaceholderInternals"/>; authored bones / organs can use it directly.
/// </summary>
[DisallowMultipleComponent]
public class XRayInternalPart : MonoBehaviour
{
    public enum PartKind { Skeleton, Organ }

    [SerializeField] private PartKind _kind = PartKind.Organ;

    [Tooltip("Organ tint in Organ mode (ignored for skeleton parts).")]
    [SerializeField] private Color _color = Color.red;

    public PartKind Kind => _kind;
    public Color Color => _color;

    public void Configure(PartKind kind, Color color)
    {
        _kind = kind;
        _color = color;
    }
}
