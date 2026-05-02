using UnityEngine;

public class FootstepsAudio : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;

    [Tooltip("Clips played when the player is outside the booth.")]
    public AudioClip[] outsideFootstepClips;

    [Tooltip("Clips played when the player is inside the booth.")]
    public AudioClip[] insideFootstepClips;

    [Header("Pitch Variation")]
    [Range(0f, 0.5f)]
    public float pitchRandomness = 0.1f;

    [Header("Step Intervals")]
    [Tooltip("Seconds between footstep sounds while walking.")]
    public float walkStepInterval = 0.5f;

    [Tooltip("Seconds between footstep sounds while running.")]
    public float runStepInterval = 0.3f;

    [Header("References")]
    [Tooltip("The PlayerMovementController used to read movement state.")]
    public PlayerMovementController movementController;

    public bool disable;

    private float _stepTimer;
    private CharacterController _characterController;

    private void Awake()
    {
        if (movementController != null)
            _characterController = movementController.GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (disable || movementController == null)
            return;

        // Only the local owner drives the timer — the RPC handles remote playback.
        if (!movementController.IsOwner)
            return;

        bool isMoving = movementController.MoveXRaw != 0f || movementController.MoveZRaw != 0f;

        bool isGrounded = _characterController != null && _characterController.isGrounded;

        if (!isMoving || !isGrounded)
        {
            _stepTimer = 0f;
            return;
        }

        float interval = movementController.IsRunning ? runStepInterval : walkStepInterval;

        _stepTimer += Time.deltaTime;

        if (_stepTimer >= interval)
        {
            _stepTimer = 0f;
            movementController.PlayFootstepNetworked();
        }
    }

    /// <summary>
    /// Plays a random footstep clip chosen from the appropriate set based on whether
    /// the player is currently inside or outside the booth.
    /// </summary>
    public void PlayFootstep()
    {
        if (disable)
            return;

        if (audioSource == null)
            return;

        AudioClip[] clips = ResolveClipSet();

        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];

        audioSource.pitch = 1f + Random.Range(-pitchRandomness, pitchRandomness);
        audioSource.PlayOneShot(clip);
    }

    private AudioClip[] ResolveClipSet()
    {
        bool isOutside = PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutside;
        return isOutside ? outsideFootstepClips : insideFootstepClips;
    }
}
