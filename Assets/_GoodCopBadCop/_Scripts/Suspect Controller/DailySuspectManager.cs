using System;
using System.Collections.Generic;
using UnityEngine;

public class DailySuspectManager : MonoBehaviour
{
    [SerializeField] private SuspectSet allSuspects;
    public List<SuspectData> shiftSuspects;
    [SerializeField] private Vector2 suspectsPerShift;

    [Header("Mutant Intruder")]
    [SerializeField] private MutantLineupSet lineupMutants;
    [SerializeField] private MutantIntruderData mutantIntruderData;
    [SerializeField, Range(0f, 1f)] private float mutantSpawnChance = 0.2f;

    [Header("Doppelganger")]
    [SerializeField] private DoppelgangerLineupSet lineupDoppelgangers;

    public static DailySuspectManager Instance;

    /// <summary>
    /// SINGLE SOURCE OF TRUTH for "how many suspects the player must process this shift" — the
    /// number shown by the "Process N subjects" objective counter (<see cref="DayBase"/>) and the
    /// Task Page total (<see cref="ProcessResidentsTask"/>). Deliberately EXCLUDES injected mutant
    /// intruder slots (<see cref="_mutantSlotIndices"/>): mutants are a random combat threat added
    /// on top of the shift, never a "suspect to process", and must never affect this total or the
    /// displayed X/Y count. Doppelganger and full-mutant slots DO count — both stand in for a real
    /// suspect and are resolved through the normal folder verdict flow.
    /// Returns 0 before the lineup has been populated for the day.
    /// </summary>
    public int TotalSuspectsThisShift => shiftSuspects.Count - _mutantSlotIndices.Count;

    /// <summary>
    /// Total occupied lineup slots this shift, INCLUDING injected mutant intruder slots. This is
    /// the number of times the lineup actually has to advance before the shift can end — mutants
    /// still occupy a real slot and must still be resolved (killed/fled/etc.) even though they are
    /// excluded from <see cref="TotalSuspectsThisShift"/>. Used only by
    /// <see cref="ShiftManager.SetNextSuspectReady"/> to know when the lineup is exhausted.
    /// </summary>
    public int TotalLineupSlotsThisShift => shiftSuspects.Count;

    /// <summary>
    /// True once <see cref="PopulateShiftCharacters"/> has finished running for the current shift.
    /// Callers should treat <see cref="TotalSuspectsThisShift"/> as not-yet-authoritative until this
    /// is true, rather than falling back to a different, secondary count.
    /// </summary>
    public bool IsLineupPopulated { get; private set; }

    private readonly HashSet<int> _mutantSlotIndices = new HashSet<int>();
    private readonly Dictionary<int, DoppelgangerData> _doppelgangerSlots = new Dictionary<int, DoppelgangerData>();

    /// <summary>
    /// Tracks which lineup slot indices belong to full-mutant civilians — either freshly
    /// fully-mutated (<see cref="SuspectRecord.IsFullyMutated"/>) or a returning
    /// <see cref="SuspectRecord.isLegacyMutant"/> who previously escaped a full-mutant encounter
    /// and hasn't been permanently killed by fire yet.
    /// Populated by <see cref="InjectFullMutantSlots"/> after all other slot injections.
    /// The corresponding <see cref="SuspectData"/> for each index always has
    /// <see cref="SuspectData.fullMutantDialogue"/> assigned.
    /// </summary>
    private readonly HashSet<int> _fullMutantSlotIndices = new HashSet<int>();

    /// <summary>
    /// Tracks which lineup slot indices are replacement suspects (killed civilians that have
    /// re-activated after their replacement window elapsed). These suspects spawn normally
    /// but are initialized via InitializeAsReplacement on SuspectCharacter.
    /// </summary>
    private readonly HashSet<int> _replacementSlotIndices = new HashSet<int>();

