using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Campaign-wide configuration asset. Holds an ordered list of DayEntry records,
/// one per in-game day. Assign to CampaignManager in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "CampaignData", menuName = "GoodCopBadCop/Campaign Data")]
public class CampaignData : ScriptableObject
{
    [SerializeField] private List<DayEntry> _days = new List<DayEntry>();

    /// <summary>Total number of authored days in this campaign.</summary>
    public int TotalDays => _days.Count;

    /// <summary>
    /// Returns the DayEntry for the given 1-based day number.
    /// Clamps to the last entry if dayNumber exceeds TotalDays.
    /// </summary>
    public DayEntry GetDayEntry(int dayNumber)
    {
        if (_days.Count == 0)
        {
            Debug.LogWarning("[CampaignData] No day entries configured.");
            return default;
        }

        int index = Mathf.Clamp(dayNumber - 1, 0, _days.Count - 1);
        return _days[index];
    }
}

/// <summary>
/// Lightweight data record for a single campaign day.
/// Each entry is authored in the CampaignData ScriptableObject.
/// </summary>
[Serializable]
public struct DayEntry
{
    [Tooltip("Optional label shown in the Inspector for quick identification, e.g. 'Day 3 – First Anomaly'.")]
    public string dayLabel;

    [Tooltip("The set of suspects that will be processed during this day's shift.")]
    public SuspectSet suspectSet;

    [Tooltip("Tutorial steps fired by CampaignManager at the start of this day's shift.")]
    public List<TutorialStep> tutorialStepsToFire;
}
