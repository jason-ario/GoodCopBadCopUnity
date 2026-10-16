using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GPU-instanced replacement for the terrain's own tree rendering, Play Mode only.
/// Unity draws painted mesh trees (LODGroup prefabs) one draw call per tree part,
/// so a dense forest costs thousands of draws. This component reads the same tree
/// instances painted on the terrain, sets the terrain's Tree Distance to 0 while
/// active (painting and the Edit Mode view are untouched), and submits the trees with
/// <see cref="Graphics.RenderMeshInstanced{T}(in RenderParams, Mesh, int, T[], int, int)"/>,
/// batched per prototype / LOD / mesh part / material.
///
/// Per camera, trees are bucketed in a flat (CSR) grid, culled by cell then per tree
/// against a padded frustum (padding keeps off-screen trees casting shadows into
/// view), and LOD-selected with the same screen-relative-height rule as LODGroup
/// (honours QualitySettings.lodBias). LOD cross-fade is not reproduced (LODs pop).
/// Call <see cref="Rebuild"/> after modifying terrain trees at runtime.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain))]
public class TerrainTreeInstancedRenderer : MonoBehaviour
{
    [Header("Culling")]
    [Tooltip("Grid cell size (meters) used to bucket tree instances for culling.")]
    [SerializeField] private float cellSize = 32f;

    [Tooltip("Frustum planes are pushed out by this many meters so trees just off-screen still cast shadows into view.")]
    [SerializeField] private float frustumPadding = 12f;

    [Tooltip("Max tree draw distance. 0 = use the terrain's Tree Distance.")]
    [SerializeField] private float maxDistanceOverride;

    [Header("Rendering")]
    [Tooltip("Light probe mode for the instanced draws. The scene has no baked probes, so BlendProbes resolves to the ambient probe like the terrain did.")]
    [SerializeField] private LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes;

    [Tooltip("Tree prototype prefabs that keep Unity's own terrain rendering (e.g. grass whose shader relies on " +
             "terrain-supplied per-object data). Their instances are drawn by a hidden helper terrain instead of " +
             "being instanced.")]
    [SerializeField] private GameObject[] terrainDrawnPrototypes = new GameObject[0];

    // Instancing limit when both objectToWorld and worldToObject are sent per instance
    // (tree width/height scales are non-uniform, so both are needed).
    private const int MaxInstancesPerDraw = 511;

    private sealed class Part
    {
        public Mesh mesh;
        public int submesh;
        public Material material;
        public Matrix4x4 local;
        public bool localIsIdentity;
        public ShadowCastingMode shadows;
        public bool receiveShadows;
        public int layer;
        public Matrix4x4[] buffer = new Matrix4x4[64];
        public int count;

        public void Add(in Matrix4x4 matrix)
        {
            if (count == buffer.Length)
            {
                System.Array.Resize(ref buffer, buffer.Length * 2);
            }
            buffer[count++] = matrix;
        }
    }

    private sealed class Lod
    {
        public float screenHeight;
        public Part[] parts;
    }

    private sealed class Prototype
    {
        public Lod[] lods;
        public float lodSize;
        public Vector3 lodReference;
        public Vector3 rootScale;
        public Bounds localBounds;
    }

    private Terrain _terrain;
    private Prototype[] _prototypes = new Prototype[0];

    private int _treeCount;
    private Matrix4x4[] _treeMatrices = new Matrix4x4[0];
    private Vector3[] _treeLodPoints = new Vector3[0];
    private float[] _treeLodSizes = new float[0];
    private Vector3[] _treeCenters = new Vector3[0];
    private float[] _treeRadii = new float[0];
    private int[] _treePrototype = new int[0];

    private int _cellCount;
    private int[] _cellStart = new int[1];
    private int[] _cellTrees = new int[0];
    private Bounds[] _cellBounds = new Bounds[0];
    private Bounds _worldBounds;

