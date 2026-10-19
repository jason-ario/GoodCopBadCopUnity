using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes every active NavMeshSurface in the loaded scenes with terrain tree trunks carved out.
///
/// <see cref="TerrainTreeColliderStreamer"/> turns off the TerrainCollider's "Enable Tree Colliders"
/// and streams proxy colliders at runtime instead. The bake then collects the terrain as a single
/// heightfield source with no trees, so a plain "Bake" button press bakes straight through every
/// trunk and agents path into trees. (Re-enabling the flag just for the bake doesn't help; the
/// collector still ignores the trees.) This tool spawns temporary proxy colliders for every tree
/// inside the surfaces' volumes, starts the bake (sources are collected synchronously inside
/// NavMeshSurface.UpdateNavMesh; only the voxel build is async), then destroys them.
/// </summary>
public static class NavMeshBakeWithTerrainTrees
{
    [MenuItem("Tools/NavMesh/Bake All Surfaces (With Terrain Trees)")]
    public static void BakeAll()
    {
        var surfaces = new List<Object>();
        Bounds bakeBounds = default;
        foreach (NavMeshSurface surface in NavMeshSurface.activeSurfaces)
        {
            if (surface == null || EditorUtility.IsPersistent(surface))
                continue;

            Bounds worldBounds = GetWorldBounds(surface);
            if (surfaces.Count == 0) bakeBounds = worldBounds;
            else bakeBounds.Encapsulate(worldBounds);
            surfaces.Add(surface);
        }

        if (surfaces.Count == 0)
        {
            Debug.LogWarning("[NavMeshBakeWithTerrainTrees] No active NavMeshSurface found in the loaded scenes.");
            return;
        }

        var tempRoots = new List<GameObject>();
        int treeColliders = 0;
        try
        {
            foreach (TerrainTreeColliderStreamer streamer in
                     Object.FindObjectsByType<TerrainTreeColliderStreamer>(FindObjectsInactive.Exclude))
            {
                GameObject root = streamer.CreateBakeColliders(bakeBounds);
                if (root == null) continue;
                tempRoots.Add(root);
                treeColliders += root.transform.childCount;
            }

            Physics.SyncTransforms();
            NavMeshAssetManager.instance.StartBakingSurfaces(surfaces.ToArray());
        }
        finally
        {
            foreach (GameObject root in tempRoots)
            {
                if (root != null) Object.DestroyImmediate(root);
            }
        }

        Debug.Log($"[NavMeshBakeWithTerrainTrees] Baking {surfaces.Count} surface(s) with {treeColliders} " +
                  "temporary tree colliders. Save the scene once baking finishes.");
    }

    /// <summary>
    /// World-space bounds the surface collects from. Volume surfaces use their box; others fall back
    /// to infinite bounds so every tree is included.
    /// </summary>
    private static Bounds GetWorldBounds(NavMeshSurface surface)
    {
        if (surface.collectObjects != CollectObjects.Volume)
            return new Bounds(Vector3.zero, Vector3.one * float.MaxValue);

        Matrix4x4 m = Matrix4x4.TRS(surface.transform.position, surface.transform.rotation, Vector3.one);
        Vector3 c = surface.center;
        Vector3 e = surface.size * 0.5f;
        var bounds = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            bounds.Encapsulate(m.MultiplyPoint3x4(c + corner));
        }
        return bounds;
    }
}
