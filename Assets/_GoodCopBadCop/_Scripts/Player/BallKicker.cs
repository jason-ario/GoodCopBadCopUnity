using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to the Player. Detects CharacterController contact with a SoccerBall
/// and applies an impulse force via server RPC so all clients see the result.
/// </summary>
public class BallKicker : NetworkBehaviour
{
    [SerializeField] private float kickForce = 6f;
    [SerializeField] private float upwardKickFactor = 0.15f;

    /// <summary>Minimum time in seconds between kicks to the same ball.</summary>
    [SerializeField] private float kickCooldown = 0.25f;

    private float _lastKickTime = float.MinValue;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!IsOwner) return;
        if (Time.time - _lastKickTime < kickCooldown) return;

        SoccerBall ball = hit.collider.GetComponent<SoccerBall>();
        if (ball == null) return;

        _lastKickTime = Time.time;

        // Flat XZ direction from player to ball — gives a reliable "away from player" vector
        Vector3 toBall = hit.collider.transform.position - transform.position;
        toBall.y = 0f;
        if (toBall.sqrMagnitude < 0.0001f)
            toBall = transform.forward; // fallback for exact overlap
        toBall.Normalize();

        // Add a small upward component so the ball hops slightly, then normalize
        Vector3 kickDir = (toBall + Vector3.up * upwardKickFactor).normalized;

        // Force is purely proportional to how fast the player is moving — no artificial minimum
        float speed = hit.controller.velocity.magnitude;
        ball.RequestKick(kickDir * kickForce * speed);
        hit.collider.GetComponent<CreepyKickReaction>()?.OnKick(transform.position);
    }
}
