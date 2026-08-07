using UnityEngine;

/// <summary>
/// Attach (typically via <see cref="Initialize"/> right after <c>AddComponent</c>) to a
/// physics-driven gore piece that pops out with a Rigidbody velocity. On the first collision
/// with a Collider on <see cref="_groundLayer"/>, spawns a random blood-decal prefab at the
/// contact point, oriented with its forward axis facing down into the ground surface (via
/// <see cref="BloodDecalUtility"/>), then spawns an aligned cosmetic blood-particle effect at the
/// same position/rotation before removing itself so it never spawns more than once.
///
/// Purely cosmetic and local — does not use Netcode. Intended for client-local, non-networked
/// gore pieces (e.g. <c>MutantEnemy</c>'s cosmetic gore bursts) where each client simulates its
/// own physics and a perfectly-synced decal isn't required.
///
/// Also plays a "splat" impact sound (via <see cref="SFXController"/>, spatialized at the
/// contact point) on landing, independent of whether a decal prefab is assigned — so this can
/// be reused purely for the landing sound (e.g. gore hitting the ground in general) even when
/// no decal is configured.
/// </summary>
public class GoreLandingDecalSpawner : MonoBehaviour
{
    private GameObject[] _decalPrefabs;
    private LayerMask _groundLayer;
    private float _decalLifetime;
    private GameObject _particlePrefab;
    private float _particleLifetime;
    private AudioClip _landingSound;
    private bool _hasLanded;

    /// <summary>
    /// Configures this spawner. Must be called right after adding the component, since its
    /// fields aren't serialized (the component is always added at runtime).
    /// </summary>
    public void Initialize(GameObject[] decalPrefabs, LayerMask groundLayer, float decalLifetime, GameObject particlePrefab, float particleLifetime, AudioClip landingSound = null)
    {
        _decalPrefabs = decalPrefabs;
        _groundLayer = groundLayer;
        _decalLifetime = decalLifetime;
        _particlePrefab = particlePrefab;
        _particleLifetime = particleLifetime;
        _landingSound = landingSound;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasLanded)
            return;

        if (((1 << collision.gameObject.layer) & _groundLayer.value) == 0)
            return;

        _hasLanded = true;

        ContactPoint contact = collision.GetContact(0);

        if (_decalPrefabs != null && _decalPrefabs.Length > 0)
            SpawnDecal(contact.point, contact.normal);

        if (_landingSound != null)
            SFXController.Instance?.PlayAtPosition(_landingSound, contact.point);

        Destroy(this);
    }

    private void SpawnDecal(Vector3 position, Vector3 normal)
    {
        GameObject prefab = _decalPrefabs[Random.Range(0, _decalPrefabs.Length)];
        if (prefab == null)
            return;

        // TODO: BloodDecalUtility.GetGroundDecalRotation(normal) was producing incorrect
        // orientations on landing; forcing identity rotation for now until that's fixed.
        Quaternion rotation = Quaternion.identity;
        GameObject decal = Instantiate(prefab, position, rotation);

        if (_decalLifetime > 0f)
            Destroy(decal, _decalLifetime);

        BloodDecalUtility.SpawnAlignedParticle(_particlePrefab, position, rotation, _particleLifetime);
    }
}
