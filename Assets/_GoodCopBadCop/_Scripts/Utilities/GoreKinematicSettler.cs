using System.Collections;
using UnityEngine;

/// <summary>
/// Attach (via <see cref="Initialize"/> right after <c>AddComponent</c>) to a cosmetic,
/// physics-driven gore piece to switch its <see cref="Rigidbody"/> to kinematic a short delay
/// after it becomes active — once it's had time to pop/fall/settle, there's no gameplay reason
/// left to keep simulating it, so this is a cheap perf win for death bursts that can spawn many
/// pieces at once (e.g. <c>MutantEnemy</c>'s death gore burst on Day 3 and every other day).
///
/// Purely cosmetic and local — does not use Netcode. Networked gore (tracked as a
/// <see cref="JunkItem"/> for the Trash Task) intentionally does NOT use this: its Rigidbody's
/// kinematic state is already managed by Netcode's <c>NetworkRigidbody</c>
/// (AutoUpdateKinematicState keeps every non-owner client's copy kinematic), so forcing it here
/// would fight that component's authority logic.
/// </summary>
public class GoreKinematicSettler : MonoBehaviour
{
    private Rigidbody _rigidbody;
    private float _delay;

    /// <summary>
    /// Configures this settler. Must be called right after adding the component, since its
    /// fields aren't serialized (the component is always added at runtime).
    /// </summary>
    /// <param name="rigidbody">The Rigidbody to switch to kinematic once <paramref name="delay"/> elapses.</param>
    /// <param name="delay">Seconds after activation to wait before switching to kinematic.</param>
    public void Initialize(Rigidbody rigidbody, float delay)
    {
        _rigidbody = rigidbody;
        _delay = delay;

        StartCoroutine(SettleAfterDelay());
    }

    private IEnumerator SettleAfterDelay()
    {
        if (_delay > 0f)
            yield return new WaitForSeconds(_delay);

        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
        }

        Destroy(this);
    }
}
