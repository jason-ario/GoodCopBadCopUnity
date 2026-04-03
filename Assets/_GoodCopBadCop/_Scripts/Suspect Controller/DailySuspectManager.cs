using System.Collections.Generic;
using UnityEngine;

public class DailySuspectManager : MonoBehaviour
{
    [SerializeField] private SuspectSelectionController suspectSelectionController;

    private List<SuspectRecord> currentDayLineup = new();

    public List<SuspectRecord> CurrentDayLineup => currentDayLineup;

    public void GenerateDay(int currentDay)
    {
        SuspectDatabase.Instance.AdvanceToDay(currentDay);

        currentDayLineup = suspectSelectionController.GenerateLineupForDay(currentDay);

        foreach (var record in currentDayLineup)
        {
            SuspectDatabase.Instance.MarkSuspectSeenToday(record.Data, currentDay);
        }
    }

    public SuspectRecord GetSuspectForSlot(int index)
    {
        if (index < 0 || index >= currentDayLineup.Count)
        {
            Debug.LogError($"Invalid suspect index: {index}");
            return null;
        }

        return currentDayLineup[index];
    }
}