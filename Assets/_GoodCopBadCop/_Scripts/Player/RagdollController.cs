using Unity.Netcode;
using Unity.Netcode.Components;
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
    [SerializeField] private GameObject firstPersonArms;

    [Header("Underwater Settings")]
    [Tooltip("Linear damping applied to all ragdoll Rigidbodies when inside an underwater zone.")]
    [SerializeField] private float underwaterLinearDamping = 4f;
    [Tooltip("Angular damping applied to all ragdoll Rigidbodies when inside an underwater zone.")]
    [SerializeField] private float underwaterAngularDamping = 4f;

    private PlayerHealth _playerHealth;
    private CharacterController _characterController;
    private NetworkTransform _networkTransform;
    private NetworkAnimator _networkAnimator;
    private PlayerAnimationController _playerAnimationController;
    private NetworkObject _networkObject;

    private float[] _originalLinearDamping;
    private float[] _originalAngularDamping;
    private bool _isUnderwater;

    private const int DefaultLayer = 0;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (ragdollRigidbodies == null || ragdollRigidbodies.Length == 0)
            ragdollRigidbodies = GetComponentsInChildren<Rigidbody>();
        if (ragdollColliders == null || ragdollColliders.Length == 0)
            ragdollColliders = GetComponentsInChildren<Collider>();

        _characterController = GetComponent<CharacterController>();
        _networkTransform = GetComponent<NetworkTransform>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        _networkObject = GetComponent<NetworkObject>();
        _playerHealth = GetComponent<PlayerHealth>();
        if (_playerHealth != null)
            _playerHealth.OnDeath += OnDeath;

        // Cache original damping values so they can be restored on zone exit
        _originalLinearDamping = new float[ragdollRigidbodies.Length];
        _originalAngularDamping = new float[ragdollRigidbodies.Length];
        for (int i = 0; i < ragdollRigidbodies.Length; i++)
        {
            if (ragdollRigidbodies[i] == null) continue;
            _originalLinearDamping[i] = ragdollRigidbodies[i].linearDamping;
            _originalAngularDamping[i] = ragdollRigidbodies[i].angularDamping;
        }

        SetRagdollActive(activateOnAwake);
    }

    private void OnEnable()
    {
        GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnUnderwaterStateChanged += HandleUnderwaterStateChanged;
    }

    private void OnDisable()
    {
        GoodCopBadCop.EnvironmentSystem.UnderwaterZone.OnUnderwaterStateChanged -= HandleUnderwaterStateChanged;
    }

    private void OnDestroy()
    {
        if (_playerHealth != null)
            _playerHealth.OnDeath -= OnDeath;
    }

    private void HandleUnderwaterStateChanged(bool isUnderwater)
    {
        // Only apply to the local player's ragdoll
        if (_networkObject != null && !_networkObject.IsOwner)
            return;

        _isUnderwater = isUnderwater;
        ApplyUnderwaterPhysics(isUnderwater);
    }

    private void ApplyUnderwaterPhysics(bool underwater)
    {
        for (int i = 0; i < ragdollRigidbodies.Length; i++)
        {
            if (ragdollRigidbodies[i] == null) continue;
            ragdollRigidbodies[i].linearDamping  = underwater ? underwaterLinearDamping  : _originalLinearDamping[i];
            ragdollRigidbodies[i].angularDamping = underwater ? underwaterAngularDamping : _originalAngularDamping[i];
        }
    }

    private void OnDeath()
    {
        ActivateRagdoll();

        if (headTransform != null)
            headTransform.localScale = Vector3.one;

        if (bodyArmsMesh != null)
        {
            bodyArmsMesh.layer = DefaultLayer;

            if (bodyArmsMesh.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                smr.shadowCastingMode = ShadowCastingMode.On;
        }

        if (firstPersonArms != null)
            firstPersonArms.SetActive(false);
    }

    [ContextMenu("Activate")]
    public void ActivateRagdoll()
    {
        if (_characterController != null)
            _characterController.enabled = false;

        // Stop NetworkTransform from overwriting the root position with
        // stale server updates, which would drag the ragdoll across the ground.
        if (_networkTransform != null)
            _networkTransform.enabled = false;

        // Stop NetworkAnimator from pushing stale animation state to proxy bones
        // while ragdoll physics is trying to take over.
        if (_networkAnimator != null)
            _networkAnimator.enabled = false;

        // Stop PlayerAnimationController from writing bone transforms in LateUpdate,
        // which would fight ragdoll physics every frame and cause violent shaking.
        if (_playerAnimationController != null)
            _playerAnimationController.enabled = false;

        SetRagdollActive(true);

        // Zero inherited velocity from the CharacterController's positional delta,
        // then explicitly wake each rigidbody so gravity begins immediately on all clients.
        foreach (var rb in ragdollRigidbodies)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }

        // If already inside an underwater zone, apply buoyancy damping immediately
        if (_isUnderwater)
            ApplyUnderwaterPhysics(true);
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
