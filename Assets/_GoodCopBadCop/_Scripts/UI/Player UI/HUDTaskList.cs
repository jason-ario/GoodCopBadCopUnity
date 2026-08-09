using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges <see cref="TaskRegistry"/> into the tutorial overlay's <see cref="TutorialObjectiveList"/>.
/// Every active <see cref="ISystemicThreat"/> — tutorial step prompts and regular systemic-threat
/// tasks alike — gets a row in the objective list; rows are added, relabeled, and completed as the
/// registry changes, so all current tasks live in one place instead of a separate HUD list.
/// </summary>
public class HUDTaskList : MonoBehaviour
{
    private readonly Dictionary<ISystemicThreat, TutorialObjectiveItem> _rows = new();

    /// <summary>
    /// Public re-sync entry point. Tasks triggered before this HUD element ever became enabled
    /// (e.g. <see cref="Day_03"/>'s trash/blood tasks, which <see cref="CampaignManager.ApplyDay"/>
    /// activates the moment <see cref="GameManager.TryStartGame"/> runs on a resumed save — well
    /// before <see cref="ShiftManager.ResumeSavedDay"/> shows the player UI) are already sitting
    /// in <see cref="TaskRegistry"/> by the time this fires, but <see cref="OnEnable"/>'s one-shot
    /// <see cref="Rebuild"/> call only catches them if this component happens to enable after
    /// they're registered. Call this explicitly after such a resume to force a fresh sync instead
    /// of relying on that ordering.
    /// </summary>
    public void ForceRebuild() => Rebuild();

    private void OnEnable()
    {
        TaskRegistry.OnTaskListChanged += Rebuild;
        TaskRegistry.OnTaskStateChanged += RefreshLabels;
        Rebuild();
    }

    private void OnDisable()
    {
        TaskRegistry.OnTaskListChanged -= Rebuild;
        TaskRegistry.OnTaskStateChanged -= RefreshLabels;
    }

    /// <summary>Syncs objective-list rows with the current registry state: adds new, removes stale.</summary>
    private void Rebuild()
    {
        TutorialObjectiveList list = TutorialObjectiveList.Instance;
        if (list == null) return;

        IReadOnlyList<ISystemicThreat> active = TaskRegistry.Instance != null
            ? TaskRegistry.Instance.Threats
            : System.Array.Empty<ISystemicThreat>();

        // Add a row for every newly-active threat.
        foreach (ISystemicThreat threat in active)
        {
            if (_rows.ContainsKey(threat)) continue;

            // ProcessResidentsTask mirrors the exact same requirement DayBase already shows as
            // its own hand-scripted "Process N subjects X/Y" row (see DayBase.ShowAutomaticSubjectCounterTask).
            // Skip it here so the two don't both add a row for the same thing.
            if (threat is ProcessResidentsTask) continue;

            // TakeOutTrashTask / CleanGraffitiTask / FenceRepairTask / CleanBloodTask are skipped
            // whenever a day script (e.g. Day 1) is showing its own hand-scripted tutorial row for
            // them — see HasCustomTutorialRow. Without this, Day 1 duplicates each objective: one
            // row from Day_01's tutorial choreography and a second from this generic registry
            // bridge (e.g. "Clean Blood: 0/4" next to "Clean up the blood 0/4").
            if (threat is TakeOutTrashTask trashTask && trashTask.HasCustomTutorialRow) continue;
            if (threat is CleanGraffitiTask graffitiTask && graffitiTask.HasCustomTutorialRow) continue;
            if (threat is FenceRepairTask fenceTask && fenceTask.HasCustomTutorialRow) continue;
            if (threat is CleanBloodTask bloodTask && bloodTask.HasCustomTutorialRow) continue;

            TutorialObjectiveItem item = list.AddObjective(BuildLabel(threat));
            if (item != null)
                _rows[threat] = item;
        }

        // Complete and remove rows for threats no longer in the registry.
        List<ISystemicThreat> stale = null;
        foreach (KeyValuePair<ISystemicThreat, TutorialObjectiveItem> kvp in _rows)
        {
            bool stillActive = false;
            foreach (ISystemicThreat threat in active)
            {
                if (ReferenceEquals(threat, kvp.Key))
                {
                    stillActive = true;
                    break;
                }
            }

            if (!stillActive)
                (stale ??= new List<ISystemicThreat>()).Add(kvp.Key);
        }

        if (stale == null) return;

        foreach (ISystemicThreat threat in stale)
        {
            list.CompleteAndRemoveObjective(_rows[threat], preHideDelay: 1.5f);
            _rows.Remove(threat);
        }
    }

    /// <summary>Refreshes row text for threats whose description/level changed without list membership changing.</summary>
    private void RefreshLabels()
    {
        TutorialObjectiveList list = TutorialObjectiveList.Instance;
        if (list == null) return;

        foreach (KeyValuePair<ISystemicThreat, TutorialObjectiveItem> kvp in _rows)
            list.UpdateObjective(kvp.Value, BuildLabel(kvp.Key));
    }

    private static string BuildLabel(ISystemicThreat threat)
    {
        bool hasDescription = !string.IsNullOrEmpty(threat.ThreatDescription);
        return hasDescription
            ? $"{threat.ThreatName}: {threat.ThreatDescription}"
            : threat.ThreatName;
    }
}