    /// <summary>
    /// When assigned, replaces the default random population logic entirely.
    /// The delegate is responsible for populating <see cref="shiftSuspects"/> directly.
    /// Set by a day subclass (e.g. Day_01) and cleared when that day deactivates.
    /// </summary>
    public Action PopulateSuspectOverride;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Replaces the active suspect pool for the upcoming shift.
    /// Called by CampaignManager.AdvanceDay before the shift starts.
    /// </summary>
    public void SetSuspectSet(SuspectSet suspectSet)
    {
        if (suspectSet == null)
        {
            Debug.LogWarning("[DailySuspectManager] SetSuspectSet called with null SuspectSet — keeping current pool.");
            return;
        }

        allSuspects = suspectSet;
        Debug.Log($"[DailySuspectManager] Suspect pool updated to '{suspectSet.name}'.");
    }

    /// <summary>
    /// Replaces the active mutant lineup pool for the upcoming shift.
    /// Call from a day subclass to change which mutants can appear in the lineup.
    /// </summary>
    public void SetMutantLineupSet(MutantLineupSet mutantSet)
    {
        if (mutantSet == null)
        {
            Debug.LogWarning("[DailySuspectManager] SetMutantLineupSet called with null MutantLineupSet — keeping current pool.");
            return;
        }

        lineupMutants = mutantSet;
        Debug.Log($"[DailySuspectManager] Mutant lineup pool updated to '{mutantSet.name}'.");
    }

    /// <summary>
    /// Replaces the active doppelganger lineup pool for the upcoming shift.
    /// Call from a day subclass to control which doppelgangers can appear.
    /// Set <see cref="DoppelgangerLineupSet.spawnChance"/> to 0 on the asset for Days 1–5.
    /// </summary>
    public void SetDoppelgangerLineupSet(DoppelgangerLineupSet doppelgangerSet)
    {
        if (doppelgangerSet == null)
        {
            Debug.LogWarning("[DailySuspectManager] SetDoppelgangerLineupSet called with null DoppelgangerLineupSet — keeping current pool.");
            return;
        }

        lineupDoppelgangers = doppelgangerSet;
        Debug.Log($"[DailySuspectManager] Doppelganger lineup pool updated to '{doppelgangerSet.name}'.");
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += PopulateShiftCharacters;
    }

    private void PopulateShiftCharacters()
    {
        shiftSuspects.Clear();
        ResetSlotTracking();
        IsLineupPopulated = false;

        if (PopulateSuspectOverride != null)
        {
            PopulateSuspectOverride.Invoke();
            RemoveInvalidSuspects();
            Debug.Log($"[DailySuspectManager] Shift populated via override — {shiftSuspects.Count} suspect(s).");
            InjectMutantSlots();
            InjectDoppelgangerSlots();
            InjectForcedFullMutantSlots();
            InjectFullMutantSlots();
            IsLineupPopulated = true;
            Debug.Log($"[DailySuspectManager] TotalSuspectsThisShift (override) = {TotalSuspectsThisShift}.");
            return;
        }

        int suspectAmount = GetSuspectAmountForToday();

        List<SuspectData> randomSuspects = GetRandomSuspects(suspectAmount);
        foreach (SuspectData suspectData in randomSuspects)
        {
            int slotIndex = shiftSuspects.Count;
            shiftSuspects.Add(suspectData);

            // If this suspect is a replacement, register their slot so SuspectController
            // can call InitializeAsReplacement instead of the normal init path.
            SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
            if (record != null && record.isReplacement)
            {
                _replacementSlotIndices.Add(slotIndex);
                Debug.Log($"[DailySuspectManager] '{suspectData.name}' replacement slot registered at index {slotIndex}.");
            }
        }

        InjectMutantSlots();
        InjectDoppelgangerSlots();
        InjectForcedFullMutantSlots();
        InjectFullMutantSlots();
        IsLineupPopulated = true;
        Debug.Log($"[DailySuspectManager] TotalSuspectsThisShift = {TotalSuspectsThisShift} (base draw request was {suspectAmount}).");
    }

