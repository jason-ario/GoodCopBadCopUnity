using System;
using UnityEngine;

/// <summary>
/// HUD task shown after the player reaches the trail destination and a pack of enemies spawns.
///
/// Registered on all clients via <see cref="FollowTrailThreat._killMutantCount"/> NetworkVariable.
/// The task description tracks how many enemies remain using the server-authoritative count that
/// propagates via the NetworkVariable. Purely a HUD display object — kill tracking itself is done
/// by <see cref="FollowTrailThreat"/>, which subscribes directly to the per-instance
/// <see cref="MutantEnemy.OnRemovedFromPlay"/> event of exactly the mutants IT spawned for this
/// pack (see <see cref="FollowTrailThreat.HandlePackMutantRemoved"/>). This class intentionally
/// does NOT listen to the global <see cref="MutantEnemy.OnAnyMutantKilled"/> event — that fires
/// for every mutant in the world (including the ambient population spawner), which would
/// incorrectly count kills unrelated to this task toward its completion.
/// Completion fires <see cref="OnKillMutantTaskCompleted"/> via <see cref="RaiseCompleted"/>,
/// called by <see cref="FollowTrailThreat"/> once every pack mutant it spawned is dead.
/// Day scripts (e.g. Day_02) subscribe to <see cref="OnKillMutantTaskCompleted"/> to advance the
/// night phase.
/// </summary>
public class KillMutantTask : ISystemicThreat
{
    private const string TaskName = "Kill the mutants";

    /// <summary>The currently active instance, or null if none exists.</summary>
    public static KillMutantTask Current { get; private set; }

    /// <summary>
    /// Fired on the server when all enemies in the pack are killed.
    /// Day scripts (e.g. Day_02) subscribe to this to advance the night phase.
    /// </summary>
    public static event Action OnKillMutantTaskCompleted;

    private int _killsRemaining;

    public string ThreatName => TaskName;

    public string ThreatDescription => _killsRemaining > 1
        ? $"{_killsRemaining} mutants remain"
        : _killsRemaining == 1
            ? "1 mutant remains"
            : "All mutants eliminated.";

    public float ThreatLevel  => 0f;
    public float ScoreWeight  => 0f;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new instance with <paramref name="killCount"/> enemies to eliminate,
    /// registers it with <see cref="TaskRegistry"/>.
    /// If an instance is already active, updates its count instead of creating a duplicate.
    /// Called on all clients via the <see cref="FollowTrailThreat"/> NetworkVariable callback.
    /// </summary>
    public static KillMutantTask CreateAndRegister(int killCount)
    {
        if (Current != null)
        {
            Current._killsRemaining = killCount;
            return Current;
        }

        Current = new KillMutantTask { _killsRemaining = killCount };
        TaskRegistry.Instance.AddThreat(Current);
        return Current;
    }

    /// <summary>
    /// Updates the displayed kill count on all clients without recreating the task.
    /// Called by the <see cref="FollowTrailThreat"/> NetworkVariable callback mid-combat.
    /// </summary>
    public static void UpdateCount(int killCount)
    {
        if (Current == null) return;
        Current._killsRemaining = killCount;
    }

    /// <summary>
    /// Removes the active instance from <see cref="TaskRegistry"/> and clears
    /// <see cref="Current"/>. Safe to call when no instance is active.
    /// Does NOT fire <see cref="OnKillMutantTaskCompleted"/> — see <see cref="RaiseCompleted"/>.
    /// </summary>
    public static void CompleteAndRemove()
    {
        if (Current == null) return;

        TaskRegistry.Instance.RemoveThreat(Current);
        Current = null;
    }

    /// <summary>
    /// Fires <see cref="OnKillMutantTaskCompleted"/>. Called by <see cref="FollowTrailThreat"/>
    /// (server-authoritative, since mutant deaths only ever resolve on the server) once every
    /// mutant it spawned for the pack has been permanently killed.
    /// </summary>
    public static void RaiseCompleted()
    {
        Debug.Log("[KillMutantTask] All pack mutants killed — task complete.");
        OnKillMutantTaskCompleted?.Invoke();
    }

    // ISystemicThreat — no server-driven behaviour needed.
    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