    private readonly Dictionary<Material, Material> _instancedMaterials = new Dictionary<Material, Material>();
    private readonly List<Material> _ownedMaterials = new List<Material>();
    private readonly List<Part> _activeParts = new List<Part>();
    private readonly Plane[] _planes = new Plane[6];

    private float _terrainTreeDistance;
    private bool _tookOverTerrain;

    private Terrain _helperTerrain;
    private TerrainData _helperData;

    public int TreeCount => _treeCount;
    public int LastFrameDrawCalls { get; private set; }
    public int LastFrameInstances { get; private set; }

    private void Awake()
    {
        _terrain = GetComponent<Terrain>();
    }

    private void OnEnable()
    {
        if (_terrain == null) _terrain = GetComponent<Terrain>();

        _terrainTreeDistance = _terrain.treeDistance;
        Rebuild();

        _terrain.treeDistance = 0f;
        _tookOverTerrain = true;
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;

        if (_tookOverTerrain && _terrain != null)
        {
            _terrain.treeDistance = _terrainTreeDistance;
        }
        _tookOverTerrain = false;

        DestroyHelperTerrain();

        foreach (Material material in _ownedMaterials)
        {
            if (material != null) Destroy(material);
        }
        _ownedMaterials.Clear();
        _instancedMaterials.Clear();
    }

    /// <summary>
    /// Re-reads tree prototypes/instances from the TerrainData. Call after modifying
    /// trees at runtime.
    /// </summary>
    public void Rebuild()
    {
        TerrainData data = _terrain.terrainData;
        if (data == null)
        {
            _prototypes = new Prototype[0];
            _treeCount = 0;
            _cellCount = 0;
            DestroyHelperTerrain();
            return;
        }

        BuildPrototypes(data);
        BuildTrees(data);
        BuildGrid(data);
        BuildHelperTerrain(data);
    }

    private bool IsTerrainDrawn(GameObject prefab)
    {
        if (prefab == null || terrainDrawnPrototypes == null) return false;
        return System.Array.IndexOf(terrainDrawnPrototypes, prefab) >= 0;
    }

    private void BuildPrototypes(TerrainData data)
    {
        TreePrototype[] source = data.treePrototypes;
        _prototypes = new Prototype[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            _prototypes[i] = IsTerrainDrawn(source[i].prefab) ? null : BuildPrototype(source[i].prefab);
        }
    }

    // A geometry-less terrain that holds only the terrain-drawn prototypes, so Unity's
    // terrain tree path keeps rendering them exactly as before (main terrain's Tree
    // Distance is 0 while this component is active, which hides all of its trees).
    private void BuildHelperTerrain(TerrainData data)
    {
        DestroyHelperTerrain();

        TreePrototype[] prototypes = data.treePrototypes;
        var remap = new int[prototypes.Length];
        var kept = new List<TreePrototype>();
        for (int i = 0; i < prototypes.Length; i++)
        {
            remap[i] = -1;
            if (!IsTerrainDrawn(prototypes[i].prefab)) continue;
            remap[i] = kept.Count;
            kept.Add(prototypes[i]);
        }
        if (kept.Count == 0) return;

        var instances = new List<TreeInstance>();
        foreach (TreeInstance instance in data.treeInstances)
        {
            if (instance.prototypeIndex < 0 || instance.prototypeIndex >= remap.Length) continue;
            int mapped = remap[instance.prototypeIndex];
            if (mapped < 0) continue;
            TreeInstance copy = instance;
            copy.prototypeIndex = mapped;
            instances.Add(copy);
        }
        if (instances.Count == 0) return;

        _helperData = new TerrainData { name = data.name + " (Terrain-Drawn Trees)", hideFlags = HideFlags.DontSave };
        _helperData.heightmapResolution = 33;
        _helperData.size = data.size;
        _helperData.treePrototypes = kept.ToArray();
        _helperData.SetTreeInstances(instances.ToArray(), false);

        var helperGo = new GameObject("Terrain-Drawn Trees") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
        helperGo.transform.SetParent(transform, false);
        helperGo.transform.position = _terrain.GetPosition();

        _helperTerrain = helperGo.AddComponent<Terrain>();
        _helperTerrain.allowAutoConnect = false;
        _helperTerrain.terrainData = _helperData;
        _helperTerrain.materialTemplate = _terrain.materialTemplate;
        _helperTerrain.drawHeightmap = false;
        _helperTerrain.drawTreesAndFoliage = true;
        _helperTerrain.drawInstanced = _terrain.drawInstanced;
        _helperTerrain.treeDistance = _terrainTreeDistance;
        _helperTerrain.treeBillboardDistance = _terrain.treeBillboardDistance;
        _helperTerrain.treeCrossFadeLength = _terrain.treeCrossFadeLength;
        _helperTerrain.treeMaximumFullLODCount = _terrain.treeMaximumFullLODCount;
        _helperTerrain.preserveTreePrototypeLayers = _terrain.preserveTreePrototypeLayers;
        _helperTerrain.bakeLightProbesForTrees = _terrain.bakeLightProbesForTrees;
        _helperTerrain.deringLightProbesForTrees = _terrain.deringLightProbesForTrees;
        _helperTerrain.treeMotionVectorModeOverride = _terrain.treeMotionVectorModeOverride;
        _helperTerrain.shadowCastingMode = _terrain.shadowCastingMode;
        _helperTerrain.reflectionProbeUsage = _terrain.reflectionProbeUsage;
        _helperTerrain.renderingLayerMask = _terrain.renderingLayerMask;
    }

