using UnityEngine;

/// <summary>
/// Purely cosmetic bullet that travels in a straight line from the muzzle and
/// auto-destroys after a set lifetime. A TrailRenderer on the same GameObject
/// produces the visible streak. No collision or physics — this is visuals only.
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

    [Tooltip("Seconds before the GameObject is destroyed.")]
    [SerializeField] private float _lifetime = 3f;

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
        transform.position += _direction * (_speed * Time.deltaTime);
    }
}
