using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compound-collider region that defines "the checkpoint" for cleanup-scoring purposes — i.e.
/// the volume inside which mutant corpses, gore pieces, blood splatters, and stray junk COUNT
/// toward <see cref="TakeOutTrashTask"/>'s objective total and, through it,
/// <see cref="CheckpointIntegrityService"/>'s Checkpoint Integrity Score.
///
/// Why this exists separately from <see cref="TakeOutTrashTask"/>'s <c>SpawnZone</c>s:
/// those zones describe where the task RANDOMLY PLACES items — they are deliberately tight,
/// flat, and tucked inside the yard so spawned trash never lands on a wall, in the booth, or
/// under a prop. Reusing them as the "does this count?" test made the counted region
/// dramatically smaller than the fenced checkpoint the player perceives, so a body that died
/// three metres from a trash zone (but well inside the fence) silently stopped counting. This
/// component lets the counted region be authored independently — and much wider — than the
/// spawn footprint: build it from as many box/sphere/capsule colliders as needed to trace the
/// actual fence line, including the gate apron, booth surrounds, and side alleys.
///
/// Scene setup:
///   - Add this to a single GameObject (e.g. "Checkpoint Cleanup Area") under ENV_ EXTERIOR.
///   - Add child GameObjects with BoxCollider/SphereCollider/CapsuleCollider components shaped
///     to cover the interior of the fence. Leave <see cref="_includeChildColliders"/> on and they
///     are picked up automatically; otherwise list them explicitly in <see cref="_regions"/>.
///   - The colliders are used purely as geometry queries: they are never raycast against and do
///     not need to be enabled, on any particular layer, or marked as triggers. Mark them as
///     triggers on an unused layer anyway if you want to be certain they never affect physics.
///   - Assign this to <see cref="TakeOutTrashTask"/>'s cleanup-area field (or leave it empty and
///     the task resolves <see cref="Instance"/> automatically).
///
/// Give the volumes real, finite height. <see cref="_ignoreHeight"/> defaults to false on
/// purpose: an infinitely tall region would keep counting a corpse that clipped through the
/// world and is falling forever inside the fence's XZ footprint, which is exactly the
/// required-but-unreachable state that soft-locks the objective.
/// </summary>
[DisallowMultipleComponent]
public class CheckpointCleanupArea : MonoBehaviour
{
    /// <summary>
    /// Most-recently-awoken area in the scene. <see cref="TakeOutTrashTask"/> falls back to this
    /// when its own reference is unassigned, so a designer only has to drop the component in.
    /// </summary>
    public static CheckpointCleanupArea Instance { get; private set; }

    [Tooltip("Colliders whose union forms the checkpoint region. Used as pure geometry — never " +
             "raycast against, and they do not need to be enabled or on any specific layer.")]
    [SerializeField] private List<Collider> _regions = new List<Collider>();

    [Tooltip("Also treat every Collider on this GameObject and its children as part of the " +
             "region, including disabled ones. Leave on to author the area by simply parenting " +
             "box colliders under this object.")]
    [SerializeField] private bool _includeChildColliders = true;

    [Tooltip("Ignore the query point's height and test only the horizontal footprint (matches " +
             "the old SpawnZone behaviour). Leave OFF: an unbounded-height region keeps counting " +
             "a corpse that fell through the world, which can soft-lock the cleanup objective.")]
    [SerializeField] private bool _ignoreHeight = false;

    [Tooltip("Extra slack in metres added to every region's surface, so an item resting exactly " +
             "on the boundary (or half-embedded in the fence line) still counts.")]
    [Min(0f)]
    [SerializeField] private float _boundaryTolerance = 0.25f;

    private readonly List<Collider> _resolved = new List<Collider>();
    private bool _warnedUnsupportedShape;

    /// <summary>True when at least one usable region collider was resolved.</summary>
    public bool HasRegions => _resolved.Count > 0;

    private void Awake()
    {
        Instance = this;
        ResolveRegions();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Rebuilds the cached region list from <see cref="_regions"/> plus (optionally) every child
    /// collider. Call after adding or removing region colliders at runtime.
    /// </summary>
    public void ResolveRegions()
    {
        _resolved.Clear();

        foreach (Collider c in _regions)
        {
            if (c != null && !_resolved.Contains(c))
                _resolved.Add(c);
        }

        if (_includeChildColliders)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (c != null && !_resolved.Contains(c))
                    _resolved.Add(c);
            }
        }

