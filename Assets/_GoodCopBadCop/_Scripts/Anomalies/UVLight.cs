using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a GameObject as an active UV light source that projects a cone-shaped reveal volume.
/// Registers itself into a shared static list on OnEnable and removes itself on OnDisable,
/// so the flashlight script only needs to enable/disable this component to participate
/// in the reveal effect — no direct references are required.
///
/// The cone is defined by a full cone angle (degrees) and a range (world-space distance).
/// The light direction is always transform.forward.
/// </summary>
[ExecuteAlways]
public class UVLight : MonoBehaviour
{
    /// <summary>All currently active UV light sources across the scene.</summary>
    public static readonly List<UVLight> ActiveLights = new();

    [Tooltip("Full cone angle in degrees (the total spread, not half-angle).")]
    [SerializeField] private float coneAngleDegrees = 30f;

    [Tooltip("How far the cone extends in world units.")]
    [SerializeField] private float range = 3f;

    /// <summary>World-space position of this UV light this frame.</summary>
    public Vector3 Position => transform.position;

    /// <summary>World-space normalized forward direction of the cone.</summary>
    public Vector3 Direction => transform.forward;

    /// <summary>Half of the full cone angle, in degrees.</summary>
    public float ConeHalfAngleDeg => coneAngleDegrees * 0.5f;

    /// <summary>Maximum world-space reach of the cone along its forward axis.</summary>
    public float Range => range;

    /// <summary>
    /// Backward-compatible alias for Range. Used by BlueVeinsAnomaly which still operates
    /// on a sphere model. Returns the cone's range as the effective sphere radius.
    /// </summary>
    public float Radius => range;

    // ── Global shader data (GraffitiScrubDecal UV glow) ─────────────────────────
    // Pushed as GLOBAL properties with distinct names from the per-renderer _UVLight* arrays
    // (UVReveal / Character Shader rely on per-renderer MaterialPropertyBlocks and must not be
    // affected). Any shader can read these without a per-object driver component — used so
    // blood splatter decals glow under UV light while GraffitiInteractable owns their MPB.
    private const int MaxGlobalLights = 4; // Must match UV_GLOW_MAX_LIGHTS in GraffitiScrubDecal.shader.

    private static readonly int GlowPositionsId  = Shader.PropertyToID("_UVGlowLightPositions");
    private static readonly int GlowDirectionsId = Shader.PropertyToID("_UVGlowLightDirections");
    private static readonly int GlowParamsId     = Shader.PropertyToID("_UVGlowLightParams");
    private static readonly int GlowCountId      = Shader.PropertyToID("_UVGlowLightCount");

    private static readonly Vector4[] s_globalPositions  = new Vector4[MaxGlobalLights];
    private static readonly Vector4[] s_globalDirections = new Vector4[MaxGlobalLights];
    private static readonly Vector4[] s_globalParams     = new Vector4[MaxGlobalLights];
    private static int s_lastPushFrame = -1;

    private void OnEnable()
    {
        if (!ActiveLights.Contains(this))
            ActiveLights.Add(this);
        PushShaderGlobals();
    }

    private void OnDisable()
    {
        ActiveLights.Remove(this);
        // Push immediately so the glow switches off even when no other light is left to update.
        PushShaderGlobals();
    }

    private void LateUpdate()
    {
        // Every active light runs this; only the first one per frame does the work.
        if (s_lastPushFrame == Time.frameCount) return;
        s_lastPushFrame = Time.frameCount;
        PushShaderGlobals();
    }

    /// <summary>
    /// Writes all active UV lights (capped at <see cref="MaxGlobalLights"/>) into the global
    /// _UVGlowLight* shader arrays consumed by GraffitiScrubDecal's UV glow.
    /// </summary>
    public static void PushShaderGlobals()
    {
        int count = 0;
        for (int i = 0; i < ActiveLights.Count && count < MaxGlobalLights; i++)
        {
            UVLight light = ActiveLights[i];
            if (light == null) continue;

            Vector3 p = light.Position;
            Vector3 d = light.Direction;
            s_globalPositions[count]  = new Vector4(p.x, p.y, p.z, light.Range);
            s_globalDirections[count] = new Vector4(d.x, d.y, d.z, 0f);
            s_globalParams[count]     = new Vector4(Mathf.Cos(light.ConeHalfAngleDeg * Mathf.Deg2Rad), 0f, 0f, 0f);
            count++;
        }

        for (int i = count; i < MaxGlobalLights; i++)
        {
            s_globalPositions[i]  = Vector4.zero;
            s_globalDirections[i] = Vector4.zero;
            s_globalParams[i]     = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(GlowPositionsId,  s_globalPositions);
        Shader.SetGlobalVectorArray(GlowDirectionsId, s_globalDirections);
        Shader.SetGlobalVectorArray(GlowParamsId,     s_globalParams);
        Shader.SetGlobalFloat(GlowCountId, count);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.4f, 1f, 0.5f);
        DrawConeGizmo();
    }

    /// <summary>Draws a cone wireframe using the light's current transform and settings.</summary>
    private void DrawConeGizmo()
    {
        Vector3 origin    = transform.position;
        Vector3 forward   = transform.forward;
        Vector3 right     = transform.right;
        Vector3 up        = transform.up;
        float   halfRad   = ConeHalfAngleDeg * Mathf.Deg2Rad;
        float   tipRadius = range * Mathf.Tan(halfRad);
        Vector3 tip       = origin + forward * range;

        // Four edge lines from the apex to the rim.
        const int EdgeCount = 8;
        for (int i = 0; i < EdgeCount; i++)
        {
            float angle    = i * (Mathf.PI * 2f / EdgeCount);
            Vector3 rimPt  = tip + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * tipRadius;
            Gizmos.DrawLine(origin, rimPt);
        }

        // Circle at the rim.
        const int CircleSegments = 32;
        for (int s = 0; s < CircleSegments; s++)
        {
            float a0 = s       * (Mathf.PI * 2f / CircleSegments);
            float a1 = (s + 1) * (Mathf.PI * 2f / CircleSegments);
            Vector3 p0 = tip + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * tipRadius;
            Vector3 p1 = tip + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * tipRadius;
            Gizmos.DrawLine(p0, p1);
        }
    }
#endif
}
