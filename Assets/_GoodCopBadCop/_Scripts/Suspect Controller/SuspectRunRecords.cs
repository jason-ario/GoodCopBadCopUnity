using System.Collections.Generic;
using UnityEngine;

public class SuspectRunRecords : MonoBehaviour
{
    public const int QuarantineSlotLimit = 5;
    public const int QuarantineDurationDays = 2;

    private List<SuspectRecord> records = new List<SuspectRecord>();
    public IReadOnlyList<SuspectRecord> Records => records;
    public SuspectSet allSuspects;
    [Tooltip("Inclusive range (0–100 scale) a suspect's infection is rolled from at first meeting. " +
             "Keep the max below AnomalyController.FULLY_MUTATED_THRESHOLD (80) unless first-meeting full mutants are desired.")]
    public Vector2 startingInfectionScore = new Vector2(0, 60);
    public Vector2 inspectionScoreIncreasePerDay = new Vector2(5, 20);

    [Tooltip("Daily infection growth applies to every living suspect, seen or not. Suspects the player has " +
             "never seen are capped at this score so they can't appear fully mutated (>= 80) before the " +
             "player has had a chance to process them. Keep at 74 or below to hold first sightings in the " +
             "Quarantine band (mutation score 7); 75+ rounds to 8 (Kill).")]
    [Range(0, 100)] public int neverSeenInfectionCap = 70;

    [Tooltip("Multiplier on daily infection growth for suspects the player has already seen (not killed). " +
             "Doesn't apply on the night of a quarantine verdict, when the score resets instead. Makes passing a suspect " +
             "visibly worse than catching them: with SuspectData.dailyInfectionProgression 8–18, the AnomalyManager " +
             "day roll (1.6–2.4x in Main) and a 1.5x multiplier, a passed suspect gains roughly 2–6 anomaly-budget " +
             "points per night, so passing a borderline (3–4) suspect tends to put them in the Kill band next visit.")]
    [Min(0f)] public float passedInfectionGrowthMultiplier = 2.5f;

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
            record.populationKillPending   = entry.PopulationKillPending;
            record.populationDeathRecorded = entry.PopulationDeathRecorded;
            record.killedOnDay             = entry.KilledOnDay;
            record.isReplacement           = entry.IsReplacement;
            record.quarantinedOnDay        = entry.QuarantinedOnDay;
            record.infectionScore          = entry.InfectionScore;
            record.isLegacyMutant          = entry.IsLegacyMutant;
            record.hasPlayedFullMutantEncounter = entry.HasPlayedFullMutantEncounter;
            record.daysShown               = entry.DaysShown;
            record.lastDayShown            = entry.LastDayShown;
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
    /// Returns the runtime record whose <see cref="SuspectData"/> asset name matches
    /// <paramref name="suspectDataName"/>, or null if not found. Used to resolve a suspect
    /// by name across the network (SuspectData asset references are identical on every peer,
    /// but only the asset name is cheap to send over an RPC).
    /// </summary>
    public SuspectRecord GetRecordByName(string suspectDataName)
    {
        if (string.IsNullOrEmpty(suspectDataName)) return null;
        return records.Find(record => record.SuspectData != null && record.SuspectData.name == suspectDataName);
    }