    private void DestroyHelperTerrain()
    {
        if (_helperTerrain != null) Destroy(_helperTerrain.gameObject);
        if (_helperData != null) Destroy(_helperData);
        _helperTerrain = null;
        _helperData = null;
    }

    private Prototype BuildPrototype(GameObject prefab)
    {
        if (prefab == null) return null;

        Transform root = prefab.transform;
        Matrix4x4 rootInverse = root.worldToLocalMatrix;
        LODGroup lodGroup = prefab.GetComponent<LODGroup>();

        var prototype = new Prototype { rootScale = root.localScale };
        bool hasBounds = false;

        if (lodGroup != null)
        {
            LOD[] lods = lodGroup.GetLODs();
            prototype.lods = new Lod[lods.Length];
            prototype.lodSize = lodGroup.size;
            prototype.lodReference = lodGroup.localReferencePoint;
            for (int l = 0; l < lods.Length; l++)
            {
                prototype.lods[l] = new Lod
                {
                    screenHeight = lods[l].screenRelativeTransitionHeight,
                    parts = BuildParts(lods[l].renderers, rootInverse, prototype, ref hasBounds),
                };
            }
        }
        else
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            prototype.lods = new[]
            {
                new Lod { screenHeight = 0f, parts = BuildParts(renderers, rootInverse, prototype, ref hasBounds) },
            };
        }

        if (!hasBounds) return null;

        if (lodGroup == null)
        {
            prototype.lodReference = prototype.localBounds.center;
            Vector3 s = prototype.localBounds.size;
            prototype.lodSize = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }

