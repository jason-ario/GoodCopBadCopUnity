using UnityEngine;

public class RagdollController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody[] ragdollRigidbodies;
    [SerializeField] private Collider[] ragdollColliders;
    [SerializeField] Vector3 forceToApplyOnActivate;

    void Awake()
    {
        // Automatically find components if not assigned
        if (animator == null) animator = GetComponent<Animator>();
        if (ragdollRigidbodies == null || ragdollRigidbodies.Length == 0)
            ragdollRigidbodies = GetComponentsInChildren<Rigidbody>();
        if (ragdollColliders == null || ragdollColliders.Length == 0)
            ragdollColliders = GetComponentsInChildren<Collider>();

        SetRagdollActive(false);
    }

    public void SetRagdollActive(bool active)
    {
        // Disable/Enable the animator
        if (animator != null) animator.enabled = !active;

        // Toggle rigidbodies
        foreach (var rb in ragdollRigidbodies)
        {
            rb.isKinematic = !active;
        }

        // Toggle colliders (optional, depends if you want them always on)
        foreach (var col in ragdollColliders)
        {
            // Ignore the main controller collider if you have one on the root
            if (col.gameObject != gameObject)
            {
                col.enabled = active;
            }
        }
    }

    public void ActivateRagdollWithForce()
    {
        ActivateRagdollWithForce(forceToApplyOnActivate);
    }

    public void ActivateRagdollWithForce(Vector3 force, ForceMode mode = ForceMode.Impulse)
    {
        SetRagdollActive(true);

        // Apply force to the main body part (usually the Hips or Spine)
        if (ragdollRigidbodies.Length > 0)
        {
            ragdollRigidbodies[0].AddForce(force, mode);
        }
    }
}