    /// <summary>
    /// Returns how many BASE suspects to draw for today's lineup, before any mutant/doppelganger/
    /// full-mutant injection. Uses the active day's <see cref="DayBase.SuspectsToProcess"/> when
    /// configured (>0, the normal case — default 5). Falls back to the legacy random
    /// <see cref="suspectsPerShift"/> range only when the active day explicitly sets its suspect
    /// quota to 0.
    ///
    /// This is a DRAW REQUEST only — NOT the shift's total suspect count. The final total (after
    /// injection) is <see cref="TotalSuspectsThisShift"/>, the single source of truth every other
    /// system (task display, objective counter, end-of-shift check) must read instead.
    /// </summary>
    private int GetSuspectAmountForToday()
    {
        DayBase activeDay = CampaignManager.Instance != null ? CampaignManager.Instance.ActiveDay : null;
        if (activeDay != null && activeDay.SuspectsToProcess > 0)
            return activeDay.SuspectsToProcess;

        return (int)UnityEngine.Random.Range(suspectsPerShift.x, suspectsPerShift.y);
    }

    /// <summary>
    /// Inserts null sentinel entries into shiftSuspects at random positions.
    /// The count is derived from mutantSpawnChance as a percentage of the normal suspect count.
    /// Only runs from Day 2 onwards.
    /// </summary>
    private void InjectMutantSlots()
    {
        if (CampaignManager.Instance != null && CampaignManager.Instance.CurrentDay < 2)
        {
            Debug.Log("[DailySuspectManager] Day 1 — mutant lineup injection skipped.");
            return;
        }

        if (lineupMutants == null || mutantIntruderData == null)
        {
            if (lineupMutants != null && mutantIntruderData == null)
                Debug.LogWarning("[DailySuspectManager] lineupMutants is assigned but mutantIntruderData is null — no mutants will spawn.");
            return;
        }

        if (lineupMutants.mutants == null || lineupMutants.mutants.Count == 0)
        {
            Debug.LogWarning($"[DailySuspectManager] MutantLineupSet '{lineupMutants.name}' has no mutant prefabs — no mutants will spawn.");
            return;
        }

        int normalCount = shiftSuspects.Count;
        if (normalCount == 0) return;

        int mutantCount = Mathf.RoundToInt(normalCount * mutantSpawnChance);
        if (mutantCount <= 0) return;

        for (int i = 0; i < mutantCount; i++)
        {
            int insertIndex = UnityEngine.Random.Range(0, shiftSuspects.Count + 1);
            shiftSuspects.Insert(insertIndex, null);
            _mutantSlotIndices.Add(insertIndex);

            // Shift any previously recorded mutant indices at or above the insertion point,
            // excluding the one we just added.
            ShiftHashSetIndicesAfterInsert(insertIndex, _mutantSlotIndices);
            ShiftDoppelgangerSlotsAfterInsert(insertIndex);
            ShiftReplacementSlotsAfterInsert(insertIndex);
        }

        Debug.Log($"[DailySuspectManager] Lineup: {normalCount} suspect(s) + {mutantCount} mutant intruder(s) = {shiftSuspects.Count} total slot(s).");
    }

