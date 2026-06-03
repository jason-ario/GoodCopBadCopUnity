using System;
using System.Collections.Generic;
using UnityEngine;

public class DailySuspectManager : MonoBehaviour
{
    [SerializeField] private SuspectSet allSuspects;
    public List<SuspectData> shiftSuspects;
    [SerializeField] private Vector2 suspectsPerShift;
    
    public static DailySuspectManager Instance;

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

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += PopulateShiftCharacters;
    }

    private void PopulateShiftCharacters()
    {
        shiftSuspects.Clear();

        if (PopulateSuspectOverride != null)
        {
            PopulateSuspectOverride.Invoke();
            Debug.Log($"[DailySuspectManager] Shift populated via override — {shiftSuspects.Count} suspect(s).");
            return;
        }

        int suspectAmount = (int)UnityEngine.Random.Range(suspectsPerShift.x, suspectsPerShift.y);
        
        //Get random suspects and populate the shift characters
        List<SuspectData> randomSuspects = GetRandomSuspects(suspectAmount);
        foreach (SuspectData suspectData in randomSuspects)
        {
            shiftSuspects.Add(suspectData);
        }
    }

    private List<SuspectData> GetRandomSuspects(int amount)
    {
        List<SuspectData> randomSuspects = new List<SuspectData>();
        List<SuspectData> availableSuspects = new List<SuspectData>(allSuspects.suspects);
        
        for (int i = 0; i < amount && availableSuspects.Count > 0; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, availableSuspects.Count);
            randomSuspects.Add(availableSuspects[randomIndex]);
            availableSuspects.RemoveAt(randomIndex);
        }
        
        return randomSuspects;
    }
}