using UnityEngine;

/// <summary>
/// Destroys the GameObject after a specified delay.
/// When <see cref="waitForParticles"/> is true the delay is extended until every
/// <see cref="ParticleSystem"/> in the hierarchy has finished playing, so the
/// object is never culled while a particle effect is still visible.
/// </summary>
public class AutoDestroy : MonoBehaviour
{
    [Tooltip("Minimum seconds before the GameObject is destroyed.")]
    [Min(0f)]
    [SerializeField] private float delay = 5f;

    [Tooltip("When true, waits for all child ParticleSystems to finish before destroying.")]
    [SerializeField] private bool waitForParticles = true;

    private ParticleSystem[] _particles;
    private float _elapsed;

    private void Start()
    {
        _particles = waitForParticles ? GetComponentsInChildren<ParticleSystem>(true) : System.Array.Empty<ParticleSystem>();
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (_elapsed < delay)
            return;

        if (waitForParticles && IsAnyParticlePlaying())
            return;

        Destroy(gameObject);
    }

    private bool IsAnyParticlePlaying()
    {
        foreach (ParticleSystem ps in _particles)
        {
            if (ps != null && ps.IsAlive(true))
                return true;
        }

        return false;
    }
}
