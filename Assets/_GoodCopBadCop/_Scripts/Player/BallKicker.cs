using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to the Player. Detects CharacterController contact with a SoccerBall
/// and applies an impulse force via server RPC so all clients see the result.
///
/// Also kicks <see cref="KickablePhysicsBody"/> junk (gore, Vlad pieces, gut chunks): the owner
/// probes a small sphere at foot height while moving, and asks the server to kick anything it
/// touches. A probe (rather than OnControllerColliderHit) is used so kicks work regardless of
/// whether the collision matrix lets the CharacterController physically collide with junk layers.
/// </summary>
public class BallKicker : NetworkBehaviour
{
    [SerializeField] private float kickForce = 6f;
    [SerializeField] private float upwardKickFactor = 0.15f;

    /// <summary>Minimum time in seconds between kicks to the same ball.</summary>
    [SerializeField] private float kickCooldown = 0.25f;

    [Header("Physics Body Kicks (gore, corpses, junk)")]
    [Tooltip("Minimum horizontal player speed (m/s) for walking into a body to count as a kick.")]
    [SerializeField] private float bodyKickMinSpeed = 1.2f;
    [Tooltip("Height of the foot probe above the bottom of the CharacterController.")]
    [SerializeField] private float bodyKickProbeHeight = 0.3f;
    [Tooltip("Extra radius added to the CharacterController radius for the foot probe.")]
    [SerializeField] private float bodyKickProbeRadius = 0.2f;
    [Tooltip("Seconds before the same body can be kicked again by this player.")]
    [SerializeField] private float bodyKickCooldown = 0.35f;
    [Tooltip("Server sanity check: max distance between the kicker and the kick point.")]
    [SerializeField] private float bodyKickMaxServerDistance = 4f;

    private float _lastKickTime = float.MinValue;

    private CharacterController _controller;
    private readonly Collider[] _probeHits = new Collider[24];
    private readonly HashSet<KickablePhysicsBody> _probeSeen = new();

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

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (KickablePhysicsBody.ActiveCount == 0) return;

        if (_controller == null && !TryGetComponent(out _controller)) return;
        if (!_controller.enabled) return;

        Vector3 velocity = _controller.velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        if (speed < bodyKickMinSpeed) return;

        Vector3 moveDir = velocity / speed;
        Bounds bounds = _controller.bounds;
        Vector3 center = new Vector3(bounds.center.x, bounds.min.y + bodyKickProbeHeight, bounds.center.z)
                         + moveDir * (_controller.radius * 0.5f);
        float radius = _controller.radius + bodyKickProbeRadius;

        int count = Physics.OverlapSphereNonAlloc(center, radius, _probeHits, ~0, QueryTriggerInteraction.Ignore);
        if (count == 0) return;

        _probeSeen.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider col = _probeHits[i];
            if (col == null) continue;

            KickablePhysicsBody body = col.GetComponentInParent<KickablePhysicsBody>();
            if (body == null || !_probeSeen.Add(body)) continue;
            if (Time.time - body.LastLocalKickTime < bodyKickCooldown) continue;

            NetworkObject bodyNetObj = body.GetComponent<NetworkObject>();
            if (bodyNetObj == null || !bodyNetObj.IsSpawned) continue;

            body.LastLocalKickTime = Time.time;

            Vector3 point = col.ClosestPoint(center);
            Vector3 toBody = point - center;
            toBody.y = 0f;
            Vector3 dir = toBody.sqrMagnitude > 0.0001f
                ? (moveDir * 0.7f + toBody.normalized * 0.3f)
                : moveDir;

            KickBodyServerRpc(bodyNetObj, dir, point, speed);
        }
    }

    [Rpc(SendTo.Server)]
    private void KickBodyServerRpc(NetworkObjectReference target, Vector3 direction, Vector3 point, float playerSpeed)
    {
        if (!target.TryGet(out NetworkObject netObj)) return;

        KickablePhysicsBody body = netObj.GetComponent<KickablePhysicsBody>();
        if (body == null) return;

        if ((point - transform.position).sqrMagnitude > bodyKickMaxServerDistance * bodyKickMaxServerDistance)
            return;

        body.ApplyKickServer(direction, point, Mathf.Clamp(playerSpeed, 0f, 12f));
    }
}
