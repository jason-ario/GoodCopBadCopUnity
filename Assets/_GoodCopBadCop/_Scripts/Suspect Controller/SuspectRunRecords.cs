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
}