    /// <summary>
    /// Inserts doppelganger entries into shiftSuspects at random positions after mutant injection.
    /// Uses the target suspect's SuspectData so the existing prefab spawning path works unchanged.
    /// The injected slot is tracked in _doppelgangerSlots so IsDoppelgangerSlot can flag it.
    /// Only runs from Day 2 onwards and only when the spawn chance roll succeeds.
    /// </summary>
    private void InjectDoppelgangerSlots()
    {
        if (CampaignManager.Instance != null && CampaignManager.Instance.CurrentDay < 2)
        {
            Debug.Log("[DailySuspectManager] Day 1 — doppelganger injection skipped.");
            return;
        }

        if (lineupDoppelgangers == null)
            return;

        if (lineupDoppelgangers.doppelgangers == null || lineupDoppelgangers.doppelgangers.Count == 0)
        {
            Debug.LogWarning($"[DailySuspectManager] DoppelgangerLineupSet '{lineupDoppelgangers.name}' has no entries — skipping injection.");
            return;
        }

        if (UnityEngine.Random.value > lineupDoppelgangers.spawnChance)
        {
            Debug.Log("[DailySuspectManager] Doppelganger spawn chance not met — skipping injection.");
            return;
        }

        DoppelgangerData doppelgangerData = lineupDoppelgangers.GetRandom();
        if (doppelgangerData == null)
            return;

        if (doppelgangerData.targetSuspect == null)
        {
            Debug.LogWarning($"[DailySuspectManager] DoppelgangerData '{doppelgangerData.name}' has no targetSuspect assigned — skipping injection.");
            return;
        }

        if (doppelgangerData.targetSuspect.CharacterPrefab == null)
        {
            Debug.LogWarning($"[DailySuspectManager] DoppelgangerData target '{doppelgangerData.targetSuspect.name}' has no CharacterPrefab — skipping injection.");
            return;
        }

        int insertIndex = UnityEngine.Random.Range(0, shiftSuspects.Count + 1);

        // Insert the target's SuspectData so the existing prefab spawn path resolves the prefab normally.
        shiftSuspects.Insert(insertIndex, doppelgangerData.targetSuspect);

        // Shift all existing mutant and doppelganger slot indices that sit at or above the insertion point.
        ShiftHashSetIndicesAfterInsert(insertIndex, _mutantSlotIndices);
        ShiftDoppelgangerSlotsAfterInsert(insertIndex);
        ShiftReplacementSlotsAfterInsert(insertIndex);

        _doppelgangerSlots[insertIndex] = doppelgangerData;

        Debug.Log($"[DailySuspectManager] Doppelganger of '{doppelgangerData.targetSuspect.name}' injected at lineup index {insertIndex}. Total slots: {shiftSuspects.Count}.");
    }

    /// <summary>
    /// Shifts all entries in the given HashSet that are >= insertIndex up by one,
    /// excluding the entry at exactly insertIndex (which was just added and must not move).
    /// </summary>
    private static void ShiftHashSetIndicesAfterInsert(int insertIndex, HashSet<int> indices)
    {
        List<int> toShift = new List<int>();
        foreach (int idx in indices)
        {
            if (idx != insertIndex && idx >= insertIndex)
                toShift.Add(idx);
        }

        foreach (int idx in toShift)
        {
            indices.Remove(idx);
            indices.Add(idx + 1);
        }
    }

    /// <summary>
    /// Shifts all doppelganger slot keys that are >= insertIndex up by one.
    /// Called before the new entry is written to _doppelgangerSlots.
    /// </summary>
    private void ShiftDoppelgangerSlotsAfterInsert(int insertIndex)
    {
        List<int> toShift = new List<int>();
        foreach (int idx in _doppelgangerSlots.Keys)
        {
            if (idx >= insertIndex)
                toShift.Add(idx);
        }

        foreach (int idx in toShift)
        {
            DoppelgangerData data = _doppelgangerSlots[idx];
            _doppelgangerSlots.Remove(idx);
            _doppelgangerSlots[idx + 1] = data;
        }
    }

    /// <summary>
    /// Shifts all replacement slot indices that are >= insertIndex up by one.
    /// Called after a mutant or doppelganger is inserted ahead of existing replacement slots.
    /// </summary>
    private void ShiftReplacementSlotsAfterInsert(int insertIndex)
    {
        ShiftHashSetIndicesAfterInsert(insertIndex, _replacementSlotIndices);
    }

    /// <summary>Clears all slot index tracking for the new shift.</summary>
    private void ResetSlotTracking()
    {
        _mutantSlotIndices.Clear();
        _doppelgangerSlots.Clear();
        _fullMutantSlotIndices.Clear();
        _replacementSlotIndices.Clear();
    }

