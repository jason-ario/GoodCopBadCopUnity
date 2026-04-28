using System;
using System.Collections.Generic;
using UnityEngine;

public class DailySuspectManager : MonoBehaviour
{
    [SerializeField] private SuspectSet allSuspects;
    public List<SuspectData> shiftSuspects;
    [SerializeField] private Vector2 suspectsPerShift;
    
    public static DailySuspectManager Instance;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += PopulateShiftCharacters;
    }

    private void PopulateShiftCharacters()
    {
        int suspectAmount = (int)UnityEngine.Random.Range(suspectsPerShift.x, suspectsPerShift.y);
        
        //Get random suspects and populate the shift characters
        shiftSuspects.Clear();
        
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