/// <summary>
/// Lightweight <see cref="ISystemicThreat"/> implementation used to display
/// tutorial step entries in the HUD task list via <see cref="GuidebookTaskRegistry"/>.
/// All systemic-threat members are no-ops or return neutral defaults.
/// </summary>
public class TutorialTask : ISystemicThreat
{
    public string ThreatName { get; }
    public string ThreatDescription => string.Empty;
    public float ThreatLevel => 0f;
    public float ScoreWeight => 0f;

    public TutorialTask(string name) => ThreatName = name;

    public void BeginNightPhase() { }
    public void EndNightPhase() { }
}
