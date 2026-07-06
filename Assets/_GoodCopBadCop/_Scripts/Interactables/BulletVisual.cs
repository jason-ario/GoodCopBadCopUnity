using UnityEngine;

/// <summary>
/// Purely cosmetic bullet that travels in a straight line from the muzzle and
/// auto-destroys after a set lifetime. A TrailRenderer on the same GameObject
/// produces the visible streak. No collision or physics — this is visuals only.
///
/// Each frame the bullet raycasts ahead by the distance it is about to travel.
/// On hit it spawns an optional impact VFX prefab aligned to the surface normal,
/// then destroys itself immediately (leaving the impact particle to self-clean).
///
/// Prefab requirements:
///   - TrailRenderer component (configured in the prefab Inspector)
///   - This component
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class BulletVisual : MonoBehaviour
{
    [Tooltip("Travel speed in metres per second.")]
    [SerializeField] private float _speed = 250f;

    [Tooltip("Seconds before the GameObject is destroyed if nothing is hit.")]
    [SerializeField] private float _lifetime = 3f;

    [Header("Impact")]
    [Tooltip("Particle prefab spawned at the surface hit point. Must have Stop Action = Destroy.")]
    [SerializeField] private GameObject _impactVFXPrefab;

    [Tooltip("Layers the bullet checks for impact. Defaults to Everything.")]
    [SerializeField] private LayerMask _impactLayers = ~0;

    private Vector3 _direction;
    private bool _initialized;

    /// <summary>
    /// Call immediately after instantiation to set the bullet in motion.
    /// </summary>
    /// <param name="spawnPosition">World-space muzzle position.</param>
    /// <param name="direction">World-space travel direction (does not need to be normalised).</param>
    public void Initialize(Vector3 spawnPosition, Vector3 direction)
    {
        transform.position = spawnPosition;
        _direction = direction.normalized;
        transform.forward = _direction;
        _initialized = true;
        Destroy(gameObject, _lifetime);
    }

    private void Update()
    {
        if (!_initialized) return;

        float stepDistance = _speed * Time.deltaTime;

        // Check for surfaces in the path before moving so we don't pass through thin geometry.
        if (Physics.Raycast(transform.position, _direction, out RaycastHit hit, stepDistance, _impactLayers, QueryTriggerInteraction.Ignore))
        {
            SpawnImpact(hit.point, hit.normal);
            Destroy(gameObject);
            return;
        }

        transform.position += _direction * stepDistance;
    }

    private void SpawnImpact(Vector3 position, Vector3 normal)
    {
        if (_impactVFXPrefab == null) return;

        // Build a stable tangent on the surface plane to use as the LookRotation forward axis.
        // Prefer Vector3.forward; fall back to Vector3.up when the normal is nearly parallel to it.
        Vector3 tangent = Mathf.Abs(Vector3.Dot(normal, Vector3.forward)) < 0.999f
            ? Vector3.Cross(normal, Vector3.forward).normalized
            : Vector3.Cross(normal, Vector3.up).normalized;

        // LookRotation(forward, upwards) sets +Z = tangent and +Y = normal.
        // The Hemisphere particle shape emits along local +Y, so particles spray along the hit normal.
        Quaternion rotation = Quaternion.LookRotation(tangent, normal);
        GameObject impact = Instantiate(_impactVFXPrefab, position, rotation);

        // Safety cleanup — the particle lifetimes are sub-second so 2 s is more than enough.
        Destroy(impact, 2f);
    }
}
