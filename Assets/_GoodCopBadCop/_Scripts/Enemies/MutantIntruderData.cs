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
    [Tooltip("Seconds to wait after rotating into position before acting.")]
    [Min(0f)]
    public float preAttackPauseSeconds = 1f;

    [Tooltip("Seconds taken to move through the booth window into the player area.")]
    [Min(0.1f)]
    public float climbDurationSeconds = 3f;

    [Header("Shutter Bang")]
    [Tooltip("Number of times the mutant bangs on the closed shutter before giving up.")]
    [Min(1)]
    public int shutterBangCount = 5;

    [Tooltip("Seconds between each bang animation.")]
    [Min(0.1f)]
    public float bangIntervalSeconds = 1.5f;

    [Header("Animation Triggers")]
    [Tooltip("Animator trigger name played when climbing through the booth window.")]
    public string climbAnimationTrigger = "Climb";

    [Tooltip("Animator trigger name played for each shutter bang.")]
    public string bangAnimationTrigger = "Attack";
}
