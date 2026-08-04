using UnityEngine;

/// <summary>
/// Attach (via <see cref="Initialize"/> right after <c>AddComponent</c>) to a cosmetic,
/// physics-driven gore piece (e.g. <c>MutantEnemy</c>'s client-local death-burst debris) to
/// guard against it clipping through the floor and falling forever — which can happen when a
/// piece spawns underground (e.g. on uneven terrain) or gets launched at a bad angle. Every
/// frame, destroys the GameObject as soon as its Y position drops below <see cref="_minY"/>,
/// so a piece that fell out of the world is cleaned up instead of falling indefinitely.
///
/// Purely cosmetic and local — does not use Netcode. Networked gore (tracked as a
/// <see cref="JunkItem"/> for the Trash Task) uses its own server-only watchdog coroutine
/// instead (see <c>MutantEnemy.MonitorGoreJunkItem</c>) since that despawn must be
/// server-authoritative.
/// </summary>
public class GoreFallSafety : MonoBehaviour
{
    private float _minY;
    private bool _initialized;

    /// <summary>
    /// Configures this safety check. Must be called right after adding the component, since
    /// its field isn't serialized (the component is always added at runtime).
    /// </summary>
    /// <param name="minY">World-space Y below which this piece is considered lost and destroyed.</param>
    public void Initialize(float minY)
    {
        _minY = minY;
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized)
            return;

        if (transform.position.y < _minY)
            Destroy(gameObject);
    }
}
