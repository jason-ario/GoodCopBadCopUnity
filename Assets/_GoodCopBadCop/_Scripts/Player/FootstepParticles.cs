using UnityEngine;

/// <summary>
/// Emits a particle burst at the forward foot's position each time a footstep fires.
/// "Forward foot" is whichever foot bone projects further along the player's current
/// world-space movement direction. The burst is placed at the foot bone's XZ position
/// snapped to the player's root Y so particles always spawn at ground level.
/// </summary>
[RequireComponent(typeof(FootstepsAudio))]
public class FootstepParticles : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The ParticleSystem used to emit footstep bursts. Its Transform will be repositioned per step.")]
    [SerializeField] private ParticleSystem footstepParticleSystem;

    [Header("Settings")]
    [Tooltip("Number of particles emitted per footstep while walking.")]
    [SerializeField] private int walkParticlesPerStep = 4;

    [Tooltip("Number of particles emitted per footstep while running.")]
    [SerializeField] private int runParticlesPerStep = 8;


    // Cached references resolved once on Awake.
    private PlayerAnimationController _animController;
    private PlayerMovementController _movementController;

    // Foot bones resolved after the animator has had one frame to evaluate.
    private Transform _leftFootBone;
    private Transform _rightFootBone;
    private bool _bonesResolved;

    private void Awake()
    {
        _animController    = GetComponent<PlayerAnimationController>();
        _movementController = GetComponent<PlayerMovementController>();
    }

    private void Start()
    {
        // Delay one frame so the Animator has evaluated and bone positions are valid.
        StartCoroutine(ResolveBonesCR());
    }

    private System.Collections.IEnumerator ResolveBonesCR()
    {
        yield return null;

        if (_animController != null)
        {
            _leftFootBone  = _animController.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFootBone = _animController.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        _bonesResolved = true;

        if (_leftFootBone == null || _rightFootBone == null)
        {
            Debug.LogWarning("[FootstepParticles] Could not resolve foot bones from the body animator. " +
                             "Particles will fall back to emitting at the player's root position.", this);
        }
    }

    /// <summary>
    /// Emits a burst of particles at the forward foot, scaled to the player's movement speed.
    /// Pass true for running, false for walking.
    /// </summary>
    public void EmitStep(bool isRunning)
    {
        if (footstepParticleSystem == null)
            return;

        Vector3 emitPosition = ResolveEmitPosition();
        footstepParticleSystem.transform.position = emitPosition;

        int count = isRunning ? runParticlesPerStep : walkParticlesPerStep;
        footstepParticleSystem.Emit(count);
    }

    /// <summary>
    /// Emits a running footstep burst. Kept for backwards compatibility.
    /// </summary>
    public void EmitRunStep() => EmitStep(true);

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Picks the forward foot based on the movement direction, then places the
    /// emit position at the foot bone's XZ projected onto the player's root Y (ground level).
    /// </summary>
    private Vector3 ResolveEmitPosition()
    {
        Transform chosenFoot = PickForwardFoot();

        if (chosenFoot == null)
            return transform.position;

        return new Vector3(chosenFoot.position.x, transform.position.y, chosenFoot.position.z);
    }

    /// <summary>
    /// Returns whichever foot bone projects further along the player's current
    /// world-space movement direction. Falls back to null when bones are unresolved.
    /// </summary>
    private Transform PickForwardFoot()
    {
        if (!_bonesResolved || _leftFootBone == null || _rightFootBone == null)
            return null;

        // Build world-space movement direction from raw input axes.
        Vector3 moveDir = Vector3.zero;
        if (_movementController != null)
        {
            float x = _movementController.MoveXRaw;
            float z = _movementController.MoveZRaw;
            // Transform local input direction to world space using the player's facing.
            moveDir = transform.TransformDirection(new Vector3(x, 0f, z));
        }

        // If there's no meaningful input direction, default to the player's forward.
        if (moveDir.sqrMagnitude < 0.01f)
            moveDir = transform.forward;

        moveDir.Normalize();

        float leftDot  = Vector3.Dot(_leftFootBone.position  - transform.position, moveDir);
        float rightDot = Vector3.Dot(_rightFootBone.position - transform.position, moveDir);

        return leftDot >= rightDot ? _leftFootBone : _rightFootBone;
    }
}