        if (_resolved.Count == 0)
        {
            Debug.LogWarning($"[CheckpointCleanupArea] '{name}' has no region colliders — every " +
                             "position will test as OUTSIDE the checkpoint. Add box colliders as " +
                             "children, or list them in Regions.");
        }
    }

    /// <summary>
    /// Returns true when <paramref name="worldPosition"/> falls inside any region collider
    /// (within <see cref="_boundaryTolerance"/>). Pure maths against each collider's local shape,
    /// so it works on disabled colliders and never touches the physics scene.
    /// </summary>
    public bool Contains(Vector3 worldPosition)
    {
        for (int i = 0; i < _resolved.Count; i++)
        {
            Collider c = _resolved[i];
            if (c == null) continue;

            if (ContainsInCollider(c, worldPosition))
                return true;
        }

        return false;
    }

    // ── Shape tests ───────────────────────────────────────────────────────────

    private bool ContainsInCollider(Collider collider, Vector3 point)
    {
        if (_ignoreHeight)
            point.y = GetRegionCenter(collider).y;

        switch (collider)
        {
            case BoxCollider box:
            {
                Vector3 local = box.transform.InverseTransformPoint(point) - box.center;
                Vector3 half  = box.size * 0.5f;
                Vector3 scale = AbsLossyScale(box.transform);

                return Mathf.Abs(local.x) <= half.x + LocalTolerance(scale.x)
                    && Mathf.Abs(local.y) <= half.y + LocalTolerance(scale.y)
                    && Mathf.Abs(local.z) <= half.z + LocalTolerance(scale.z);
            }

            case SphereCollider sphere:
            {
                Vector3 local = sphere.transform.InverseTransformPoint(point) - sphere.center;
                Vector3 scale = AbsLossyScale(sphere.transform);
                float   reach = sphere.radius + LocalTolerance(MaxComponent(scale));

                return local.sqrMagnitude <= reach * reach;
            }

            case CapsuleCollider capsule:
            {
                Vector3 local = capsule.transform.InverseTransformPoint(point) - capsule.center;
                Vector3 scale = AbsLossyScale(capsule.transform);

                int   axis     = Mathf.Clamp(capsule.direction, 0, 2);
                float halfLine = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);

                Vector3 nearest = Vector3.zero;
                nearest[axis] = Mathf.Clamp(local[axis], -halfLine, halfLine);

                float reach = capsule.radius + LocalTolerance(MaxComponent(scale));
                return (local - nearest).sqrMagnitude <= reach * reach;
            }

            default:
            {
                // MeshCollider/TerrainCollider and friends: no cheap analytic containment test, and
                // Collider.ClosestPoint returns the input point unchanged for a non-convex mesh
                // (which would read as "inside" everywhere). Fall back to the world AABB and warn
                // once — author the region from primitives instead.
                if (!_warnedUnsupportedShape)
                {
                    _warnedUnsupportedShape = true;
                    Debug.LogWarning($"[CheckpointCleanupArea] '{name}' contains an unsupported " +
                                     $"region collider type ({collider.GetType().Name} on " +
                                     $"'{collider.name}') — falling back to its bounding box. Use " +
                                     "Box/Sphere/Capsule colliders for an accurate region.");
                }

                Bounds bounds = collider.bounds;
                bounds.Expand(_boundaryTolerance * 2f);
                return bounds.Contains(point);
            }
        }
    }

    private float LocalTolerance(float scaleComponent)
    {
        // Convert the world-space slack into the collider's local units.
        return _boundaryTolerance / Mathf.Max(scaleComponent, 0.0001f);
    }

    private static Vector3 AbsLossyScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    private static float MaxComponent(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

    private static Vector3 GetRegionCenter(Collider collider)
    {
        switch (collider)
        {
            case BoxCollider box:         return box.transform.TransformPoint(box.center);
            case SphereCollider sphere:   return sphere.transform.TransformPoint(sphere.center);
            case CapsuleCollider capsule: return capsule.transform.TransformPoint(capsule.center);
            default:                      return collider.transform.position;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos() => DrawGizmo(false);

    private void OnDrawGizmosSelected() => DrawGizmo(true);

    private void DrawGizmo(bool selected)
    {
        // Distinct from SpawnZone's cyan so the two regions are never confused in the viewport.
        Color fill = new Color(1f, 0.55f, 0.1f, selected ? 0.18f : 0.06f);
        Color line = new Color(1f, 0.55f, 0.1f, selected ? 1f : 0.45f);

        // Gizmos run outside the normal lifecycle (and in edit mode), so resolve on the fly.
        var toDraw = new List<Collider>(_regions);
        if (_includeChildColliders)
            toDraw.AddRange(GetComponentsInChildren<Collider>(includeInactive: true));

        foreach (Collider c in toDraw)
        {
            if (c == null) continue;

            Gizmos.matrix = c.transform.localToWorldMatrix;

            if (c is BoxCollider box)
            {
                Gizmos.color = fill;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = line;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (c is SphereCollider sphere)
            {
                Gizmos.color = fill;
                Gizmos.DrawSphere(sphere.center, sphere.radius);
                Gizmos.color = line;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = line;
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        if (selected)
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, "Checkpoint Cleanup Area");
    }
#endif
}
