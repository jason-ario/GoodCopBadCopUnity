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

    public static DailySuspectManager Instance;

    private readonly HashSet<int> _mutantSlotIndices = new HashSet<int>();

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

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += PopulateShiftCharacters;
    }

    private void PopulateShiftCharacters()
    {
        shiftSuspects.Clear();
        ResetMutantSlots();

        if (PopulateSuspectOverride != null)
        {
            PopulateSuspectOverride.Invoke();
            RemoveInvalidSuspects();
            Debug.Log($"[DailySuspectManager] Shift populated via override — {shiftSuspects.Count} suspect(s).");
            InjectMutantSlots();
            return;
        }

        int suspectAmount = (int)UnityEngine.Random.Range(suspectsPerShift.x, suspectsPerShift.y);

        List<SuspectData> randomSuspects = GetRandomSuspects(suspectAmount);
        foreach (SuspectData suspectData in randomSuspects)
        {
            shiftSuspects.Add(suspectData);
        }

        InjectMutantSlots();
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

            // Shift any previously recorded indices that are at or above the insertion point.
            ShiftMutantIndicesAfterInsert(insertIndex);
        }

        Debug.Log($"[DailySuspectManager] Lineup: {normalCount} suspect(s) + {mutantCount} mutant intruder(s) = {shiftSuspects.Count} total slot(s).");
    }

    /// <summary>
    /// After inserting a null at insertIndex, all existing recorded mutant slot indices
    /// at or above that position shift up by one to remain accurate.
    /// </summary>
    private void ShiftMutantIndicesAfterInsert(int insertIndex)
    {
        List<int> toShift = new List<int>();
        foreach (int idx in _mutantSlotIndices)
        {
            if (idx != insertIndex && idx >= insertIndex)
                toShift.Add(idx);
        }

        foreach (int idx in toShift)
        {
            _mutantSlotIndices.Remove(idx);
            _mutantSlotIndices.Add(idx + 1);
        }
    }

    /// <summary>Clears the mutant slot index tracking for the new shift.</summary>
    private void ResetMutantSlots()
    {
        _mutantSlotIndices.Clear();
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

    private List<SuspectData> GetRandomSuspects(int amount)
    {
        List<SuspectData> randomSuspects = new List<SuspectData>();
        List<SuspectData> availableSuspects = new List<SuspectData>();

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