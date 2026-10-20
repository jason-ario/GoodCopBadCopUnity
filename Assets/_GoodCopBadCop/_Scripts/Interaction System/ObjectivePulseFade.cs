using UnityEngine;

/// <summary>
/// Shared camera-distance curve for the objective pulse (junk, broken fences, graffiti), so up close
/// the pulse fades away and the object itself can be seen. Graffiti mirrors these numbers in
/// GraffitiScrubDecal.shader (_PulseNearHide / _PulseNearFade) — keep the material values in sync.
/// </summary>
public static class ObjectivePulseFade
{
    /// <summary>Within this distance (m) the pulse is fully gone.</summary>
    public const float DefaultNearHide = 5f;

    /// <summary>Distance (m) beyond <see cref="DefaultNearHide"/> over which the pulse fades back to full.</summary>
    public const float DefaultNearFadeWidth = 3f;

    /// <summary>
    /// 0 at or inside <paramref name="nearHide"/>, smoothstep up to 1 at
    /// <paramref name="nearHide"/> + <paramref name="fadeWidth"/>. <paramref name="nearHide"/> &lt;= 0 disables it.
    /// </summary>
    public static float Near(float distance, float nearHide, float fadeWidth)
    {
        if (nearHide <= 0f) return 1f;
        float t = Mathf.Clamp01((distance - nearHide) / Mathf.Max(0.01f, fadeWidth));
        return t * t * (3f - 2f * t);
    }
}
