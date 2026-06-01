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

    private void OnEnable()
    {
        if (!ActiveLights.Contains(this))
            ActiveLights.Add(this);
    }

    private void OnDisable()
    {
        ActiveLights.Remove(this);
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
