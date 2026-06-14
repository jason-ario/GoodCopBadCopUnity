using UnityEngine;

/// <summary>
/// Lightweight debug-only systemic threat. Does not require a NetworkObject or server authority.
/// Toggle completion via DebugConsole (F5 by default).
/// </summary>
public class DebugTask : MonoBehaviour, ISystemicThreat
{
    private const string DefaultName        = "Debug Threat";
    private const string DefaultDescription = "A fake systemic threat for testing the guidebook task list.";

    public string ThreatName        => DefaultName;
    public string ThreatDescription => DefaultDescription;
    public float  ThreatLevel       { get; private set; }
    public float  ScoreWeight       => 0f;

    /// <summary>Simulates a high-threat state and notifies the registry.</summary>
    public void Complete()
    {
        ThreatLevel = 1f;
        Debug.Log("[DebugTask] Threat level set to max.");
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }

    /// <summary>Resets the threat level to zero and notifies the registry.</summary>
    public void ResetTask()
    {
        ThreatLevel = 0f;
        Debug.Log("[DebugTask] Threat level reset.");
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public void BeginNightPhase() { }
    public void EndNightPhase()   { }
}
