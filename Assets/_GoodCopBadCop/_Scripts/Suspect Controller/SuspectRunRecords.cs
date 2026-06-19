using System;
using System.Collections.Generic;
using UnityEngine;

public class SuspectRunRecords : MonoBehaviour
{
    private List<SuspectRecord> records = new List<SuspectRecord>();
    public SuspectSet allSuspects;
    public Vector2 startingInfectionScore = new Vector2(0, 10);
    public Vector2 inspectionScoreIncreasePerDay = new Vector2(5, 20);

    public static SuspectRunRecords Instance;
    
    private void Start()
    {
        Instance = this;
        
        InitializeRecordsForRun();
    }

    void InitializeRecordsForRun()
    {
        foreach (var suspectData in allSuspects.suspects)
        {
            SuspectRecord record = new SuspectRecord(suspectData);
            records.Add(record);
        }
    }
    
    public SuspectRecord GetRecord(SuspectData suspectData)
    {
        return records.Find(record => record.SuspectData == suspectData);
    }

    /// <summary>
    /// Advances each living suspect's infection score by a per-character random amount.
    /// Quarantine-treated suspects have their score reset instead — unless they are fully mutated,
    /// in which case the quarantine has no effect.
    /// Call this before DailySuspectManager populates the next shift.
    /// </summary>
    public void AdvanceDayInfection()
    {
        foreach (SuspectRecord record in records)
        {
            if (record.isKilled) continue;

            if (record.pendingVaccineReset)
            {
                record.pendingVaccineReset = false;

                if (record.IsFullyMutated)
                {
                    Debug.Log($"[SuspectRunRecords] '{record.SuspectData.name}' quarantine had no effect — already fully mutated (score {record.infectionScore}).");
                }
                else
                {
                    record.infectionScore = record.SuspectData.startingInfectionScore;
                    Debug.Log($"[SuspectRunRecords] '{record.SuspectData.name}' quarantine reset → score {record.infectionScore}.");
                }
            }
            else
            {
                Vector2Int range = record.SuspectData.dailyInfectionProgression;
                int increase = UnityEngine.Random.Range(range.x, range.y + 1);
                record.infectionScore = Mathf.Clamp(record.infectionScore + increase, 0, 100);
                Debug.Log($"[SuspectRunRecords] '{record.SuspectData.name}' infection +{increase} → {record.infectionScore}{(record.IsFullyMutated ? " [FULLY MUTATED]" : "")}.");
            }
        }
    }
}
