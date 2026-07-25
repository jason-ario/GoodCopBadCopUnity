using UnityEngine;

/// <summary>
/// Shared helpers for placing blood-splatter decals on the ground. Decals are expected to
/// be flat quads/planes whose local forward (+Z) axis is the "into the surface" direction —
/// <see cref="GetGroundDecalRotation"/> rotates that forward axis to point down into the
/// ground (i.e. opposite the surface normal) and applies a random spin around it so
/// repeated decals don't look identical.
/// </summary>
public static class BloodDecalUtility
{
    /// <summary>
    /// Builds a rotation that points the decal's forward axis down into the ground surface
    /// described by <paramref name="groundNormal"/>, with a random rotation around that axis.
    /// </summary>
    public static Quaternion GetGroundDecalRotation(Vector3 groundNormal)
    {
        Vector3 normal = groundNormal.sqrMagnitude > 0.0001f ? groundNormal.normalized : Vector3.up;
        Vector3 decalForward = -normal;

        // Vector3.up can't be used as the "up" hint when it's parallel/anti-parallel to
        // decalForward (the common flat-ground case), so fall back to Vector3.forward.
        Vector3 upHint = Mathf.Abs(Vector3.Dot(decalForward, Vector3.up)) > 0.999f
            ? Vector3.forward
            : Vector3.up;

        Quaternion baseRotation = Quaternion.LookRotation(decalForward, upHint);
        return Quaternion.AngleAxis(Random.Range(0f, 360f), decalForward) * baseRotation;
    }
}
