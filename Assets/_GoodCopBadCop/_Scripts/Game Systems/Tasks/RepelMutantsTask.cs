/// <summary>
/// <see cref="TaskRegistry"/> entry for a running mutant breach ("Repel the mutants").
///
/// Purely a display object, same pattern as <see cref="KillMutantTask"/>: it exists so the breach
/// shows up on every registry-driven task list — the world-space <see cref="TaskPage"/>, the
/// guidebook Tasks tab, and (outside Day 1) the HUD via <see cref="HUDTaskList"/>. Before this
/// existed the breach was only ever a hand-scripted <see cref="TutorialObjectiveList"/> row in
/// <see cref="Day_01"/>, so it never reached the Task Page.
///
/// Driven entirely by <see cref="MutantBreachManager"/> on every peer from its replicated
/// running/remaining/total state, so late joiners get the row too. Kill tracking and breach
/// completion stay in the manager.
/// </summary>
public class RepelMutantsTask : ISystemicThreat
{
    private const string TaskName = "Repel the mutants";

    /// <summary>The currently registered instance, or null when no breach is running.</summary>
    public static RepelMutantsTask Current { get; private set; }

    /// <summary>
    /// Set by a day script that shows its own hand-scripted HUD row for the breach (Day 1's
    /// tutorial "Repel the mutants (x/y)" row), so <see cref="HUDTaskList"/> doesn't add a
    /// duplicate. Static because the instance only exists while a breach is running. Does NOT
    /// affect the Task Page or guidebook, which always list the task.
    /// </summary>
    public static bool HasCustomTutorialRow { get; set; }

    private int _remaining = -1;
    private int _total;

    public string ThreatName => TaskName;

    /// <summary>"defeated/total" once the wave has spawned; empty during the alarm lead time.</summary>
    public string ThreatDescription
    {
        get
        {
            if (_total <= 0 || _remaining < 0) return string.Empty;
            int defeated = System.Math.Clamp(_total - _remaining, 0, _total);
            return $"{defeated}/{_total}";
        }
    }

    public float ThreatLevel => 0f;
    public float ScoreWeight => 0f;

    /// <summary>
    /// Ensures the task exists and is registered, then applies the latest count (pass a total of
    /// 0 when the wave hasn't spawned yet). Re-registers unconditionally so a registry rebuild
    /// (<see cref="TaskRegistry.SetThreats"/>) mid-breach self-heals on the next update —
    /// <see cref="TaskRegistry.AddThreat"/> ignores duplicates.
    /// </summary>
    public static void EnsureRegistered(int remaining, int total)
    {
        Current ??= new RepelMutantsTask();

        bool changed = false;
        if (total > 0 && (Current._remaining != remaining || Current._total != total))
        {
            Current._remaining = remaining;
            Current._total = total;
            changed = true;
        }

        TaskRegistry registry = TaskRegistry.Instance;
        if (registry == null) return;

        if (!TaskRegistry.IsRegistered(Current))
            registry.AddThreat(Current);
        else if (changed)
            registry.NotifyTaskStateChanged();
    }

    /// <summary>Removes the task from <see cref="TaskRegistry"/>. Safe to call when none is active.</summary>
    public static void Remove()
    {
        if (Current == null) return;

        RepelMutantsTask task = Current;
        Current = null;
        TaskRegistry.Instance?.RemoveThreat(task);
    }

    // ISystemicThreat — no server-driven behaviour needed.
    public void BeginNightPhase() { }
    public void EndNightPhase() { }
}
