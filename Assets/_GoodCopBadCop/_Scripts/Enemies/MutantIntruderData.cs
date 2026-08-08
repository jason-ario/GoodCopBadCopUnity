using UnityEngine;

/// <summary>
/// Behaviour config for a mutant appearing in the suspect lineup.
/// Controls timing, animation triggers, and shutter-bang parameters.
/// The mutant prefab to spawn is drawn separately from MutantLineupSet on DailySuspectManager,
/// keeping pool and config concerns distinct (mirrors the SuspectData / SuspectSet split).
/// </summary>
[CreateAssetMenu(menuName = "Scriptable Objects/Mutant Intruder Data")]
public class MutantIntruderData : ScriptableObject
{
    [Header("Timing")]
    [Tooltip("Seconds taken to walk from the spawn point to the booth stand position. Should match the suspect walk-in duration (default 3).")]
    [Min(0.1f)]
    public float walkInDurationSeconds = 3f;

    [Tooltip("Seconds to wait after rotating into position before acting.")]
    [Min(0f)]
    public float preAttackPauseSeconds = 1f;

    [Tooltip("Seconds taken to move through the booth window into the player area.")]
    [Min(0.1f)]
    public float climbDurationSeconds = 3f;

    [Header("Shutter Bang")]
    [Tooltip("Number of times the mutant bangs on the closed shutter before giving up. Only used when canClimb is true.")]
    [Min(1)]
    public int shutterBangCount = 9;

    [Tooltip("Seconds after which a non-climbing mutant loses interest and walks away. Only used when canClimb is false.")]
    [Min(1f)]
    public float losesInterestAfterSeconds = 18f;

    [Tooltip("Seconds the Attack bool stays true during each shutter hit. Should be shorter than bangIntervalSeconds.")]
    [Min(0.1f)]
    public float attackAnimDurationSeconds = 1f;

    [Tooltip("Seconds between the start of one attack and the start of the next.")]
    [Min(0.1f)]
    public float bangIntervalSeconds = 1.5f;

    [Header("Retreat")]
    [Tooltip("NavMeshAgent speed when sprinting away after losing interest. Should be noticeably faster than walk-in speed.")]
    [Min(1f)]
    public float retreatSpeed = 12f;

    [Tooltip("Hard deadline in seconds from the start of the retreat before the mutant is force-despawned, regardless of distance to the despawn point.")]
    [Min(0.5f)]
    public float retreatDespawnTimeout = 2f;

    [Header("Animation Triggers")]
    [Tooltip("Animator trigger name played when climbing through the booth window.")]
    public string climbAnimationTrigger = "Climb";
}
