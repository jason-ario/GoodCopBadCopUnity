using System.Collections.Generic;
using UnityEngine;

public class SuspectRunRecords : MonoBehaviour
{
    public const int QuarantineSlotLimit = 5;
    public const int QuarantineDurationDays = 2;

    private List<SuspectRecord> records = new List<SuspectRecord>();
    public IReadOnlyList<SuspectRecord> Records => records;
    public SuspectSet allSuspects;
    public Vector2 startingInfectionScore = new Vector2(0, 10);
    public Vector2 inspectionScoreIncreasePerDay = new Vector2(5, 20);

    [Header("Replacement System")]
    [Tooltip("Number of days after a suspect is killed before their replacement version activates and re-enters the shift pool.")]
    [Min(1)] public int replacementWindowDays = 7;

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

            record.isKilled                = entry.IsKilled;
            record.hasEnteredCity          = entry.HasEnteredCity;
            record.populationDeathRecorded = entry.PopulationDeathRecorded;
            record.killedOnDay             = entry.KilledOnDay;
            record.isReplacement           = entry.IsReplacement;
            record.quarantinedOnDay        = entry.QuarantinedOnDay;
            record.infectionScore          = entry.InfectionScore;
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

    public int GetActiveQuarantineCount(int currentDay)
    {
        int count = 0;

        foreach (SuspectRecord record in records)
        {
            if (record == null || record.isKilled)
                continue;

            if (GetRemainingQuarantineDays(record, currentDay) > 0)
                count++;
        }

        return count;
    }

    public bool HasQuarantineSlot(int currentDay)
        => GetActiveQuarantineCount(currentDay) < QuarantineSlotLimit;

    public List<SuspectRecord> GetActiveQuarantineRecords(int currentDay)
    {
        List<SuspectRecord> activeRecords = new List<SuspectRecord>();

        foreach (SuspectRecord record in records)
        {
            if (record == null || record.isKilled)
                continue;

            if (GetRemainingQuarantineDays(record, currentDay) > 0)
                activeRecords.Add(record);
        }

        return activeRecords;
    }

    public bool IsInActiveQuarantine(SuspectData suspectData, int currentDay)
    {
        SuspectRecord record = GetRecord(suspectData);
        return GetRemainingQuarantineDays(record, currentDay) > 0;
    }

    public int GetRemainingQuarantineDays(SuspectData suspectData, int currentDay)
    {
        SuspectRecord record = GetRecord(suspectData);
        return GetRemainingQuarantineDays(record, currentDay);
    }

    public int GetRemainingQuarantineDays(SuspectRecord record, int currentDay)
    {
        if (record == null || currentDay < 0 || record.quarantinedOnDay < 0)
            return 0;

        int elapsedDays = currentDay - record.quarantinedOnDay;
        return Mathf.Clamp(QuarantineDurationDays - elapsedDays, 0, QuarantineDurationDays);
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
    /// Checks whether any killed suspect has waited long enough to have their replacement activate.
    /// Persists all changes to disk after advancing.
    /// Call this before DailySuspectManager populates the next shift.
    /// </summary>
    public void AdvanceDayInfection()
    {
        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;

        foreach (SuspectRecord record in records)
        {
            // --- Replacement activation check ---
            // A killed suspect with a valid replacementConfig re-enters the pool as an uncanny
            // replacement after replacementWindowDays have elapsed since their death.
            if (record.isKilled && !record.isReplacement)
            {
                if (currentDay >= 0 && record.killedOnDay >= 0
                    && (currentDay - record.killedOnDay) >= replacementWindowDays
                    && record.SuspectData != null && record.SuspectData.replacementIDPhoto != null)
                {
                    record.isReplacement = true;
                    Debug.Log($"[SuspectRunRecords] '{record.SuspectData.name}' replacement activated on day {currentDay} " +
                              $"(killed on day {record.killedOnDay}, window {replacementWindowDays}d).");
                }
                // Killed suspects (not yet replaced) skip normal infection advancement.
                continue;
            }

            // Replacement suspects also skip normal infection advancement (they're handled as doppelgangers).
            if (record.isReplacement) continue;

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
