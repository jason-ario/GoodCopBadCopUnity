using System;
using System.Collections.Generic;
using UnityEngine;
using Mathf = UnityEngine.Mathf;
using System.Linq;


[Serializable]
public class SuspectRecord
{
    public SuspectData Data;
    public CharacterStatus Status;
    public DateTime LastExitTime;
    public bool HasBeenSeen;

    [Header("Hidden Infection State")]
    [Range(0, 100)] public int InfectionScore;
    [Min(0)] public int DailyProgressionRate;
    public int LastDayUpdated = 0;
    public bool IsQuarantined = false;

    [Header("Appearance Tracking")]
    public int LastDaySeen = -999;
    public int TimesSeen = 0;

    [NonSerialized] public List<Anomaly> AssignedAnomalies = new();

    public bool CanProgressInfection
    {
        get
        {
            if (IsQuarantined) return false;
            if (Status == CharacterStatus.Deceased) return false;
            return true;
        }
    }

    public bool CanAppear
    {
        get
        {
            if (IsQuarantined) return false;
            if (Status == CharacterStatus.Deceased) return false;
            return true;
        }
    }
}

public enum InfectionStage
{
    Clean,
    Mild,
    Suspicious,
    Infected,
    Critical
}

public enum JudgmentResult
{
    Passed,
    Quarantined,
    Killed
}

public class SuspectDatabase : MonoBehaviour
{
    public static SuspectDatabase Instance;

    [SerializeField] private SuspectSet allSuspects;

    private Dictionary<SuspectData, SuspectRecord> records = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildDatabase();
    }

    private void BuildDatabase()
    {
        records.Clear();

        foreach (var suspect in allSuspects.suspects)
        {
            records[suspect] = new SuspectRecord
            {
                Data = suspect,
                Status = CharacterStatus.Resident,
                LastExitTime = DateTime.MinValue,
                HasBeenSeen = false,

                InfectionScore = Mathf.Clamp(suspect.startingInfectionScore, 0, 100),
                DailyProgressionRate = Mathf.Max(0, suspect.dailyInfectionProgression),
                LastDayUpdated = 0,
                IsQuarantined = false
            };
        }
    }

    public SuspectRecord GetRecord(SuspectData data)
    {
        if (data == null)
        {
            Debug.LogError("Tried to get suspect record with null SuspectData.");
            return null;
        }

        if (records.TryGetValue(data, out var record))
            return record;

        Debug.LogError($"No record found for suspect: {data.name}");
        return null;
    }

    public List<SuspectRecord> GetAllRecords()
    {
        return records.Values.ToList();
    }

    public void AdvanceToDay(int currentDay)
    {
        foreach (var record in records.Values)
        {
            UpdateRecordToDay(record, currentDay);
        }
    }

    public void UpdateRecordToDay(SuspectRecord record, int currentDay)
    {
        if (record == null)
            return;

        if (currentDay <= record.LastDayUpdated)
            return;

        if (!record.CanProgressInfection)
        {
            record.LastDayUpdated = currentDay;
            return;
        }

        int daysElapsed = currentDay - record.LastDayUpdated;
        int increase = daysElapsed * record.DailyProgressionRate;

        record.InfectionScore = Mathf.Clamp(record.InfectionScore + increase, 0, 100);
        record.LastDayUpdated = currentDay;
    }

    public void SetInfectionScore(SuspectData data, int newScore)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.InfectionScore = Mathf.Clamp(newScore, 0, 100);
    }

    public void AddInfectionScore(SuspectData data, int amount)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.InfectionScore = Mathf.Clamp(record.InfectionScore + amount, 0, 100);
    }

    public int GetInfectionScore(SuspectData data)
    {
        var record = GetRecord(data);
        if (record == null) return 0;

        return record.InfectionScore;
    }

    public void SetQuarantined(SuspectData data, bool isQuarantined)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.IsQuarantined = isQuarantined;
    }

    public void SetDailyProgressionRate(SuspectData data, int newRate)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.DailyProgressionRate = Mathf.Max(0, newRate);
    }
    
    public void MarkSuspectSeenToday(SuspectData data, int currentDay)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.HasBeenSeen = true;
        record.LastDaySeen = currentDay;
        record.TimesSeen++;
    }

    public List<SuspectRecord> GetAppearableRecords()
    {
        return records.Values.Where(r => r.CanAppear).ToList();
    }
    
    public void ApplyJudgment(SuspectData data, JudgmentResult result, int currentDay)
    {
        var record = GetRecord(data);
        if (record == null) return;

        switch (result)
        {
            case JudgmentResult.Passed:
                record.LastDaySeen = currentDay;
                record.HasBeenSeen = true;
                break;

            case JudgmentResult.Quarantined:
                record.LastDaySeen = currentDay;
                record.HasBeenSeen = true;
                record.IsQuarantined = true;
                record.Status = CharacterStatus.Resident; // or CharacterStatus.Quarantined if you add that enum later
                break;

            case JudgmentResult.Killed:
                record.LastDaySeen = currentDay;
                record.HasBeenSeen = true;
                record.Status = CharacterStatus.Deceased;
                break;
        }
    }
    
    public void ReleaseFromQuarantine(SuspectData data)
    {
        var record = GetRecord(data);
        if (record == null) return;

        record.IsQuarantined = false;

        if (record.Status == CharacterStatus.Deceased)
            return;

        record.Status = CharacterStatus.Resident;
    }
}