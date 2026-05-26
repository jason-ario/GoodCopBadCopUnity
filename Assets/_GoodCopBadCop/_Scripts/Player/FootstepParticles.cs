using UnityEngine;

/// <summary>
/// Emits a particle burst at the player's feet each time a running footstep fires.
/// Attach to the same GameObject as FootstepsAudio and call EmitRunStep() from there.
/// </summary>
[RequireComponent(typeof(FootstepsAudio))]
public class FootstepParticles : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The ParticleSystem positioned at the player's feet.")]
    [SerializeField] private ParticleSystem footstepParticleSystem;

    [Header("Settings")]
    [Tooltip("Number of particles emitted per footstep.")]
    [SerializeField] private int particlesPerStep = 8;

    /// <summary>
    /// Emits a burst of particles. Should only be called when the player is running.
    /// </summary>
    public void EmitRunStep()
    {
        if (footstepParticleSystem == null)
            return;

        footstepParticleSystem.Emit(particlesPerStep);
    }
}
