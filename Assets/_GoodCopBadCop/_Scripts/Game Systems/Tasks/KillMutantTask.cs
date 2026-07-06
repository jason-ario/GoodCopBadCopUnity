using System;
using UnityEngine;

/// <summary>
/// HUD task shown after the player reaches the trail destination on days that use the
/// post-shift Vlad Out-Back / Follow Trail sequence.
///
/// Registered on all clients via <see cref="FollowTrailThreat._killMutantActive"/> NetworkVariable.
/// Removed (and the night phase advanced) when any mutant is killed while this task is active,
/// detected via <see cref="MutantEnemy.OnAnyMutantKilled"/> (fires server-side only). The kill
/// handler tells <see cref="FollowTrailThreat"/> to clear its NetworkVariable, which propagates
/// the task removal to all clients.
/// </summary>
public class KillMutantTask : ISystemicThreat
{
    private const string TaskDisplayName = "Kill the mutant";
    private const string TaskDisplayDesc = "Something followed the trail";

    /// <summary>The currently active instance, or null if none exists.</summary>
    public static KillMutantTask Current { get; private set; }

    /// <summary>
    /// Fired on the server when the active <see cref="KillMutantTask"/> is completed by a mutant kill.
    /// Subscribers (e.g. Day_02) use this to advance the night phase.
    /// </summary>
    public static event Action OnKillMutantTaskCompleted;

    public string ThreatName        => TaskDisplayName;
    public string ThreatDescription => TaskDisplayDesc;
    public float  ThreatLevel       => 0f;
    public float  ScoreWeight       => 0f;

    /// <summary>
    /// Creates a new instance, registers it with <see cref="TaskRegistry"/>, subscribes to
    /// <see cref="MutantEnemy.OnAnyMutantKilled"/>, and returns the instance.
    /// If an instance is already active, returns it without creating a duplicate.
    /// Called on all clients via <see cref="FollowTrailThreat"/> NetworkVariable callback.
    /// </summary>
    public static KillMutantTask CreateAndRegister()
    {
        if (Current != null) return Current;

        Current = new KillMutantTask();
        TaskRegistry.Instance.AddThreat(Current);
        MutantEnemy.OnAnyMutantKilled += Current.OnMutantKilled;
        return Current;
    }

    /// <summary>
    /// Removes the active instance from <see cref="TaskRegistry"/>, unsubscribes from mutant
    /// kill events, and clears <see cref="Current"/>. Safe to call when no instance is active.
    /// Called on all clients via <see cref="FollowTrailThreat"/> NetworkVariable callback when
    /// the kill is detected on the server.
    /// </summary>
    public static void CompleteAndRemove()
    {
        if (Current == null) return;

        MutantEnemy.OnAnyMutantKilled -= Current.OnMutantKilled;
        TaskRegistry.Instance.RemoveThreat(Current);
        Current = null;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void OnMutantKilled()
    {
        // Only fires on the server (MutantEnemy.Die() is server-authoritative).
        // Guard against re-entrancy if multiple mutants die in the same frame.
        if (Current != this) return;

        // Unsubscribe immediately so further kills don't double-fire.
        MutantEnemy.OnAnyMutantKilled -= OnMutantKilled;

        Debug.Log("[KillMutantTask] Mutant killed — notifying FollowTrailThreat to clear task on all clients.");

        // Null Current before the NetworkVariable update fires, preventing re-entrant guard issues.
        Current = null;

        // Setting the NetworkVariable to false fires OnKillMutantActiveChanged on ALL clients,
        // which calls CompleteAndRemove() everywhere (safe no-op since Current is already null here).
        FollowTrailThreat.Instance?.SetKillMutantActive(false);

        // Advance the night phase and notify Day_02's KillMutantSequence coroutine.
        OnKillMutantTaskCompleted?.Invoke();
    }

    // ISystemicThreat — no server-driven behaviour needed.
    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
