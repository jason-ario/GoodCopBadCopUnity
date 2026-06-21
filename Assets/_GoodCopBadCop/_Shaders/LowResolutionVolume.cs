using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Volume override that controls the low-resolution pixelation effect.
/// Add this to any Volume in the scene and raise PixelSize above 1 to activate.
/// </summary>
[VolumeComponentMenu("Post-processing/Low Resolution")]
public class LowResolutionVolume : VolumeComponent, IPostProcessComponent
{
    /// <summary>Size of each pixel block in screen pixels. 1 = native, 2 = half-res, 4 = quarter-res.</summary>
    [Tooltip("Size of each pixel block in screen pixels. 1 = native resolution, 2 = half-res, 4 = quarter-res, etc.")]
    public ClampedIntParameter pixelSize = new ClampedIntParameter(1, 1, 64);

    /// <inheritdoc/>
    public bool IsActive() => pixelSize.value > 1;

    /// <inheritdoc/>
    public bool IsTileCompatible() => false;
}
