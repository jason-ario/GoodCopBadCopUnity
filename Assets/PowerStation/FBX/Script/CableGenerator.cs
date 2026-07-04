using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CableGenerator : MonoBehaviour
{
    [Header("Anchors")]
    public Transform anchorA;
    public Transform anchorB;

    [Header("Sag")]
    [Min(0f)] public float sagRatio = 0.05f; // Unlocked! Push this as high as you want.
    [Range(6, 40)] public int pathSegments = 16;

    [Header("Main Cable Core")]
    public float mainRadius = 0.05f;
    [Range(3, 12)] public int mainSides = 8;

    [Header("Wrapped Wires")]
    [Range(0, 8)] public int wireCount = 4;
    public float wireRadius = 0.012f;
    [Range(3, 6)] public int wireSides = 4;
    public float wrapPitch = 0.35f;
    [Range(0f, 0.3f)] public float jitter = 0.1f;

    [Header("Rendering")]
    public Material material;          // Main plastic sheath (Submesh 0)
    public float uvTilingPerMeter = 1f;

    [Header("Hanging / Severed Mode")]
    public bool isHanging = false;     // Changes wind from parabola to pendulum!
    public Material capMaterial;       // Optional metal texture for the exposed cut end (Submesh 1)

    [Header("Wind (Baked into Vertex Colors)")]
    public bool windEnabled = true;

    private MeshFilter _mf;
    private MeshRenderer _mr;

    private void OnEnable()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
    }

    public void Regenerate()
    {
        if (anchorA == null || anchorB == null) return;
        if (_mf == null) _mf = GetComponent<MeshFilter>();
        if (_mr == null) _mr = GetComponent<MeshRenderer>();

        float spanLength = SpanLength();
        
        // Convert world positions to local space relative to this GameObject
        Vector3 localPosA = transform.InverseTransformPoint(anchorA.position);
        Vector3 localPosB = transform.InverseTransformPoint(anchorB.position);

        var samples = CableSpline.SamplePath(localPosA, localPosB, spanLength * sagRatio, pathSegments);
        var frames = CableSpline.BuildParallelTransportFrames(samples);

        bool hasCap = (capMaterial != null);

        var meshData = CableMeshBuilder.BuildCableMesh(
            samples, frames,
            mainRadius, mainSides,
            wireCount, wireRadius, wireSides, wrapPitch, jitter,
            uvTilingPerMeter, windEnabled,
            isHanging, hasCap
        );

        // Clean up memory in edit mode
        if (_mf.sharedMesh != null && !UnityEditor.AssetDatabase.Contains(_mf.sharedMesh))
        {
            if (Application.isPlaying) Destroy(_mf.sharedMesh);
            else DestroyImmediate(_mf.sharedMesh);
        }

        _mf.sharedMesh = meshData.ToMesh();

        // Assign materials correctly based on whether we generated 1 or 2 submeshes
        if (isHanging && hasCap)
        {
            _mr.sharedMaterials = new Material[] { material, capMaterial };
        }
        else
        {
            _mr.sharedMaterial = material;
        }
    }

    public float SpanLength()
    {
        if (anchorA == null || anchorB == null) return 0f;
        return Vector3.Distance(anchorA.position, anchorB.position);
    }
    
    public int GetEstimatedTriangles()
    {
        int mainTris = pathSegments * mainSides * 2;
        int wireTris = wireCount * pathSegments * wireSides * 2;
        int capTris = isHanging ? (mainSides * 2) + (wireCount * wireSides * 2) : 0;
        return mainTris + wireTris + capTris;
    }
}