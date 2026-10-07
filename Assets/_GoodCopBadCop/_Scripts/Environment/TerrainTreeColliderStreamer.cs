using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Distance-based "collider LOD" for terrain trees, local machine only. The
/// TerrainCollider's built-in "Enable Tree Colliders" (a collider for every tree
/// instance) is switched off in the editor via OnValidate; instead a pooled set of
/// proxy colliders is placed only on trees within <see cref="activationRadius"/> of
/// the local focus (<see cref="focusOverride"/>, or Camera.main).
///
/// Each proxy copies the collider found on the tree prototype prefab (Capsule,
/// Sphere, Box or Mesh), scaled/rotated per tree instance exactly like the terrain
/// does. Tree instances are bucketed once into a flat (CSR) spatial grid, so a
/// refresh only touches the handful of cells around the focus. Refreshes run on an
/// interval, are skipped while the focus hasn't moved, and allocate nothing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain))]
public class TerrainTreeColliderStreamer : MonoBehaviour
{
    [Header("Focus")]
    [Tooltip("Optional. Transform to stream colliders around. If unset, uses Camera.main.")]
    [SerializeField] private Transform focusOverride;

    [Header("Range")]
    [Tooltip("Trees whose trunk is within this horizontal distance of the focus get a collider.")]
    [SerializeField] private float activationRadius = 30f;

    [Tooltip("Extra distance before an active collider is released (prevents thrashing at the boundary).")]
    [SerializeField] private float deactivationPadding = 5f;

    [Header("Update")]
    [Tooltip("Seconds between refreshes.")]
    [SerializeField] private float updateInterval = 0.2f;

    [Tooltip("A refresh is skipped unless the focus moved at least this far since the last one.")]
    [SerializeField] private float moveThreshold = 1.5f;

    [Tooltip("Grid cell size (meters) used to bucket tree instances.")]
    [SerializeField] private float cellSize = 16f;

    [Tooltip("Proxies pre-created per tree prototype at startup, to avoid AddComponent hitches in play.")]
    [SerializeField] private int prewarmPerPrototype = 16;

    private struct TreeEntry
    {
        public Vector3 position;
        public float rotationDegrees;
        public float widthScale;
        public float heightScale;
        public int prototypeIndex;
    }

    private sealed class PrototypeTemplate
    {
        public Collider source;
        public Vector3 colliderLocalPosition;
        public Quaternion colliderLocalRotation;
        public Vector3 colliderLocalScale;
        public Vector3 rootScale;
        public int layer;
        public string tag;
        public string name;
        public readonly Stack<Transform> pool = new Stack<Transform>();
    }

    private Terrain _terrain;
    private Transform _container;
    private TreeEntry[] _trees = new TreeEntry[0];
    private PrototypeTemplate[] _templates = new PrototypeTemplate[0];

    private int _gridWidth;
    private int _gridHeight;
    private float _cellSize;
    private int[] _cellStart = new int[1];
    private int[] _cellTrees = new int[0];
    private Vector3 _terrainOrigin;

    private readonly Dictionary<int, Transform> _activeProxies = new Dictionary<int, Transform>();
    private readonly List<int> _toRelease = new List<int>();
    private Vector3 _lastFocusPosition;
    private bool _hasLastFocus;
    private float _timer;

    public int ActiveColliderCount => _activeProxies.Count;

    private void Awake()
    {
        _terrain = GetComponent<Terrain>();

        var containerGo = new GameObject("Tree Collider Proxies");
        _container = containerGo.transform;
        _container.SetParent(transform, false);

        Rebuild();
    }

#if UNITY_EDITOR
    // TerrainCollider has no runtime API for "Enable Tree Colliders", so the built-in
    // per-tree colliders are switched off through the serialized property instead
    // (saved with the scene). Otherwise every tree would collide twice.
    private void OnValidate()
    {
        TerrainCollider terrainCollider = GetComponent<TerrainCollider>();
        if (terrainCollider == null) return;

        var serialized = new UnityEditor.SerializedObject(terrainCollider);
        UnityEditor.SerializedProperty enableTrees = serialized.FindProperty("m_EnableTreeColliders");
        if (enableTrees == null)
        {
            Debug.LogWarning("[TerrainTreeColliderStreamer] Could not find 'Enable Tree Colliders' on the " +
                             "TerrainCollider. Untick it manually to avoid duplicate tree collision.", this);
            return;
        }

        if (enableTrees.boolValue)
        {
            enableTrees.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.EditorUtility.SetDirty(terrainCollider);
        }
    }
#endif

    private void OnDestroy()
    {
        if (_container != null)
        {
            Destroy(_container.gameObject);
        }
    }

    /// <summary>
    /// Re-reads tree instances/prototypes from the TerrainData. Call after modifying
    /// trees at runtime.
    /// </summary>
    public void Rebuild()
    {
        ReleaseAll();
        foreach (PrototypeTemplate template in _templates)
        {
            if (template == null) continue;
            while (template.pool.Count > 0)
            {
                Transform proxy = template.pool.Pop();
                if (proxy != null) Destroy(proxy.gameObject);
            }
        }

        TerrainData data = _terrain.terrainData;
        if (data == null)
        {
            _trees = new TreeEntry[0];
            _templates = new PrototypeTemplate[0];
            return;
        }

        BuildTemplates(data);
        BuildTrees(data);
        BuildGrid(data);
        Prewarm();

        _hasLastFocus = false;
        _timer = 0f;
    }

