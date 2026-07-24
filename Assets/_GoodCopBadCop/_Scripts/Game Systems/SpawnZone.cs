using UnityEngine;

/// <summary>
/// A rectangular horizontal zone in which objects (e.g. trash bags) can be spawned.
/// Attach this to a GameObject to define a spawning area in the scene.
/// </summary>
public class SpawnZone : MonoBehaviour
{
    [Tooltip("Half-extents on X and Z. Y is ignored for horizontal placement.")]
    public Vector3 HalfExtents = new Vector3(5f, 0.1f, 5f);

    /// <summary>
    /// Returns a random world position within this zone.
    /// </summary>
    public Vector3 GetRandomPosition()
    {
        float randomX = Random.Range(-HalfExtents.x, HalfExtents.x);
        float randomZ = Random.Range(-HalfExtents.z, HalfExtents.z);
        return transform.position + new Vector3(randomX, 0f, randomZ);
    }

    /// <summary>
    /// Returns true when <paramref name="worldPosition"/> falls within this zone's horizontal
    /// bounds (X/Z half-extents around the zone's position). Height (Y) is ignored, matching
    /// <see cref="GetRandomPosition"/>'s horizontal-only placement.
    /// </summary>
    public bool Contains(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - transform.position;
        return Mathf.Abs(local.x) <= HalfExtents.x && Mathf.Abs(local.z) <= HalfExtents.z;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        DrawGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(true);
    }

    private void DrawGizmo(bool selected)
    {
        // Use a consistent color for spawn zones.
        Color color = Color.cyan;
        color.a = selected ? 0.4f : 0.15f;

        Gizmos.color = color;
        Vector3 size = new Vector3(HalfExtents.x * 2f, Mathf.Max(HalfExtents.y * 2f, 0.1f), HalfExtents.z * 2f);
        
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, size);

        color.a = selected ? 1f : 0.5f;
        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.zero, size);

        // Reset matrix to avoid affecting other gizmos.
        Gizmos.matrix = Matrix4x4.identity;

        if (selected)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, name);
        }
    }
#endif
}
