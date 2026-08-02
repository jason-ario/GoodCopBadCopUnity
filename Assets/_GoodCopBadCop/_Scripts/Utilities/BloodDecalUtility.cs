using UnityEngine;

/// <summary>
/// Shared helpers for placing blood-splatter decals on the ground. Decals use Unity's built-in
/// Plane primitive, whose local up (+Y) axis is the surface normal at identity rotation (it
/// already lies flat on the ground, face-up, with no rotation applied) — <see
/// cref="GetGroundDecalRotation"/> aligns that local up axis with the surface normal and applies
/// a random spin around it so repeated decals don't look identical.
/// </summary>
public static class BloodDecalUtility
{
    /// <summary>
    /// Builds a rotation that aligns the decal's local up axis with the ground surface normal
    /// described by <paramref name="groundNormal"/>, with a random spin around that axis.
    /// </summary>
    public static Quaternion GetGroundDecalRotation(Vector3 groundNormal)
    {
        Vector3 normal = groundNormal.sqrMagnitude > 0.0001f ? groundNormal.normalized : Vector3.up;

        Quaternion alignToNormal = Quaternion.FromToRotation(Vector3.up, normal);
        Quaternion spin = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
        return alignToNormal * spin;
    }
}