    private void BuildTemplates(TerrainData data)
    {
        TreePrototype[] prototypes = data.treePrototypes;
        _templates = new PrototypeTemplate[prototypes.Length];

        for (int i = 0; i < prototypes.Length; i++)
        {
            GameObject prefab = prototypes[i].prefab;
            if (prefab == null) continue;

            Collider source = prefab.GetComponent<Collider>();
            if (source == null) source = prefab.GetComponentInChildren<Collider>(true);
            if (source == null) continue;

            Transform root = prefab.transform;
            Transform colliderTransform = source.transform;
            Matrix4x4 relative = root.worldToLocalMatrix * colliderTransform.localToWorldMatrix;
            bool onRoot = colliderTransform == root;

            _templates[i] = new PrototypeTemplate
            {
                source = source,
                colliderLocalPosition = onRoot ? Vector3.zero : (Vector3)relative.GetColumn(3),
                colliderLocalRotation = onRoot ? Quaternion.identity : relative.rotation,
                colliderLocalScale = onRoot ? Vector3.one : relative.lossyScale,
                rootScale = root.localScale,
                layer = _terrain.preserveTreePrototypeLayers ? prefab.layer : gameObject.layer,
                tag = colliderTransform.gameObject.tag,
                name = prefab.name,
            };
        }
    }

    private void BuildTrees(TerrainData data)
    {
        TreeInstance[] instances = data.treeInstances;
        Vector3 size = data.size;
        _terrainOrigin = _terrain.GetPosition();

        var trees = new List<TreeEntry>(instances.Length);
        for (int i = 0; i < instances.Length; i++)
        {
            TreeInstance instance = instances[i];
            int prototypeIndex = instance.prototypeIndex;
            if (prototypeIndex < 0 || prototypeIndex >= _templates.Length || _templates[prototypeIndex] == null)
            {
                continue;
            }

            trees.Add(new TreeEntry
            {
                position = _terrainOrigin + Vector3.Scale(instance.position, size),
                rotationDegrees = instance.rotation * Mathf.Rad2Deg,
                widthScale = instance.widthScale,
                heightScale = instance.heightScale,
                prototypeIndex = prototypeIndex,
            });
        }

        _trees = trees.ToArray();
    }

    private void BuildGrid(TerrainData data)
    {
        _cellSize = Mathf.Max(1f, cellSize);
        _gridWidth = Mathf.Max(1, Mathf.CeilToInt(data.size.x / _cellSize));
        _gridHeight = Mathf.Max(1, Mathf.CeilToInt(data.size.z / _cellSize));
        int cellCount = _gridWidth * _gridHeight;

        var cellOfTree = new int[_trees.Length];
        var counts = new int[cellCount];
        for (int i = 0; i < _trees.Length; i++)
        {
            int x = CellCoord(_trees[i].position.x - _terrainOrigin.x, _gridWidth);
            int z = CellCoord(_trees[i].position.z - _terrainOrigin.z, _gridHeight);
            int cell = z * _gridWidth + x;
            cellOfTree[i] = cell;
            counts[cell]++;
        }

        _cellStart = new int[cellCount + 1];
        for (int c = 0; c < cellCount; c++)
        {
            _cellStart[c + 1] = _cellStart[c] + counts[c];
        }

        _cellTrees = new int[_trees.Length];
        var fill = new int[cellCount];
        for (int i = 0; i < _trees.Length; i++)
        {
            int cell = cellOfTree[i];
            _cellTrees[_cellStart[cell] + fill[cell]++] = i;
        }
    }

    private int CellCoord(float local, int max)
    {
        return Mathf.Clamp(Mathf.FloorToInt(local / _cellSize), 0, max - 1);
    }

    private void Prewarm()
    {
        for (int i = 0; i < _templates.Length; i++)
        {
            PrototypeTemplate template = _templates[i];
            if (template == null) continue;
            for (int n = template.pool.Count; n < prewarmPerPrototype; n++)
            {
                Transform proxy = CreateProxy(template);
                if (proxy == null) break;
                template.pool.Push(proxy);
            }
        }
    }

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = updateInterval;

        Transform focus = focusOverride != null ? focusOverride : GetMainCameraTransform();
        if (focus == null) return;

        Vector3 focusPosition = focus.position;
        if (_hasLastFocus && (focusPosition - _lastFocusPosition).sqrMagnitude < moveThreshold * moveThreshold)
        {
            return;
        }

