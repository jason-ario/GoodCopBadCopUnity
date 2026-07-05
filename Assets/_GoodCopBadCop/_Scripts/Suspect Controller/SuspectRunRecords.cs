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

    private void InitializeRecordsForRun()
    {
        records.Clear();
        foreach (SuspectData suspectData in allSuspects.suspects)
        {
            SuspectRecord record = new SuspectRecord(suspectData);
            records.Add(record);
        }

        ApplySavedData();
    }

    /// <summary>
    /// Overlays persisted state (kill flags, quarantine cooldowns, infection scores) onto the
    /// freshly-initialized runtime records. Called once at startup after the records list is built.
    /// Any suspect in the save that is no longer in the SuspectSet is silently skipped.
    /// </summary>
    private void ApplySavedData()
    {
        if (SaveDataManager.Instance == null) return;

        SuspectSaveEntry[] saved = SaveDataManager.Instance.GetSavedSuspectRecords();
        if (saved == null || saved.Length == 0) return;

        // Build a lookup from asset name → save entry for O(1) matching.
        var lookup = new Dictionary<string, SuspectSaveEntry>(saved.Length);
        foreach (SuspectSaveEntry entry in saved)
        {
            if (!string.IsNullOrEmpty(entry.SuspectName))
                lookup[entry.SuspectName] = entry;
        }

        int applied = 0;
        foreach (SuspectRecord record in records)
        {
            if (record.SuspectData == null) continue;
            if (!lookup.TryGetValue(record.SuspectData.name, out SuspectSaveEntry entry)) continue;

            record.isKilled         = entry.IsKilled;
            record.quarantinedOnDay = entry.QuarantinedOnDay;
            record.infectionScore   = entry.InfectionScore;
            applied++;
        }

        Debug.Log($"[SuspectRunRecords] Applied saved state to {applied}/{records.Count} record(s).");
    }

    /// <summary>
    /// Returns the runtime record for the given SuspectData, or null if not found.
    /// </summary>
    public SuspectRecord GetRecord(SuspectData suspectData)
    {
        return records.Find(record => record.SuspectData == suspectData);
    }

    /// <summary>
    /// Persists all current runtime records to the active save slot.
    /// Call this after any record mutation: kill, quarantine, and end-of-day infection advance.
    /// Server-only — only the host mutates suspect records.
    /// </summary>
    public void SaveRecords()
    {
        if (SaveDataManager.Instance == null)
        {
            Debug.LogWarning("[SuspectRunRecords] SaveRecords: SaveDataManager not available.");
            return;
        }

        SaveDataManager.Instance.SaveSuspectRecords(records);
    }

    /// <summary>
    /// Advances each living suspect's infection score by a per-character random amount.
    /// Quarantine-treated suspects have their score reset instead — unless they are fully mutated,
    /// in which case the quarantine has no effect.
    /// Persists all changes to disk after advancing.
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

        // Flush all changes (including updated infection scores) to disk.
        SaveRecords();
    }
}
