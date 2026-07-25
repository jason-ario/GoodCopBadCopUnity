using UnityEngine;

/// <summary>
/// Attach (typically via <see cref="Initialize"/> right after <c>AddComponent</c>) to a
/// physics-driven gore piece that pops out with a Rigidbody velocity. On the first collision
/// with a Collider on <see cref="_groundLayer"/>, spawns a random blood-decal prefab at the
/// contact point, oriented with its forward axis facing down into the ground surface (via
/// <see cref="BloodDecalUtility"/>), then removes itself so it never spawns more than once.
///
/// Purely cosmetic and local — does not use Netcode. Intended for client-local, non-networked
/// gore pieces (e.g. <c>MutantEnemy</c>'s cosmetic gore bursts) where each client simulates its
/// own physics and a perfectly-synced decal isn't required.
/// </summary>
public class GoreLandingDecalSpawner : MonoBehaviour
{
    private GameObject[] _decalPrefabs;
    private LayerMask _groundLayer;
    private float _decalLifetime;
    private bool _hasLanded;

    /// <summary>
    /// Configures this spawner. Must be called right after adding the component, since its
    /// fields aren't serialized (the component is always added at runtime).
    /// </summary>
    public void Initialize(GameObject[] decalPrefabs, LayerMask groundLayer, float decalLifetime)
    {
        _decalPrefabs = decalPrefabs;
        _groundLayer = groundLayer;
        _decalLifetime = decalLifetime;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasLanded)
            return;

        if (((1 << collision.gameObject.layer) & _groundLayer.value) == 0)
            return;

        _hasLanded = true;

        if (_decalPrefabs != null && _decalPrefabs.Length > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            SpawnDecal(contact.point, contact.normal);
        }

        Destroy(this);
    }

    private void SpawnDecal(Vector3 position, Vector3 normal)
    {
        GameObject prefab = _decalPrefabs[Random.Range(0, _decalPrefabs.Length)];
        if (prefab == null)
            return;

        Quaternion rotation = BloodDecalUtility.GetGroundDecalRotation(normal);
        GameObject decal = Instantiate(prefab, position, rotation);

        if (_decalLifetime > 0f)
            Destroy(decal, _decalLifetime);
    }
}
