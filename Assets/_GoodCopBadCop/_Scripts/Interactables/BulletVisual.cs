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
    // NOTE: perimeter fence colliders never stop the tracer (see TryFindImpact).

    [Tooltip("Travel speed in metres per second.")]
    [SerializeField] private float _speed = 250f;

    [Tooltip("Seconds before the GameObject is destroyed if nothing is hit.")]
    [SerializeField] private float _lifetime = 3f;

    [Header("Impact")]
    [Tooltip("Particle prefab spawned at the surface hit point. Must have Stop Action = Destroy.")]
    [SerializeField] private GameObject _impactVFXPrefab;

    [Tooltip("Layers the bullet checks for impact. Defaults to Everything.")]
    [SerializeField] private LayerMask _impactLayers = ~0;

    [Header("Stop Behaviour")]
    [Tooltip("When the bullet stops (impact or max distance), freeze it and let the trail fade out " +
             "over TrailRenderer.time instead of destroying it instantly. Use for short-range tracers " +
             "(shotgun pellets) that would otherwise only be visible for a frame or two.")]
    [SerializeField] private bool _lingerTrailOnStop;

    [Tooltip("Optional child renderers (e.g. the bullet body mesh) hidden when the bullet stops and lingers.")]
    [SerializeField] private Renderer[] _hideOnStop;

    private Vector3 _direction;
    private bool _initialized;
    private float _remainingDistance = float.PositiveInfinity;

    /// <summary>
    /// Call immediately after instantiation to set the bullet in motion.
    /// </summary>
    /// <param name="spawnPosition">World-space muzzle position.</param>
    /// <param name="direction">World-space travel direction (does not need to be normalised).</param>
    public void Initialize(Vector3 spawnPosition, Vector3 direction)
        => Initialize(spawnPosition, direction, float.PositiveInfinity);

    /// <summary>
    /// Sets the bullet in motion and stops it after <paramref name="maxDistance"/> metres, even if
    /// nothing was hit (used to end shotgun pellet tracers where their hitscan ended).
    /// </summary>
    public void Initialize(Vector3 spawnPosition, Vector3 direction, float maxDistance)
    {
        transform.position = spawnPosition;
        _direction = direction.normalized;
        transform.forward = _direction;
        _remainingDistance = maxDistance > 0f ? maxDistance : float.PositiveInfinity;

        // Clear the trail so it doesn't streak from the prefab's parked position to the muzzle.
        GetComponent<TrailRenderer>().Clear();

        _initialized = true;
        Destroy(gameObject, _lifetime);
    }

    private void Update()
    {
        if (!_initialized) return;

        float stepDistance = Mathf.Min(_speed * Time.deltaTime, _remainingDistance);

        // Check for surfaces in the path before moving so we don't pass through thin geometry.
        // Perimeter fences are skipped so the tracer matches the hitscan, which passes through them.
        if (TryFindImpact(stepDistance, out RaycastHit hit))
        {
            transform.position = hit.point;
            SpawnImpact(hit.point, hit.normal);
            Stop();
            return;
        }

        transform.position += _direction * stepDistance;
        _remainingDistance -= stepDistance;

        if (_remainingDistance <= 0f)
            Stop();
    }

    private void Stop()
    {
        if (!_lingerTrailOnStop)
        {
            Destroy(gameObject);
            return;
        }

        _initialized = false;
        if (_hideOnStop != null)
        {
            foreach (Renderer r in _hideOnStop)
                if (r != null) r.enabled = false;
        }

        // The trail keeps rendering its existing points and fades them over its own time.
        Destroy(gameObject, GetComponent<TrailRenderer>().time);
    }

    private readonly RaycastHit[] _stepHits = new RaycastHit[8];

    private bool TryFindImpact(float stepDistance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(transform.position, _direction, _stepHits, stepDistance, _impactLayers, QueryTriggerInteraction.Ignore);

        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _stepHits[i];
            if (CombatHitUtility.IsBulletPassthrough(h.collider))
                continue;

            if (!found || h.distance < nearest.distance)
            {
                nearest = h;
                found = true;
            }
        }
        return found;
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