    /// <summary>
    /// Returns a random mutant prefab and config from the current pool, ignoring slot indices.
    /// Used by debug tools to force a mutant spawn outside of the normal lineup injection flow.
    /// Returns false (with null outs) if no pool or config is assigned.
    /// </summary>
    public bool TryGetRandomMutant(out MutantSuspectBehaviour selectedPrefab, out MutantIntruderData data)
    {
        data = mutantIntruderData;
        selectedPrefab = null;

        if (lineupMutants == null || mutantIntruderData == null)
        {
            Debug.LogWarning("[DailySuspectManager] TryGetRandomMutant: lineupMutants or mutantIntruderData is not assigned.");
            return false;
        }

        selectedPrefab = lineupMutants.GetRandom();
        return selectedPrefab != null;
    }

    /// <summary>
    /// Returns a random DoppelgangerData from the current pool, ignoring slot indices.
    /// Used by debug tools to force a doppelganger spawn outside of the normal injection flow.
    /// Returns false with a null out if no pool is assigned or the pool is empty.
    /// </summary>
    public bool TryGetRandomDoppelganger(out DoppelgangerData data)
    {
        data = null;

        if (lineupDoppelgangers == null)
        {
            Debug.LogWarning("[DailySuspectManager] TryGetRandomDoppelganger: lineupDoppelgangers is not assigned.");
            return false;
        }

        data = lineupDoppelgangers.GetRandom();
        return data != null;
    }

    /// <summary>
    /// Returns true if the given lineup index is a mutant intrusion slot.
    /// Outputs the randomly selected prefab from the pool and the shared config data.
    /// </summary>
    public bool IsMutantSlot(int lineupIndex, out MutantSuspectBehaviour selectedPrefab, out MutantIntruderData data)
    {
        data = mutantIntruderData;
        selectedPrefab = null;

        if (!_mutantSlotIndices.Contains(lineupIndex) || lineupMutants == null)
            return false;

        selectedPrefab = lineupMutants.GetRandom();
        return selectedPrefab != null;
    }

    /// <summary>
    /// Returns true if the given lineup index is a doppelganger slot.
    /// Outputs the DoppelgangerData that controls anomaly loadout and visual modifiers.
    /// </summary>
    public bool IsDoppelgangerSlot(int lineupIndex, out DoppelgangerData data)
    {
        return _doppelgangerSlots.TryGetValue(lineupIndex, out data);
    }

    /// <summary>
    /// Returns true if the given lineup index is a replacement slot — a killed suspect
    /// whose replacement version has activated after the replacement window elapsed.
    /// These are spawned normally but initialized via SuspectCharacter.InitializeAsReplacement.
    /// </summary>
    public bool IsReplacementSlot(int lineupIndex)
    {
        return _replacementSlotIndices.Contains(lineupIndex);
    }

