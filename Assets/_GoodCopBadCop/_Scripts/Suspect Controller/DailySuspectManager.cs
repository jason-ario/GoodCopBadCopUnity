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

    private readonly HashSet<int> _mutantSlotIndices = new HashSet<int>();
    private readonly Dictionary<int, DoppelgangerData> _doppelgangerSlots = new Dictionary<int, DoppelgangerData>();

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

        if (PopulateSuspectOverride != null)
        {
            PopulateSuspectOverride.Invoke();
            RemoveInvalidSuspects();
            Debug.Log($"[DailySuspectManager] Shift populated via override — {shiftSuspects.Count} suspect(s).");
            InjectMutantSlots();
            InjectDoppelgangerSlots();
            return;
        }

        int suspectAmount = (int)UnityEngine.Random.Range(suspectsPerShift.x, suspectsPerShift.y);

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
