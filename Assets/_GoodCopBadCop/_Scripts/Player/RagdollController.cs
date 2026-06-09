using UnityEngine;
using UnityEngine.Rendering;

public class RagdollController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody[] ragdollRigidbodies;
    [SerializeField] private Collider[] ragdollColliders;
    [SerializeField] private Vector3 forceToApplyOnActivate;
    [SerializeField] private bool activateOnAwake = false;

    [Header("Death Visuals")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private GameObject bodyArmsMesh;

    private PlayerHealth _playerHealth;
    private CharacterController _characterController;

    private const int DefaultLayer = 0;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (ragdollRigidbodies == null || ragdollRigidbodies.Length == 0)
            ragdollRigidbodies = GetComponentsInChildren<Rigidbody>();
        if (ragdollColliders == null || ragdollColliders.Length == 0)
            ragdollColliders = GetComponentsInChildren<Collider>();

        _characterController = GetComponent<CharacterController>();
        _playerHealth = GetComponent<PlayerHealth>();
        if (_playerHealth != null)
            _playerHealth.OnDeath += OnDeath;

        SetRagdollActive(activateOnAwake);
    }

    private void OnDestroy()
    {
        if (_playerHealth != null)
            _playerHealth.OnDeath -= OnDeath;
    }

    private void OnDeath()
    {
        if (_characterController != null)
            _characterController.enabled = false;

        foreach (var rb in ragdollRigidbodies)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SetRagdollActive(true);

        if (headTransform != null)
            headTransform.localScale = Vector3.one;

        if (bodyArmsMesh != null)
        {
            bodyArmsMesh.layer = DefaultLayer;

            if (bodyArmsMesh.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                smr.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    [ContextMenu("Activate")]
    public void ActivateRagdoll()
    {
        SetRagdollActive(true);
    }

    /// <summary>Enables or disables ragdoll physics, toggling the animator and rigidbody/collider states.</summary>
    public void SetRagdollActive(bool active)
    {
        if (animator != null) animator.enabled = !active;

        foreach (var rb in ragdollRigidbodies)
            rb.isKinematic = !active;

        foreach (var col in ragdollColliders)
        {
            if (col.gameObject != gameObject)
                col.enabled = active;
        }
    }

    /// <summary>Activates the ragdoll and applies the pre-configured force.</summary>
    public void ActivateRagdollWithForce()
    {
        ActivateRagdollWithForce(forceToApplyOnActivate);
    }

    /// <summary>Activates the ragdoll and applies a custom force to the root rigidbody.</summary>
    public void ActivateRagdollWithForce(Vector3 force, ForceMode mode = ForceMode.Impulse)
    {
        SetRagdollActive(true);

        if (ragdollRigidbodies.Length > 0)
            ragdollRigidbodies[0].AddForce(force, mode);
    }
}