        _lastFocusPosition = focusPosition;
        _hasLastFocus = true;
        Refresh(focusPosition);
    }

    private static Transform GetMainCameraTransform()
    {
        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private void Refresh(Vector3 focus)
    {
        float releaseRadius = activationRadius + Mathf.Max(0f, deactivationPadding);
        float activateSqr = activationRadius * activationRadius;
        float releaseSqr = releaseRadius * releaseRadius;

        // Release: only the currently active set needs checking.
        _toRelease.Clear();
        foreach (KeyValuePair<int, Transform> pair in _activeProxies)
        {
            if (HorizontalSqrDistance(_trees[pair.Key].position, focus) > releaseSqr) _toRelease.Add(pair.Key);
        }
        for (int i = 0; i < _toRelease.Count; i++)
        {
            Release(_toRelease[i]);
        }

        // Acquire: scan only grid cells overlapping the activation radius.
        int minX = CellCoord(focus.x - activationRadius - _terrainOrigin.x, _gridWidth);
        int maxX = CellCoord(focus.x + activationRadius - _terrainOrigin.x, _gridWidth);
        int minZ = CellCoord(focus.z - activationRadius - _terrainOrigin.z, _gridHeight);
        int maxZ = CellCoord(focus.z + activationRadius - _terrainOrigin.z, _gridHeight);

        for (int z = minZ; z <= maxZ; z++)
        {
            int rowStart = z * _gridWidth;
            for (int x = minX; x <= maxX; x++)
            {
                int cell = rowStart + x;
                int end = _cellStart[cell + 1];
                for (int k = _cellStart[cell]; k < end; k++)
                {
                    int treeIndex = _cellTrees[k];
                    if (HorizontalSqrDistance(_trees[treeIndex].position, focus) <= activateSqr
                        && !_activeProxies.ContainsKey(treeIndex))
                    {
                        Acquire(treeIndex);
                    }
                }
            }
        }
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void Acquire(int treeIndex)
    {
        TreeEntry tree = _trees[treeIndex];
        PrototypeTemplate template = _templates[tree.prototypeIndex];

        Transform proxy = template.pool.Count > 0 ? template.pool.Pop() : CreateProxy(template);
        if (proxy == null) return;

        proxy.SetPositionAndRotation(tree.position, Quaternion.Euler(0f, tree.rotationDegrees, 0f));
        proxy.localScale = Vector3.Scale(template.rootScale,
            new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
        proxy.gameObject.SetActive(true);
        _activeProxies.Add(treeIndex, proxy);
    }

    private void Release(int treeIndex)
    {
        if (!_activeProxies.TryGetValue(treeIndex, out Transform proxy)) return;
        _activeProxies.Remove(treeIndex);
        if (proxy == null) return;

        proxy.gameObject.SetActive(false);
        _templates[_trees[treeIndex].prototypeIndex].pool.Push(proxy);
    }

    private void ReleaseAll()
    {
        _toRelease.Clear();
        _toRelease.AddRange(_activeProxies.Keys);
        for (int i = 0; i < _toRelease.Count; i++)
        {
            Release(_toRelease[i]);
        }
    }

    private Transform CreateProxy(PrototypeTemplate template)
    {
        var rootGo = new GameObject("Tree Collider - " + template.name);
        rootGo.layer = template.layer;
        rootGo.SetActive(false);
        Transform root = rootGo.transform;
        root.SetParent(_container, false);

        var colliderGo = new GameObject("Collider");
        colliderGo.layer = template.layer;
        colliderGo.tag = template.tag;
        Transform colliderTransform = colliderGo.transform;
        colliderTransform.SetParent(root, false);
        colliderTransform.localPosition = template.colliderLocalPosition;
        colliderTransform.localRotation = template.colliderLocalRotation;
        colliderTransform.localScale = template.colliderLocalScale;

        Collider copy = CopyCollider(template.source, colliderGo);
        if (copy == null)
        {
            Destroy(rootGo);
            return null;
        }

        return root;
    }

    private static Collider CopyCollider(Collider source, GameObject target)
    {
        Collider copy;
        switch (source)
        {
            case CapsuleCollider capsule:
            {
                var c = target.AddComponent<CapsuleCollider>();
                c.center = capsule.center;
                c.radius = capsule.radius;
                c.height = capsule.height;
                c.direction = capsule.direction;
                copy = c;
                break;
            }
            case SphereCollider sphere:
            {
                var c = target.AddComponent<SphereCollider>();
                c.center = sphere.center;
                c.radius = sphere.radius;
                copy = c;
                break;
            }
            case BoxCollider box:
            {
                var c = target.AddComponent<BoxCollider>();
                c.center = box.center;
                c.size = box.size;
                copy = c;
                break;
            }
            case MeshCollider mesh:
            {
                var c = target.AddComponent<MeshCollider>();
                c.sharedMesh = mesh.sharedMesh;
                c.convex = mesh.convex;
                copy = c;
                break;
            }
            default:
                Debug.LogWarning($"[TerrainTreeColliderStreamer] Unsupported tree collider type {source.GetType().Name}.");
                return null;
        }

        copy.sharedMaterial = source.sharedMaterial;
        copy.isTrigger = source.isTrigger;
        copy.includeLayers = source.includeLayers;
        copy.excludeLayers = source.excludeLayers;
        return copy;
    }
}
