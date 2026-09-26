using UnityEngine;

/// <summary>
/// Makes physics-driven gore pieces (e.g. the Gut junk prefabs) feel heavy and wet: they fall
/// faster than default gravity and stop rolling almost immediately once they hit the ground.
///
/// Only acts while the <see cref="Rigidbody"/> is simulated (non-kinematic), so it never fights
/// Netcode's NetworkRigidbody (non-authority copies are kinematic) or PickableObject holding.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GoreSettlePhysics : MonoBehaviour
{
    [Tooltip("Additional gravity applied on top of Physics.gravity (1 = double gravity).")]
    [SerializeField, Min(0f)] private float extraGravityMultiplier = 1.5f;

    [Tooltip("Fraction of spin kept on each ground impact (0 = stop spinning instantly).")]
    [SerializeField, Range(0f, 1f)] private float impactSpinRetained = 0.1f;

    [Tooltip("Fraction of horizontal speed kept on each ground impact.")]
    [SerializeField, Range(0f, 1f)] private float impactSlideRetained = 0.35f;

    [Tooltip("Angular damping used after the piece has touched the ground, to kill rolling.")]
    [SerializeField, Min(0f)] private float groundedAngularDamping = 10f;

    [Tooltip("Linear damping used after the piece has touched the ground, to kill sliding.")]
    [SerializeField, Min(0f)] private float groundedLinearDamping = 2f;

    [Tooltip("Contacts with a normal at least this upward count as ground.")]
    [SerializeField, Range(0f, 1f)] private float groundNormalMinY = 0.5f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_rb.isKinematic || _rb.IsSleeping() || extraGravityMultiplier <= 0f)
            return;

        _rb.AddForce(Physics.gravity * extraGravityMultiplier, ForceMode.Acceleration);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_rb.isKinematic || !IsGroundContact(collision))
            return;

        _rb.angularVelocity *= impactSpinRetained;

        Vector3 v = _rb.linearVelocity;
        _rb.linearVelocity = new Vector3(v.x * impactSlideRetained, v.y, v.z * impactSlideRetained);

        _rb.angularDamping = groundedAngularDamping;
        _rb.linearDamping = groundedLinearDamping;
    }

    private bool IsGroundContact(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y >= groundNormalMinY)
                return true;
        }
        return false;
    }
}