    /// <summary>
    /// Scans the final populated lineup and registers any slot whose suspect has crossed the
    /// fully-mutated threshold, OR is a returning <see cref="SuspectRecord.isLegacyMutant"/>
    /// (previously escaped a full-mutant encounter and hasn't been permanently killed by fire) —
    /// AND has a <see cref="SuspectData.fullMutantDialogue"/> assigned. Skips any suspect that
    /// already has a live full-mutant instance elsewhere in the scene (e.g. currently roaming as
    /// a <see cref="MutantSpawner"/> world spawn) — see <see cref="SuspectRunRecords.IsFullMutantInstanceActive"/>
    /// — since only one instance of a given character may exist at a time.
    /// Must run after all other injections so indices are stable.
    /// Doppelganger, mutant-intruder, and replacement slots are never double-flagged.
    /// </summary>
    /// <summary>
    /// Demo-only override, driven by <see cref="DayBase.ForceEarlyFullMutants"/> /
    /// <see cref="DayBase.ForcedFullMutantCount"/> on the active day. Picks 1–2 random suspects
    /// the player has already seen in a previous shift (<see cref="SuspectRecord.daysShown"/> &gt; 0)
    /// and who were never sent to quarantine (<see cref="SuspectRecord.quarantinedOnDay"/> &lt; 0),
    /// forces their infection score to the fully-mutated threshold via
    /// <see cref="SuspectRunRecords.ForceFullMutation"/>, and inserts each into today's lineup at a
    /// random slot — mirroring <see cref="InjectDoppelgangerSlots"/>. Must run after mutant-intruder
    /// and doppelganger injection (so slot indices are stable) and before
    /// <see cref="InjectFullMutantSlots"/>, whose normal scan then flags these slots automatically
    /// now that each candidate reads as fully mutated.
    /// </summary>
    private void InjectForcedFullMutantSlots()
    {
        DayBase activeDay = CampaignManager.Instance != null ? CampaignManager.Instance.ActiveDay : null;
        if (activeDay == null || !activeDay.ForceEarlyFullMutants) return;

        SuspectRunRecords runRecords = SuspectRunRecords.Instance;
        if (runRecords == null)
        {
            Debug.LogWarning("[DailySuspectManager] ForceEarlyFullMutants is enabled but SuspectRunRecords is not available — skipping.");
            return;
        }

        List<SuspectRecord> candidates = new List<SuspectRecord>();
        foreach (SuspectRecord record in runRecords.Records)
        {
            if (record == null || record.SuspectData == null) continue;
            if (record.isKilled || record.isReplacement) continue;
            if (record.daysShown <= 0) continue;                              // must have been seen previously
            if (record.quarantinedOnDay >= 0) continue;                       // must never have been quarantined
            if (record.IsFullyMutated || record.isLegacyMutant) continue;     // already eligible on its own
            if (record.SuspectData.fullMutantDialogue == null) continue;
            if (record.SuspectData.CharacterPrefab == null) continue;
            if (runRecords.IsFullMutantInstanceActive(record.SuspectData)) continue;
            if (shiftSuspects.Contains(record.SuspectData)) continue;         // avoid a duplicate same-day appearance

            candidates.Add(record);
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[DailySuspectManager] ForceEarlyFullMutants enabled but no eligible previously-seen, never-quarantined suspects were found.");
            return;
        }

        int desiredCount = Mathf.Clamp(activeDay.ForcedFullMutantCount, 1, 2);
        int injected = 0;

        while (injected < desiredCount && candidates.Count > 0)
        {
            int pick = UnityEngine.Random.Range(0, candidates.Count);
            SuspectRecord chosen = candidates[pick];
            candidates.RemoveAt(pick);

            runRecords.ForceFullMutation(chosen.SuspectData);

            int insertIndex = UnityEngine.Random.Range(0, shiftSuspects.Count + 1);
            shiftSuspects.Insert(insertIndex, chosen.SuspectData);

            ShiftHashSetIndicesAfterInsert(insertIndex, _mutantSlotIndices);
            ShiftDoppelgangerSlotsAfterInsert(insertIndex);
            ShiftReplacementSlotsAfterInsert(insertIndex);
            ShiftHashSetIndicesAfterInsert(insertIndex, _fullMutantSlotIndices);

            injected++;
            Debug.Log($"[DailySuspectManager] Demo override — forced '{chosen.SuspectData.name}' into today's lineup as an early full mutant (slot {insertIndex}).");
        }
    }

    private void InjectFullMutantSlots()
    {
        SuspectRunRecords runRecords = SuspectRunRecords.Instance;

        for (int i = 0; i < shiftSuspects.Count; i++)
        {
            SuspectData suspect = shiftSuspects[i];
            if (suspect == null) continue;
            if (_mutantSlotIndices.Contains(i)) continue;
            if (_doppelgangerSlots.ContainsKey(i)) continue;
            if (_replacementSlotIndices.Contains(i)) continue;

            if (suspect.fullMutantDialogue == null) continue;

            SuspectRecord record = runRecords?.GetRecord(suspect);
            if (record == null || !(record.IsFullyMutated || record.isLegacyMutant)) continue;

            if (runRecords.IsFullMutantInstanceActive(suspect))
            {
                Debug.Log($"[DailySuspectManager] '{suspect.name}' already has a live full-mutant instance elsewhere — slot {i} not flagged as full mutant.");
                continue;
            }

            _fullMutantSlotIndices.Add(i);
            Debug.Log($"[DailySuspectManager] '{suspect.name}' is {(record.IsFullyMutated ? "fully mutated" : "a legacy mutant")} — slot {i} flagged as full mutant.");
        }
    }

