using System;
using UnityEngine;

/// <summary>
/// HUD task shown after the player reaches the trail destination and a pack of enemies spawns.
///
/// Registered on all clients via <see cref="FollowTrailThreat._killMutantCount"/> NetworkVariable.
/// The task description tracks how many enemies remain using the server-authoritative count that
/// propagates via the NetworkVariable. Completion fires <see cref="OnKillMutantTaskCompleted"/>
/// on the server (where kill events originate) and removes the task from all clients via the
/// NetworkVariable dropping to zero.
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
    /// registers it with <see cref="TaskRegistry"/>, and subscribes to kill events.
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
        MutantEnemy.OnAnyMutantKilled += Current.OnMutantKilled;
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
    /// Does NOT fire <see cref="OnKillMutantTaskCompleted"/> — that fires only from
    /// <see cref="OnMutantKilled"/> when the count legitimately reaches zero.
    /// </summary>
    public static void CompleteAndRemove()
    {
        if (Current == null) return;

        MutantEnemy.OnAnyMutantKilled -= Current.OnMutantKilled;
        TaskRegistry.Instance.RemoveThreat(Current);
        Current = null;
    }

    // ── Kill handling (server-side only) ──────────────────────────────────────

    private void OnMutantKilled()
    {
        // MutantEnemy.Die() is server-authoritative, so this only ever fires on the server.
        if (Current != this) return;

        _killsRemaining = Mathf.Max(0, _killsRemaining - 1);

        // Tell FollowTrailThreat to decrement its NetworkVariable so all clients
        // update their task description immediately via OnKillMutantCountChanged.
        FollowTrailThreat.Instance?.DecrementKillMutantCount();

        if (_killsRemaining > 0)
            return;

        // All enemies dead — unsubscribe first to prevent re-entry.
        MutantEnemy.OnAnyMutantKilled -= OnMutantKilled;

        Debug.Log("[KillMutantTask] All mutants killed — task complete.");

        // Fire completion event so day scripts can advance the night phase.
        // The NetworkVariable hitting zero will call CompleteAndRemove() on all clients.
        OnKillMutantTaskCompleted?.Invoke();
    }

    // ISystemicThreat — no server-driven behaviour needed.
    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
