using UnityEngine;

/// <summary>
/// Shared helpers for placing blood-splatter decals on the ground. Decal prefabs (e.g. "Random
/// Blood Splatter Variant") are authored so their local forward (+Z) axis is the face normal —
/// <see cref="GetGroundDecalRotation"/> aligns that local forward axis with the surface normal
/// and applies a random spin around it so repeated decals don't look identical.
/// </summary>
public static class BloodDecalUtility
{
    /// <summary>
    /// Builds a rotation that aligns the decal's local forward axis with the ground surface
    /// normal described by <paramref name="groundNormal"/>, with a random spin around that axis.
    /// </summary>
    public static Quaternion GetGroundDecalRotation(Vector3 groundNormal)
    {
        Vector3 normal = groundNormal.sqrMagnitude > 0.0001f ? groundNormal.normalized : Vector3.up;

        Quaternion alignToNormal = Quaternion.FromToRotation(Vector3.forward, normal);
        Quaternion spin = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
        return alignToNormal * spin;
    }

    /// <summary>
    /// Spawns <paramref name="particlePrefab"/> at <paramref name="position"/> in world space
    /// with <paramref name="rotation"/> (typically the same rotation returned by
    /// <see cref="GetGroundDecalRotation"/> for the blood-splatter decal it accompanies, so the
    /// particle's forward axis is aligned with the same ground normal). Purely cosmetic/local —
    /// not a NetworkObject. Automatically destroyed after <paramref name="lifetime"/> seconds
    /// (0 = never). No-op when <paramref name="particlePrefab"/> is null.
    /// </summary>
    public static GameObject SpawnAlignedParticle(GameObject particlePrefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (particlePrefab == null)
            return null;

        GameObject fx = Object.Instantiate(particlePrefab, position, rotation);

        if (lifetime > 0f)
            Object.Destroy(fx, lifetime);

        return fx;
    }
}
