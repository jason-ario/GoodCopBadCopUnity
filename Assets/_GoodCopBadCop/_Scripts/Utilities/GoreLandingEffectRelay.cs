using System;
using UnityEngine;

/// <summary>
/// Server-authoritative counterpart to <see cref="GoreLandingDecalSpawner"/>: detects the first
/// collision with a Collider on <see cref="Initialize"/>'s ground layer and invokes a callback
/// with the contact point/normal, then removes itself.
///
/// Use this (instead of <see cref="GoreLandingDecalSpawner"/>) for a single networked,
/// server-authoritative physics piece (e.g. Netcode's <c>NetworkRigidbody</c> with
/// AutoUpdateKinematicState, which keeps every non-server client's copy kinematic) — only the
/// server's copy ever goes non-kinematic, so only the server ever receives collision callbacks.
/// The callback is expected to broadcast the landing effects (decal/particle/sound) to every
/// client via RPC, since clients can't detect the landing themselves.
/// </summary>
public class GoreLandingEffectRelay : MonoBehaviour
{
    private LayerMask _groundLayer;
    private Action<Vector3, Vector3> _onLanded;
    private bool _hasLanded;

    /// <summary>
    /// Configures this relay. Must be called right after adding the component, since its
    /// fields aren't serialized (the component is always added at runtime).
    /// </summary>
    public void Initialize(LayerMask groundLayer, Action<Vector3, Vector3> onLanded)
    {
        _groundLayer = groundLayer;
        _onLanded = onLanded;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasLanded)
            return;

        if (((1 << collision.gameObject.layer) & _groundLayer.value) == 0)
            return;

        _hasLanded = true;

        ContactPoint contact = collision.GetContact(0);
        _onLanded?.Invoke(contact.point, contact.normal);

        Destroy(this);
    }
}
