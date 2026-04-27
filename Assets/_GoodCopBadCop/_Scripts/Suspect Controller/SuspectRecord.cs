using System;
using UnityEngine;

[Serializable]
public class SuspectRecord
{
    public SuspectData SuspectData;
    public int daysShown = 0;
    public int lastDayShown = 0;
    public int infectionScore = 0;
    
    public SuspectRecord(SuspectData suspectData)
    {
        SuspectData = suspectData;
        infectionScore = (int)UnityEngine.Random.Range(SuspectRunRecords.Instance.startingInfectionScore.x, SuspectRunRecords.Instance.startingInfectionScore.y);
    }
}
