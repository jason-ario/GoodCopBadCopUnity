using UnityEngine;

public class AddForceOnAwake : MonoBehaviour
{
    [Header("Smash Settings")]
    [Tooltip("The world-space origin of the smash. If not set, defaults to this object's position.")]
    public Transform smashSource;

    [Tooltip("The general direction all shards should fly towards.")]
    public Vector3 smashDirection = Vector3.forward;

    [Header("Force")]
    public float minForce = 5f;
    public float maxForce = 15f;

    [Header("Randomness")]
    [Range(0f, 1f)]
    [Tooltip("0 = shards fly exactly along smash direction, 1 = completely random directions.")]
    public float randomness = 0.3f;

    [Header("Upward Bias")]
    [Tooltip("Extra upward force applied to each shard.")]
    public float upwardBias = 2f;

    [Header("Torque")]
    public float maxTorque = 10f;

    private Rigidbody[] rigidbodies;

    void Awake()
    {
        rigidbodies = GetComponentsInChildren<Rigidbody>();
    }

    void Start()
    {
        Vector3 direction = smashDirection.normalized;

        foreach (Rigidbody rb in rigidbodies)
        {
            // Blend the main smash direction with a random direction
            Vector3 randomDir = Random.onUnitSphere;
            Vector3 finalDir = Vector3.Slerp(direction, randomDir, randomness).normalized;

            // Add a little upward bias so shards arc into the air
            finalDir += Vector3.up * upwardBias;
            finalDir.Normalize();

            float force = Random.Range(minForce, maxForce);
            rb.AddForce(finalDir * force, ForceMode.Impulse);

            // Random spin for a more dramatic look
            Vector3 torque = Random.insideUnitSphere * maxTorque;
            rb.AddTorque(torque, ForceMode.Impulse);
        }
    }
}
