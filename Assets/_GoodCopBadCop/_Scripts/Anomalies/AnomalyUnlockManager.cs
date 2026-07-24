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
///   <item><see cref="AnomalyUnlockProgressionSO"/> — whole-category progression applied via <see cref="CampaignManager.OnDayChanged"/>.</item>
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

    /// <summary>
    /// In-memory set of anomaly type names unlocked during this session.
    /// Populated by <see cref="UnlockAnomaly"/> before <see cref="OnAnomalyUnlocked"/> fires,
    /// so <see cref="IsAnomalyUnlocked"/> always reflects the current session state even when
    /// <see cref="SaveDataManager"/> has no active slot (e.g. debug skip cheats).
    /// </summary>
    private readonly HashSet<string> _runtimeUnlocked = new HashSet<string>(StringComparer.Ordinal);

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
    /// Unlocks every anomaly whose category becomes available on any day from 1 through
    /// <paramref name="currentDay"/>. Each category unlocks in one shot — every anomaly it
    /// contains becomes available as soon as its <see cref="AnomalyUnlockProgressionSO.AnomalyCategoryData.UnlockDay"/>
    /// is reached, with no further per-day trickling within that category. Idempotent — safe
    /// to call repeatedly.
    /// </summary>
    private void ApplyProgressionUpToDay(int currentDay)
    {
        if (_progression == null)
        {
            Debug.LogWarning("[AnomalyUnlockManager] No AnomalyUnlockProgressionSO assigned — progression skipped.");
            return;
        }

        for (int day = 1; day <= currentDay; day++)
        {
            foreach (string typeName in _progression.GetNewUnlocksForDay(day))
                UnlockAnomaly(typeName);
        }
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

        if (_runtimeUnlocked.Contains(typeName))
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

        // Track in memory BEFORE firing the event so any IsAnomalyUnlocked check
        // inside OnAnomalyUnlocked handlers (e.g. RefreshLockStates) sees the new state.
        // This also ensures correctness when SaveDataManager has no active slot
        // (e.g. debug skip cheats), where the save write is silently skipped.
        _runtimeUnlocked.Add(typeName);
        SaveDataManager.Instance?.UnlockAnomaly(typeName);
        OnAnomalyUnlocked?.Invoke(typeName);
        Debug.Log($"[AnomalyUnlockManager] Anomaly unlocked: '{typeName}'.");
    }

    /// <summary>
    /// Cheat / debug helper: unlocks every anomaly defined in the progression asset,
    /// across every category regardless of UnlockDay.
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
