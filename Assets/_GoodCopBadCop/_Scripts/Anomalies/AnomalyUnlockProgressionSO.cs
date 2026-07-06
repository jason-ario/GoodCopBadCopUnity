using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data asset that drives the day-by-day anomaly unlock progression displayed on exam-page checklists.
///
/// Days 1–5 use explicit scripted unlock lists defined in <see cref="ScriptedDayUnlocks"/>.
/// Days 6 and beyond automatically unlock one anomaly per active category using a deterministic
/// hash of the day number, cycling through whichever anomalies remain locked.
///
/// All anomaly type names are the C# class names (e.g. "FearfulAnomaly") matching
/// <see cref="ChecklistItem.AnomalyTypeName"/>.
/// </summary>
[CreateAssetMenu(menuName = "Good Cop Bad Cop/Anomaly Unlock Progression", fileName = "AnomalyUnlockProgression")]
public class AnomalyUnlockProgressionSO : ScriptableObject
{
    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    [Serializable]
    public class DayUnlockEntry
    {
        [Tooltip("1-based campaign day number.")]
        public int Day;

        [Tooltip("Anomaly C# type names to unlock on this day (e.g. 'FearfulAnomaly').")]
        public string[] UnlockTypeNames = new string[0];
    }

    [Serializable]
    public class AnomalyCategoryData
    {
        [Tooltip("Human-readable label used only for the Inspector (e.g. 'Documentation').")]
        public string CategoryName;

        [Tooltip("All anomaly C# type names for this category in checklist order (top → bottom). " +
                 "The first N entries are the scripted base unlocks; remaining entries are used for " +
                 "the random daily unlocks on days beyond the scripted range.")]
        public string[] AnomalyTypeNames = new string[0];
    }

    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Scripted Day Unlocks (Days 1–5)")]
    [Tooltip("Explicit unlock lists for each scripted campaign day. Each entry is a day number " +
             "and the full set of anomaly type names to unlock on that day.")]
    [SerializeField] private DayUnlockEntry[] _scriptedDayUnlocks = new DayUnlockEntry[0];

    [Header("All Anomalies by Category")]
    [Tooltip("Every anomaly in the game grouped by exam category and listed in checklist order. " +
             "Used to determine which anomalies remain locked for the random daily unlock logic " +
             "that kicks in on days beyond the scripted range.")]
    [SerializeField] private AnomalyCategoryData[] _allCategories = new AnomalyCategoryData[0];

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>All category definitions, used for random unlock selection on post-scripted days.</summary>
    public AnomalyCategoryData[] AllCategories => _allCategories;

    /// <summary>The highest day number that has an explicit scripted unlock entry.</summary>
    public int LastScriptedDay
    {
        get
        {
            int max = 0;
            foreach (var entry in _scriptedDayUnlocks)
                if (entry.Day > max) max = entry.Day;
            return max;
        }
    }

    /// <summary>
    /// Returns the anomaly type names scripted for the given day, or an empty array if none.
    /// </summary>
    public string[] GetScriptedUnlocksForDay(int day)
    {
        foreach (var entry in _scriptedDayUnlocks)
            if (entry.Day == day) return entry.UnlockTypeNames;
        return Array.Empty<string>();
    }

    /// <summary>
    /// Returns the next anomaly to unlock for <paramref name="category"/> on the given
    /// post-scripted <paramref name="day"/>. Selection is deterministic: remaining locked
    /// anomalies are hashed with the day number so the result is consistent across sessions
    /// and multiplayer clients.
    /// Returns null when all anomalies in the category are already in
    /// <paramref name="alreadyUnlocked"/>.
    /// </summary>
    public string GetRandomLockedAnomaly(AnomalyCategoryData category, int day, HashSet<string> alreadyUnlocked)
    {
        if (category == null || category.AnomalyTypeNames == null) return null;

        var locked = new List<string>(category.AnomalyTypeNames.Length);
        foreach (string typeName in category.AnomalyTypeNames)
        {
            if (!string.IsNullOrEmpty(typeName) && !alreadyUnlocked.Contains(typeName))
                locked.Add(typeName);
        }

        if (locked.Count == 0) return null;

        // Hash day + category name for a stable pick that varies per category and per day.
        int hash = Mathf.Abs((int)((uint)(day * 2654435761u) ^ (uint)(category.CategoryName?.GetHashCode() ?? 0)));
        return locked[hash % locked.Count];
    }
}