    /// <summary>
    /// Returns true if the given lineup index is a fully-mutated civilian slot.
    /// Outputs the suspect's <see cref="SuspectData"/> so the caller can read it directly.
    /// </summary>
    public bool IsFullMutantSlot(int lineupIndex, out SuspectData suspectData)
    {
        suspectData = null;
        if (!_fullMutantSlotIndices.Contains(lineupIndex)) return false;
        suspectData = lineupIndex < shiftSuspects.Count ? shiftSuspects[lineupIndex] : null;
        return suspectData != null;
    }

    public IEnumerable<Texture> GetIdPhotoPool()
    {
        IEnumerable<SuspectData> source = allSuspects != null && allSuspects.suspects != null
            ? allSuspects.suspects
            : shiftSuspects;

        if (source == null)
            yield break;

        foreach (SuspectData suspect in source)
        {
            if (suspect != null && suspect.IDPhoto != null)
                yield return suspect.IDPhoto;
        }
    }

    private List<SuspectData> GetRandomSuspects(int amount)
    {
        List<SuspectData> randomSuspects = new List<SuspectData>();
        List<SuspectData> availableSuspects = new List<SuspectData>();

        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;

        foreach (SuspectData suspect in allSuspects.suspects)
        {
            if (suspect == null)
            {
                Debug.LogWarning($"[DailySuspectManager] SuspectSet '{allSuspects.name}' contains a null SuspectData entry — skipping.");
                continue;
            }

            if (suspect.CharacterPrefab == null)
            {
                Debug.LogWarning($"[DailySuspectManager] SuspectData '{suspect.name}' has no CharacterPrefab assigned — skipping.");
                continue;
            }

            SuspectRecord runRecord = SuspectRunRecords.Instance?.GetRecord(suspect);

            // Permanently exclude suspects that were killed this session — UNLESS their
            // replacement has activated, in which case they re-enter the pool.
            if (runRecord != null && runRecord.isKilled)
            {
                if (runRecord.isReplacement)
                {
                    Debug.Log($"[DailySuspectManager] '{suspect.name}' re-entering pool as replacement.");
                    // Fall through — included as an available suspect.
                }
                else
                {
                    Debug.Log($"[DailySuspectManager] '{suspect.name}' excluded — killed.");
                    continue;
                }
            }

            // Exclude suspects serving a one-day quarantine cooldown (quarantined yesterday).
            if (runRecord != null && runRecord.IsOnQuarantineCooldown(currentDay))
            {
                Debug.Log($"[DailySuspectManager] '{suspect.name}' excluded — on quarantine cooldown (quarantined on day {runRecord.quarantinedOnDay}).");
                continue;
            }

            availableSuspects.Add(suspect);
        }

        for (int i = 0; i < amount && availableSuspects.Count > 0; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, availableSuspects.Count);
            randomSuspects.Add(availableSuspects[randomIndex]);
            availableSuspects.RemoveAt(randomIndex);
        }

        return randomSuspects;
    }

    /// <summary>
    /// Removes any entries from <see cref="shiftSuspects"/> that are null or have no
    /// <see cref="SuspectData.CharacterPrefab"/> assigned. Intended for use after an
    /// override delegate populates the list, where data validity cannot be guaranteed.
    /// </summary>
    private void RemoveInvalidSuspects()
    {
        for (int i = shiftSuspects.Count - 1; i >= 0; i--)
        {
            SuspectData suspect = shiftSuspects[i];

            if (suspect == null)
            {
                Debug.LogWarning($"[DailySuspectManager] Override added a null SuspectData at index {i} — removing.");
                shiftSuspects.RemoveAt(i);
                continue;
            }

            if (suspect.CharacterPrefab == null)
            {
                Debug.LogWarning($"[DailySuspectManager] Override added SuspectData '{suspect.name}' with no CharacterPrefab at index {i} — removing.");
                shiftSuspects.RemoveAt(i);
            }
        }
    }
}
