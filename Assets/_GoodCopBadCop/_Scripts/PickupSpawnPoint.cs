using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Marks a world position as a candidate spawn location for <see cref="DailyPickupSpawnManager"/>.
/// Each spawn point defines its own pool of networked prefabs; the manager picks one at random
/// when this point is selected for the day.
/// </summary>
public class PickupSpawnPoint : MonoBehaviour
{
    [Tooltip("Pool of networked prefabs that can spawn at this location. One is chosen at random each day.")]
    [SerializeField] private GameObject[] _spawnablePrefabs;

    /// <summary>
    /// Returns a random prefab from this point's pool, or null if the pool is empty.
    /// </summary>
    public GameObject GetRandomPrefab()
    {
        if (_spawnablePrefabs == null || _spawnablePrefabs.Length == 0)
            return null;

        return _spawnablePrefabs[Random.Range(0, _spawnablePrefabs.Length)];
    }

#if UNITY_EDITOR
    private static readonly Color GizmoColorFill     = new Color(1f, 0.85f, 0f, 0.30f);
    private static readonly Color GizmoColorWire     = new Color(1f, 0.85f, 0f, 0.95f);
    private static readonly Color GizmoColorSelected = new Color(0f, 0.90f, 1f, 0.40f);
    private static readonly Color GizmoWireSelected  = new Color(0f, 0.90f, 1f, 0.95f);

    // Sphere radius in world units. The Unity sphere primitive has diameter 1,
    // so the DrawMesh scale is (GizmoRadius * 2) on each axis.
    private const float GizmoRadius  = 0.6f;
    private const float StemHeight   = 1.8f;
    private const float StemWidth    = 0.08f;

    private static Mesh s_SphereMesh;
    private static Mesh s_CylinderMesh;

    /// <summary>Gets (and caches) a primitive mesh without leaving a GameObject in the scene.</summary>
    private static Mesh GetPrimitiveMesh(PrimitiveType type)
    {
        var go   = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return mesh;
    }

    private static Mesh SphereMesh   => s_SphereMesh   ??= GetPrimitiveMesh(PrimitiveType.Sphere);
    private static Mesh CylinderMesh => s_CylinderMesh ??= GetPrimitiveMesh(PrimitiveType.Cylinder);

    private void OnDrawGizmos()
    {
        DrawSpawnPointMesh(GizmoColorFill, GizmoColorWire);

        // Label showing how many prefabs are configured.
        Vector3 labelPos = transform.position + Vector3.up * (StemHeight + GizmoRadius * 2f + 0.1f);
        int count = (_spawnablePrefabs != null) ? _spawnablePrefabs.Length : 0;
        string label = count > 0 ? $"Spawn Point ({count})" : "Spawn Point (empty)";
        Handles.Label(labelPos, label);
    }

    private void OnDrawGizmosSelected()
    {
        DrawSpawnPointMesh(GizmoColorSelected, GizmoWireSelected);

        if (_spawnablePrefabs == null || _spawnablePrefabs.Length == 0)
            return;

        // List each prefab name stacked above the sphere.
        var sb = new StringBuilder();
        for (int i = 0; i < _spawnablePrefabs.Length; i++)
        {
            string prefabName = _spawnablePrefabs[i] != null ? _spawnablePrefabs[i].name : "<null>";
            sb.AppendLine($"  [{i}] {prefabName}");
        }

        Vector3 labelPos = transform.position + Vector3.up * (StemHeight + GizmoRadius * 2f + 0.25f);
        Handles.Label(labelPos, sb.ToString());
    }

    /// <summary>
    /// Draws a solid cylinder stem topped by a solid sphere using mesh gizmos.
    /// </summary>
    private void DrawSpawnPointMesh(Color fillColor, Color wireColor)
    {
        // --- Stem (cylinder) ---
        // Unity's cylinder primitive is 2 units tall and 1 unit wide by default.
        Vector3 stemScale    = new Vector3(StemWidth, StemHeight * 0.5f, StemWidth);
        Vector3 stemPosition = transform.position + Vector3.up * (StemHeight * 0.5f);

        Gizmos.color = fillColor;
        Gizmos.DrawMesh(CylinderMesh, stemPosition, Quaternion.identity, stemScale);
        Gizmos.color = wireColor;
        Gizmos.DrawWireMesh(CylinderMesh, stemPosition, Quaternion.identity, stemScale);

        // --- Sphere at the top ---
        // Unity's sphere primitive has diameter 1, so scale = diameter = radius * 2.
        float sphereDiameter  = GizmoRadius * 2f;
        Vector3 sphereScale   = Vector3.one * sphereDiameter;
        Vector3 spherePosition = transform.position + Vector3.up * (StemHeight + GizmoRadius);

        Gizmos.color = fillColor;
        Gizmos.DrawMesh(SphereMesh, spherePosition, Quaternion.identity, sphereScale);
        Gizmos.color = wireColor;
        Gizmos.DrawWireMesh(SphereMesh, spherePosition, Quaternion.identity, sphereScale);
    }
#endif
}
