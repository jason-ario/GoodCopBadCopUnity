using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages which anomaly types are available to the player on exam-page checklists.
/// Anomalies not yet unlocked appear in a locked state at the bottom of each checklist
/// and cannot be checked by the player.
///
/// Unlock sources (checked in order):
/// <list type="bullet">
///   <item><see cref="_alwaysUnlockedTypeNames"/> — Inspector override; never requires save data.</item>
///   <item><see cref="AnomalyUnlockProgressionSO"/> — day-driven progression applied via <see cref="CampaignManager.OnDayChanged"/>.</item>
///   <item><see cref="SaveDataManager"/> — persisted unlocks earned through gameplay.</item>
/// </list>
/// </summary>
public class AnomalyUnlockManager : MonoBehaviour
{
    public static AnomalyUnlockManager Instance { get; private set; }

    [SerializeField]
    [Tooltip("Anomaly C# type names that are always available regardless of save state or day. " +
             "Useful for testing or tutorial overrides. Under normal gameplay, leave empty and " +
             "use the Progression asset instead.")]
    private string[] _alwaysUnlockedTypeNames = new string[0];

    [SerializeField]
    [Tooltip("ScriptableObject that defines which anomalies unlock on each campaign day.")]
    private AnomalyUnlockProgressionSO _progression;

    /// <summary>
    /// Fired when an anomaly type is newly unlocked.
    /// The argument is the C# type name — matches <see cref="ChecklistItem.AnomalyTypeName"/>.
    /// Exam pages subscribe here to refresh their lock states and re-sort.
    /// </summary>
    public static event Action<string> OnAnomalyUnlocked;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        CampaignManager.OnDayChanged += OnDayChanged;
    }

    private void OnDisable()
    {
        CampaignManager.OnDayChanged -= OnDayChanged;
    }

    // -------------------------------------------------------------------------
    // Day progression
    // -------------------------------------------------------------------------

    private void OnDayChanged(int day)
    {
        ApplyProgressionUpToDay(day);
    }

    /// <summary>
    /// Applies all scripted unlocks for days 1 through <paramref name="currentDay"/>
    /// cumulatively, then handles one random-deterministic unlock per active category
    /// for any days beyond the scripted range. Idempotent — safe to call repeatedly.
    /// </summary>
    private void ApplyProgressionUpToDay(int currentDay)
    {
        if (_progression == null)
        {
            Debug.LogWarning("[AnomalyUnlockManager] No AnomalyUnlockProgressionSO assigned — progression skipped.");
            return;
        }

        int lastScriptedDay = _progression.LastScriptedDay;

        // --- Scripted days ---
        for (int day = 1; day <= Mathf.Min(currentDay, lastScriptedDay); day++)
        {
            foreach (string typeName in _progression.GetScriptedUnlocksForDay(day))
                UnlockAnomaly(typeName);
        }

        // --- Post-scripted days: 1 random per active category ---
        if (currentDay > lastScriptedDay)
        {
            // Start with the full set already unlocked (scripted + saved) so each
            // simulated day knows exactly which anomalies are still available to pick.
            HashSet<string> cumulative = BuildCurrentUnlockedSet();

            for (int day = lastScriptedDay + 1; day <= currentDay; day++)
            {
                foreach (var category in _progression.AllCategories)
                {
                    string pick = _progression.GetRandomLockedAnomaly(category, day, cumulative);
                    if (pick != null)
                    {
                        UnlockAnomaly(pick);
                        cumulative.Add(pick);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Builds the complete set of anomaly type names that are already unlocked,
    /// including always-on overrides and save-data-backed unlocks from scripted days.
    /// Used as a seed when simulating which anomalies remain locked for post-scripted days.
    /// </summary>
    private HashSet<string> BuildCurrentUnlockedSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in _alwaysUnlockedTypeNames)
            if (!string.IsNullOrEmpty(name)) set.Add(name);

        if (_progression != null)
        {
            for (int day = 1; day <= _progression.LastScriptedDay; day++)
                foreach (string name in _progression.GetScriptedUnlocksForDay(day))
                    if (!string.IsNullOrEmpty(name)) set.Add(name);
        }

        string[] saved = SaveDataManager.Instance?.ActiveSlot?.UnlockedAnomalyTypeNames;
        if (saved != null)
            foreach (string name in saved)
                if (!string.IsNullOrEmpty(name)) set.Add(name);

        return set;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the anomaly with the given C# type name is unlocked.
    /// An empty or null type name is always considered unlocked.
    /// </summary>
    public bool IsAnomalyUnlocked(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return true;

        foreach (string alwaysName in _alwaysUnlockedTypeNames)
            if (string.Equals(alwaysName, typeName, StringComparison.Ordinal))
                return true;

        return SaveDataManager.Instance != null && SaveDataManager.Instance.IsAnomalyUnlocked(typeName);
    }

    /// <summary>
    /// Unlocks the anomaly with the given C# type name, persists the change to save data,
    /// and fires <see cref="OnAnomalyUnlocked"/> so exam pages can refresh their lock states.
    /// Safe to call multiple times — duplicate unlocks are silently ignored.
    /// </summary>
    public void UnlockAnomaly(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        if (IsAnomalyUnlocked(typeName)) return;

        SaveDataManager.Instance?.UnlockAnomaly(typeName);
        OnAnomalyUnlocked?.Invoke(typeName);
        Debug.Log($"[AnomalyUnlockManager] Anomaly unlocked: '{typeName}'.");
    }

    /// <summary>
    /// Cheat / debug helper: unlocks every anomaly defined in the progression asset,
    /// including all scripted and post-scripted entries across every category.
    /// Guidebook pages update automatically via <see cref="OnAnomalyUnlocked"/>.
    /// </summary>
    public void UnlockAllAnomalies()
    {
        if (_progression == null)
        {
            Debug.LogWarning("[AnomalyUnlockManager] No AnomalyUnlockProgressionSO assigned — cannot unlock all anomalies.");
            return;
        }

        int count = 0;

        // Scripted day entries (may include anomalies not present in AllCategories).
        for (int day = 1; day <= _progression.LastScriptedDay; day++)
        {
            foreach (string typeName in _progression.GetScriptedUnlocksForDay(day))
            {
                if (string.IsNullOrEmpty(typeName)) continue;
                if (IsAnomalyUnlocked(typeName)) continue;
                UnlockAnomaly(typeName);
                count++;
            }
        }

        // Full category lists — covers every anomaly in the game.
        foreach (var category in _progression.AllCategories)
        {
            if (category?.AnomalyTypeNames == null) continue;
            foreach (string typeName in category.AnomalyTypeNames)
            {
                if (string.IsNullOrEmpty(typeName)) continue;
                if (IsAnomalyUnlocked(typeName)) continue;
                UnlockAnomaly(typeName);
                count++;
            }
        }

        Debug.Log($"[AnomalyUnlockManager] Unlock All: {count} anomalies newly unlocked.");
    }
}
