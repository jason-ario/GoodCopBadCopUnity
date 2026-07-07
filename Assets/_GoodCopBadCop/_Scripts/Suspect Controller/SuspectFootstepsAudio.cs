using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Plays footstep audio for a Suspect character driven by a NavMeshAgent.
/// Fires a footstep sound on a fixed timer whenever the agent is moving,
/// choosing between inside or outside clip sets based on <see cref="IsOutside"/>.
/// Mirrors the inside/outside clip-set pattern used by <see cref="FootstepsAudio"/>
/// on the player prefab.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class SuspectFootstepsAudio : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Clips played when the suspect is outside.")]
    [SerializeField] private AudioClip[] outsideFootstepClips;

    [Tooltip("Clips played when the suspect is inside.")]
    [SerializeField] private AudioClip[] insideFootstepClips;

    [Header("Pitch Variation")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pitchRandomness = 0.1f;

    [Header("Step Interval")]
    [Tooltip("Seconds between footstep sounds while walking.")]
    [SerializeField] private float walkStepInterval = 0.5f;

    [Header("Movement Detection")]
    [Tooltip("Minimum NavMeshAgent speed (m/s) required to trigger footsteps.")]
    [SerializeField] private float movementThreshold = 0.1f;

    /// <summary>
    /// Set to true when the suspect is outdoors, false when indoors.
    /// Controls which audio clip set is used for footsteps.
    /// Defaults to true since suspects begin outside the booth.
    /// </summary>
    public bool IsOutside = true;

    private NavMeshAgent _navAgent;
    private float _stepTimer;

    private void Awake()
    {
        _navAgent = GetComponent<NavMeshAgent>();
    }

    private void Update()
    {
        if (_navAgent == null || !_navAgent.enabled || !_navAgent.isOnNavMesh)
        {
            _stepTimer = 0f;
            return;
        }

        bool isMoving = _navAgent.velocity.sqrMagnitude > movementThreshold * movementThreshold;

        if (!isMoving)
        {
            _stepTimer = 0f;
            return;
        }

        _stepTimer += Time.deltaTime;

        if (_stepTimer >= walkStepInterval)
        {
            _stepTimer = 0f;
            PlayFootstep();
        }
    }

    private void PlayFootstep()
    {
        if (audioSource == null)
            return;

        AudioClip[] clips = IsOutside ? outsideFootstepClips : insideFootstepClips;

        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        audioSource.pitch = 1f + Random.Range(-pitchRandomness, pitchRandomness);
        audioSource.PlayOneShot(clip);
    }
}
