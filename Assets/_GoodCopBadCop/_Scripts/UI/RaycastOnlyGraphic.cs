using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Invisible UI Graphic that draws no geometry but still receives UI raycasts.
/// Use it as a hit area for pointer events without the overdraw cost of a transparent Image.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RaycastOnlyGraphic : Graphic
{
    public override void SetMaterialDirty() { }
    public override void SetVerticesDirty() { }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
    }
}
