using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data asset that drives the day-by-day anomaly unlock progression displayed on exam-page checklists.
///
/// Anomalies are grouped into categories (e.g. "Documentation", "Physical", "Vitals"). Each category
/// has a single <see cref="AnomalyCategoryData.UnlockDay"/> — on that campaign day, every anomaly in
/// the category unlocks at once and stays unlocked. There is no partial or random per-day trickling
/// within a category.
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
    public class AnomalyCategoryData
    {
        [Tooltip("Human-readable label used only for the Inspector (e.g. 'Documentation').")]
        public string CategoryName;

        [Tooltip("1-based campaign day on which every anomaly in this category unlocks at once.")]
        public int UnlockDay = 1;

        [Tooltip("All anomaly C# type names for this category in checklist order (top → bottom). " +
                 "All of these unlock simultaneously on UnlockDay.")]
        public string[] AnomalyTypeNames = new string[0];
    }

    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("All Anomalies by Category")]
    [Tooltip("Every anomaly in the game grouped by exam category and listed in checklist order. " +
             "Each category unlocks in full on its configured UnlockDay.")]
    [SerializeField] private AnomalyCategoryData[] _allCategories = new AnomalyCategoryData[0];

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>All category definitions.</summary>
    public AnomalyCategoryData[] AllCategories => _allCategories;

    /// <summary>The highest UnlockDay configured across all categories.</summary>
    public int LastUnlockDay
    {
        get
        {
            int max = 0;
            foreach (var category in _allCategories)
                if (category.UnlockDay > max) max = category.UnlockDay;
            return max;
        }
    }

    /// <summary>
    /// Returns every anomaly type name whose category unlocks on the given <paramref name="day"/>.
    /// </summary>
    public string[] GetNewUnlocksForDay(int day)
    {
        var result = new List<string>();
        foreach (var category in _allCategories)
        {
            if (category == null || category.AnomalyTypeNames == null) continue;
            if (category.UnlockDay != day) continue;

            foreach (string typeName in category.AnomalyTypeNames)
                if (!string.IsNullOrEmpty(typeName)) result.Add(typeName);
        }
        return result.ToArray();
    }
}