    /// <summary>
    /// Applies the same quarantine bookkeeping as the server does locally, without persisting to
    /// disk. Called on clients (via ClientRpc) so every peer's local <see cref="records"/> list —
    /// and therefore <see cref="QuarantineBoardController"/> — reflects the quarantine immediately,
    /// instead of only the host that actually mutated and saved the record.
    /// </summary>
    public void ApplyQuarantineFromServer(string suspectDataName, int quarantinedOnDay)
    {
        SuspectRecord record = GetRecordByName(suspectDataName);
        if (record == null) return;

        record.pendingVaccineReset = true;
        record.hasEnteredCity = false;
        record.populationKillPending = false;
        record.quarantinedOnDay = quarantinedOnDay;
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

    // -------------------------------------------------------------------------
    // Legacy Mutants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runtime-only (never persisted) set of suspects that currently have a live full-mutant
    /// instance somewhere in the scene — either at the booth window or roaming the world via a
    /// <see cref="MutantSpawner"/>. Each character is unique: only one full-mutant instance of a
    /// given <see cref="SuspectData"/> may exist at a time. Registered right before that instance's
    /// <see cref="MutantEnemy"/> is enabled and unregistered when its
    /// <see cref="MutantEnemy.OnRemovedFromPlay"/> fires (see <see cref="SuspectCharacter.HandleFullMutantResolved"/>).
    /// </summary>
    private readonly HashSet<SuspectData> _activeFullMutantInstances = new HashSet<SuspectData>();

    /// <summary>True if a full-mutant instance of this suspect is currently active in the scene.</summary>
    public bool IsFullMutantInstanceActive(SuspectData data)
    {
        return data != null && _activeFullMutantInstances.Contains(data);
    }

    /// <summary>
    /// Marks a suspect's full-mutant instance as active so no second instance of the same
    /// character can be spawned (at the booth or by a <see cref="MutantSpawner"/>) while this one
    /// is alive. Server-only.
    /// </summary>
    public void RegisterActiveFullMutant(SuspectData data)
    {
        if (data == null) return;
        _activeFullMutantInstances.Add(data);
    }

    /// <summary>
    /// Clears the active-instance flag for a suspect once their full-mutant instance is removed
    /// from play (killed by fire or fled/despawned). Server-only.
    /// </summary>
    public void UnregisterActiveFullMutant(SuspectData data)
    {
        if (data == null) return;
        _activeFullMutantInstances.Remove(data);
    }

    /// <summary>
    /// Marks a suspect as a legacy mutant — they escaped a full-mutant booth encounter alive
    /// (beaten and fled into the woods rather than killed). Called from
    /// <see cref="SuspectCharacter"/> when its <see cref="MutantEnemy"/> flees instead of dying.
    /// Persists immediately. Server-only.
    /// </summary>
    public void MarkAsLegacyMutant(SuspectData data)
    {
        SuspectRecord record = GetRecord(data);
        if (record == null) return;

        if (!record.isLegacyMutant)
        {
            record.isLegacyMutant = true;
            Debug.Log($"[SuspectRunRecords] '{data.name}' escaped as a full mutant — added to the legacy mutant pool.");
        }

        SaveRecords();
    }

    /// <summary>
    /// Records that this suspect has turned full mutant at the booth and had their full-mutant
    /// conversation, so later booth appearances skip straight to the window attack. Persists
    /// immediately. Server-only.
    /// </summary>
    public void MarkFullMutantEncounterPlayed(SuspectData data)
    {
        SuspectRecord record = GetRecord(data);
        if (record == null || record.hasPlayedFullMutantEncounter) return;

        record.hasPlayedFullMutantEncounter = true;
        Debug.Log($"[SuspectRunRecords] '{data.name}' completed their full-mutant reveal — future booth visits attack immediately.");
        SaveRecords();
    }

    /// <summary>
    /// Permanently removes a suspect from the legacy mutant pool and marks them killed —
    /// called when a legacy mutant is finally destroyed with fire (the only way to
    /// permanently kill a fully-mutated resident). Mirrors the standard kill bookkeeping
    /// (<see cref="SuspectRecord.isKilled"/> / <see cref="SuspectRecord.killedOnDay"/>) so this
    /// resident never re-appears at the booth or in the legacy pool again. Persists immediately.
    /// Server-only.
    /// </summary>
    public void ClearLegacyMutant(SuspectData data)
    {
        SuspectRecord record = GetRecord(data);
        if (record == null) return;

        record.isLegacyMutant = false;
        record.isKilled = true;
        if (record.killedOnDay < 0)
            record.killedOnDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;

        Debug.Log($"[SuspectRunRecords] '{data.name}' permanently destroyed by fire — removed from the legacy mutant pool.");
        SaveRecords();
    }

    /// <summary>
    /// Forces a suspect's persistent infection score directly to the fully-mutated threshold,
    /// skipping normal day-by-day progression entirely. Called from
    /// <see cref="SuspectCharacter.FleeFromWounds"/> when a non-mutant suspect is beaten past its
    /// wounded-flee health at the booth window and escapes instead of being stamped. Guarantees
    /// (via <see cref="AnomalyController.FULLY_MUTATED_THRESHOLD"/> and
    /// <see cref="DailySuspectManager"/>'s existing full-mutant slot injection) that the next time
    /// this suspect is drawn into a shift's lineup, it spawns as a full mutant automatically — no
    /// additional injection logic is needed. Has no effect if the suspect is already fully mutated
    /// or already permanently killed. Persists immediately. Server-only.
    /// </summary>
    public void ForceFullMutation(SuspectData data)
    {
        SuspectRecord record = GetRecord(data);
        if (record == null || record.isKilled) return;

        if (!record.IsFullyMutated)
        {
            record.infectionScore = AnomalyController.FULLY_MUTATED_THRESHOLD;
            Debug.Log($"[SuspectRunRecords] '{data.name}' fled a wounded booth encounter — infection score forced to {record.infectionScore} (fully mutated on next appearance).");
        }

        SaveRecords();
    }

    /// <summary>True when at least one suspect is currently eligible for a legacy-mutant world spawn.</summary>
    public bool HasLegacyMutantCandidates
    {
        get
        {
            foreach (SuspectRecord record in records)
            {
                if (IsEligibleLegacyMutant(record))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Returns a random eligible legacy mutant record, or null if none are available.
    /// Eligible records have <see cref="SuspectRecord.isLegacyMutant"/> set, are not killed, have
    /// a valid <see cref="SuspectData.CharacterPrefab"/> to spawn, and don't already have a live
    /// full-mutant instance elsewhere in the scene (see <see cref="IsFullMutantInstanceActive"/>).
    /// Used by <see cref="MutantSpawner"/> to populate world spawns with previously-escaped residents.
    /// </summary>
    public SuspectRecord GetRandomLegacyMutantRecord()
    {
        List<SuspectRecord> candidates = new List<SuspectRecord>();
        foreach (SuspectRecord record in records)
        {
            if (IsEligibleLegacyMutant(record))
                candidates.Add(record);
        }

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private bool IsEligibleLegacyMutant(SuspectRecord record)
    {
        return record != null
               && record.isLegacyMutant
               && !record.isKilled
               && record.SuspectData != null
               && record.SuspectData.CharacterPrefab != null
               && !IsFullMutantInstanceActive(record.SuspectData);
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
    /// Quarantined suspects have their score reset on the verdict night. The score holds through the next
    /// day, then grows again from that night onward. A fully mutated suspect isn't affected by quarantine.
    /// Seen suspects grow faster (× <see cref="passedInfectionGrowthMultiplier"/>).
    /// Suspects who have never been shown to the player (<see cref="SuspectRecord.daysShown"/> == 0)
    /// still progress every day at the base rate, capped at <see cref="neverSeenInfectionCap"/>.
    /// Checks whether any killed suspect has waited long enough to have their replacement activate.
    /// Persists all changes to disk after advancing.
    /// Call this before DailySuspectManager populates the next shift.
    /// </summary>
    public void AdvanceDayInfection()
    {
        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;

        // Rolled once per day (not once per suspect) so every suspect shares the same
        // "how bad was today" swing — see AnomalyManager.RollInfectionProgressionMultiplier.
        float dayMultiplier = AnomalyManager.Instance != null ? AnomalyManager.Instance.RollInfectionProgressionMultiplier() : 1f;
        Debug.Log($"[SuspectRunRecords] Day {currentDay} infection multiplier rolled: {dayMultiplier:F2}x.");

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

            // Every living suspect progresses daily, seen or not, so later days naturally present
            // more infected suspects. Never-seen suspects are capped below the full-mutant threshold.
            bool neverSeen = record.daysShown <= 0;

            // currentDay is still the day that just ended (CampaignManager applies the new day after this).
            // Quarantine treats only the verdict night: the score resets, and the suspect stays at that
            // score through the next day (quarantine day 2). Growth resumes that night as usual.
            // The quarantinedOnDay check also covers saves, where pendingVaccineReset isn't stored.
            bool quarantinedToday = record.pendingVaccineReset || (currentDay >= 0 && record.quarantinedOnDay == currentDay);

            if (quarantinedToday)
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
                int baseIncrease = UnityEngine.Random.Range(range.x, range.y + 1);
                float statusMultiplier = neverSeen ? 1f : passedInfectionGrowthMultiplier;
                int increase = Mathf.RoundToInt(baseIncrease * dayMultiplier * statusMultiplier);
                int cap = neverSeen
                    ? Mathf.Max(record.infectionScore, Mathf.Min(neverSeenInfectionCap, AnomalyController.FULLY_MUTATED_THRESHOLD - 1))
                    : 100;
                record.infectionScore = Mathf.Clamp(record.infectionScore + increase, 0, cap);
                Debug.Log($"[SuspectRunRecords] '{record.SuspectData.name}' infection +{increase} (base {baseIncrease} × {dayMultiplier:F2} × {statusMultiplier:F2}{(neverSeen ? " never-seen" : " passed")}) → {record.infectionScore}{(record.IsFullyMutated ? " [FULLY MUTATED]" : "")}.");
            }
        }

        // Flush all changes (including updated infection scores) to disk.
        SaveRecords();
    }
}