        return prototype;
    }

    private Part[] BuildParts(Renderer[] renderers, Matrix4x4 rootInverse, Prototype prototype, ref bool hasBounds)
    {
        var parts = new List<Part>();
        foreach (Renderer renderer in renderers)
        {
            if (!(renderer is MeshRenderer) || !renderer.enabled) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;

            Matrix4x4 local = rootInverse * renderer.transform.localToWorldMatrix;
            bool identity = local.isIdentity;
            int layer = _terrain.preserveTreePrototypeLayers ? renderer.gameObject.layer : gameObject.layer;
            Material[] materials = renderer.sharedMaterials;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material material = materials.Length > 0 ? materials[Mathf.Min(sub, materials.Length - 1)] : null;
                if (material == null) continue;

                parts.Add(new Part
                {
                    mesh = mesh,
                    submesh = sub,
                    material = GetInstancedMaterial(material),
                    local = local,
                    localIsIdentity = identity,
                    shadows = renderer.shadowCastingMode,
                    receiveShadows = renderer.receiveShadows,
                    layer = layer,
                });
            }

            Bounds meshBounds = TransformBounds(local, mesh.bounds);
            if (hasBounds) prototype.localBounds.Encapsulate(meshBounds);
            else { prototype.localBounds = meshBounds; hasBounds = true; }
        }
        return parts.ToArray();
    }

    private Material GetInstancedMaterial(Material material)
    {
        if (material.enableInstancing) return material;
        if (_instancedMaterials.TryGetValue(material, out Material instanced)) return instanced;

        instanced = new Material(material)
        {
            name = material.name + " (Instanced)",
            enableInstancing = true,
        };
        _instancedMaterials[material] = instanced;
        _ownedMaterials.Add(instanced);
        return instanced;
    }

    private void BuildTrees(TerrainData data)
    {
        TreeInstance[] instances = data.treeInstances;
        Vector3 size = data.size;
        Vector3 origin = _terrain.GetPosition();

        _treeMatrices = new Matrix4x4[instances.Length];
        _treeLodPoints = new Vector3[instances.Length];
        _treeLodSizes = new float[instances.Length];
        _treeCenters = new Vector3[instances.Length];
        _treeRadii = new float[instances.Length];
        _treePrototype = new int[instances.Length];
        _treeCount = 0;

        bool hasWorldBounds = false;
        for (int i = 0; i < instances.Length; i++)
        {
            TreeInstance instance = instances[i];
            int prototypeIndex = instance.prototypeIndex;
            if (prototypeIndex < 0 || prototypeIndex >= _prototypes.Length) continue;
            Prototype prototype = _prototypes[prototypeIndex];
            if (prototype == null) continue;

            Vector3 position = origin + Vector3.Scale(instance.position, size);
            Quaternion rotation = Quaternion.Euler(0f, instance.rotation * Mathf.Rad2Deg, 0f);
            Vector3 scale = Vector3.Scale(new Vector3(instance.widthScale, instance.heightScale, instance.widthScale), prototype.rootScale);
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
            Bounds bounds = TransformBounds(matrix, prototype.localBounds);

            int t = _treeCount++;
            _treeMatrices[t] = matrix;
            _treeLodPoints[t] = matrix.MultiplyPoint3x4(prototype.lodReference);
            _treeLodSizes[t] = prototype.lodSize * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            _treeCenters[t] = bounds.center;
            _treeRadii[t] = bounds.extents.magnitude;
            _treePrototype[t] = prototypeIndex;

            if (hasWorldBounds) _worldBounds.Encapsulate(bounds);
            else { _worldBounds = bounds; hasWorldBounds = true; }
        }
    }

    private void BuildGrid(TerrainData data)
    {
        Vector3 origin = _terrain.GetPosition();
        float cell = Mathf.Max(1f, cellSize);
        int width = Mathf.Max(1, Mathf.CeilToInt(data.size.x / cell));
        int height = Mathf.Max(1, Mathf.CeilToInt(data.size.z / cell));
        _cellCount = width * height;

        var cellOfTree = new int[_treeCount];
        var counts = new int[_cellCount];
        for (int i = 0; i < _treeCount; i++)
        {
            Vector3 p = _treeMatrices[i].GetPosition();
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / cell), 0, width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / cell), 0, height - 1);
            int c = z * width + x;
            cellOfTree[i] = c;
            counts[c]++;
        }

        _cellStart = new int[_cellCount + 1];
        for (int c = 0; c < _cellCount; c++)
        {
            _cellStart[c + 1] = _cellStart[c] + counts[c];
        }

        _cellTrees = new int[_treeCount];
        _cellBounds = new Bounds[_cellCount];
        var fill = new int[_cellCount];
        for (int i = 0; i < _treeCount; i++)
        {
            int c = cellOfTree[i];
            var treeBounds = new Bounds(_treeCenters[i], Vector3.one * (_treeRadii[i] * 2f));
            if (fill[c] == 0) _cellBounds[c] = treeBounds;
            else _cellBounds[c].Encapsulate(treeBounds);
            _cellTrees[_cellStart[c] + fill[c]++] = i;
        }
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (_treeCount == 0) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

        GeometryUtility.CalculateFrustumPlanes(cam, _planes);
        for (int p = 0; p < 6; p++)
        {
            _planes[p].distance += frustumPadding;
        }

        Vector3 cameraPosition = cam.transform.position;
        float maxDistance = maxDistanceOverride > 0f ? maxDistanceOverride : _terrainTreeDistance;
        float maxDistanceSq = maxDistance * maxDistance;
        float lodBias = QualitySettings.lodBias;
        bool orthographic = cam.orthographic;
        float halfTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        int cullingMask = cam.cullingMask;

        for (int c = 0; c < _cellCount; c++)
        {
            int start = _cellStart[c];
            int end = _cellStart[c + 1];
            if (start == end) continue;

            Bounds cellBounds = _cellBounds[c];
            if (cellBounds.SqrDistance(cameraPosition) > maxDistanceSq) continue;
            if (!GeometryUtility.TestPlanesAABB(_planes, cellBounds)) continue;

            for (int k = start; k < end; k++)
            {
                int i = _cellTrees[k];
                float distanceSq = (_treeLodPoints[i] - cameraPosition).sqrMagnitude;
                if (distanceSq > maxDistanceSq) continue;
                if (!SphereInFrustum(_treeCenters[i], _treeRadii[i])) continue;

                float relativeHeight = orthographic
                    ? _treeLodSizes[i] * 0.5f * lodBias / Mathf.Max(cam.orthographicSize, 0.0001f)
                    : _treeLodSizes[i] * 0.5f * lodBias / (Mathf.Max(Mathf.Sqrt(distanceSq), 0.0001f) * halfTan);

                Lod[] lods = _prototypes[_treePrototype[i]].lods;
                Lod lod = null;
                for (int l = 0; l < lods.Length; l++)
                {
                    if (relativeHeight >= lods[l].screenHeight)
                    {
                        lod = lods[l];
                        break;
                    }
                }
                if (lod == null) continue;

                Matrix4x4 treeMatrix = _treeMatrices[i];
                Part[] parts = lod.parts;
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    Part part = parts[pi];
                    if ((cullingMask & (1 << part.layer)) == 0) continue;
                    if (part.count == 0) _activeParts.Add(part);
                    if (part.localIsIdentity) part.Add(treeMatrix);
                    else part.Add(treeMatrix * part.local);
                }
            }
        }

        int draws = 0;
        int instances = 0;
        for (int a = 0; a < _activeParts.Count; a++)
        {
            Part part = _activeParts[a];
            var renderParams = new RenderParams(part.material)
            {
                camera = cam,
                layer = part.layer,
                shadowCastingMode = part.shadows,
                receiveShadows = part.receiveShadows,
                lightProbeUsage = lightProbeUsage,
                motionVectorMode = MotionVectorGenerationMode.Camera,
                worldBounds = _worldBounds,
            };

            for (int start = 0; start < part.count; start += MaxInstancesPerDraw)
            {
                int count = Mathf.Min(MaxInstancesPerDraw, part.count - start);
                Graphics.RenderMeshInstanced(renderParams, part.mesh, part.submesh, part.buffer, count, start);
                draws++;
            }
            instances += part.count;
            part.count = 0;
        }
        _activeParts.Clear();

        if (cam.cameraType == CameraType.Game)
        {
            LastFrameDrawCalls = draws;
            LastFrameInstances = instances;
        }
    }

    private bool SphereInFrustum(Vector3 center, float radius)
    {
        for (int p = 0; p < 6; p++)
        {
            if (_planes[p].GetDistanceToPoint(center) < -radius) return false;
        }
        return true;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 e = bounds.extents;
        Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0f, 0f));
        Vector3 y = matrix.MultiplyVector(new Vector3(0f, e.y, 0f));
        Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, e.z));
        var extents = new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(center, extents * 2f);
    }
}
