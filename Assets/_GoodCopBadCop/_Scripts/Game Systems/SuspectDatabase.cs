using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using System;

[Serializable]
public class SuspectRecord
{
    public SuspectData Data;
    public CharacterStatus Status;
    public DateTime LastExitTime;
    public bool HasBeenSeen;
}

public class SuspectDatabase : MonoBehaviour
{
    public static SuspectDatabase Instance;

    [SerializeField] private SuspectSet allSuspects;

    private Dictionary<SuspectData, SuspectRecord> records = new();

    private void Awake()
    {
        Instance = this;
        foreach (var suspect in allSuspects.suspects)
        {
            records[suspect] = new SuspectRecord
            {
                Data = suspect,
                Status = CharacterStatus.Resident,
                LastExitTime = DateTime.MinValue,
                HasBeenSeen = false
            };
        }
    }

    public SuspectRecord GetRecord(SuspectData data)
    {
        return records[data];
    }

    public List<SuspectRecord> GetAllRecords()
    {
        return records.Values.ToList();
    }
}