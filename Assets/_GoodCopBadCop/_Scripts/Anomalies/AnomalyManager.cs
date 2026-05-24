using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct AnomalyLockEntry
{
    public UnityEngine.Object anomalyType;
    public bool locked;
}

public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;

    [Header("Category Locks")]
    [Tooltip("Locks the entire category from being spawned on suspects. Has no effect on the checklist.")]
    public bool mutationAnomaliesLocked;
    public bool behaviorAnomaliesLocked;
    public bool biologicalAnomaliesLocked;
    public bool documentationAnomaliesLocked;
    public bool environmentAnomaliesLocked;

    [Header("Mutation Anomalies")]
    [SerializeField] private List<AnomalyLockEntry> _mutationAnomalyLocks = new List<AnomalyLockEntry>();

    [Header("Behavior Anomalies")]
    [SerializeField] private List<AnomalyLockEntry> _behaviorAnomalyLocks = new List<AnomalyLockEntry>();

    [Header("Biological Anomalies")]
    [SerializeField] private List<AnomalyLockEntry> _biologicalAnomalyLocks = new List<AnomalyLockEntry>();

    [Header("Documentation Anomalies")]
    [SerializeField] private List<AnomalyLockEntry> _documentationAnomalyLocks = new List<AnomalyLockEntry>();

    [Header("Environment Anomalies")]
    [SerializeField] private List<AnomalyLockEntry> _environmentAnomalyLocks = new List<AnomalyLockEntry>();

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Reads anomaly category unlock flags from the active save slot and writes them
    /// into the category lock fields. The Inspector booleans remain freely editable
    /// after this call — this is a one-time populate, not a continuous override.
    /// Call once per day before suspects are spawned.
    /// </summary>
    public void ApplyUnlocksFromSave()
    {
        if (SaveDataManager.Instance == null) return;

        mutationAnomaliesLocked      = !SaveDataManager.Instance.MutationAnomaliesUnlocked;
        behaviorAnomaliesLocked      = !SaveDataManager.Instance.BehaviorAnomaliesUnlocked;
        biologicalAnomaliesLocked    = !SaveDataManager.Instance.BiologicalAnomaliesUnlocked;
        documentationAnomaliesLocked = !SaveDataManager.Instance.DocumentationAnomaliesUnlocked;
        environmentAnomaliesLocked   = !SaveDataManager.Instance.EnvironmentAnomaliesUnlocked;
    }

    /// <summary>
    /// Marks only documentation anomalies as unlocked in the save slot, then applies
    /// the result to the live category lock fields. Used by Day_01. Persists immediately.
    /// </summary>
    public void UnlockDocumentationOnly()
    {
        if (SaveDataManager.Instance != null)
            SaveDataManager.Instance.DocumentationAnomaliesUnlocked = true;

        ApplyUnlocksFromSave();
    }

    /// <summary>Unlocks all anomaly categories in the save slot and applies immediately.</summary>
    public void UnlockAll()
    {
        if (SaveDataManager.Instance != null)
        {
            SaveDataManager.Instance.MutationAnomaliesUnlocked      = true;
            SaveDataManager.Instance.BehaviorAnomaliesUnlocked      = true;
            SaveDataManager.Instance.BiologicalAnomaliesUnlocked    = true;
            SaveDataManager.Instance.DocumentationAnomaliesUnlocked = true;
            SaveDataManager.Instance.EnvironmentAnomaliesUnlocked   = true;
        }

        ApplyUnlocksFromSave();
    }

    /// <summary>Returns true if the anomaly identified by its script asset reference is locked.</summary>
    public bool IsAnomalyLocked(UnityEngine.Object anomalyType)
    {
        if (anomalyType == null)
            return false;

        foreach (List<AnomalyLockEntry> list in AllLists())
        {
            foreach (AnomalyLockEntry entry in list)
            {
                if (entry.anomalyType == anomalyType)
                    return entry.locked;
            }
        }

        return false;
    }

    /// <summary>Sets the locked state of an anomaly. Searches all category lists for a matching entry.</summary>
    public void SetAnomalyLocked(UnityEngine.Object anomalyType, bool locked)
    {
        foreach (List<AnomalyLockEntry> list in AllLists())
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].anomalyType == anomalyType)
                {
                    list[i] = new AnomalyLockEntry { anomalyType = anomalyType, locked = locked };
                    return;
                }
            }
        }

        Debug.LogWarning($"[AnomalyManager] No entry found for anomaly type '{anomalyType.name}'. Add it to the correct category list in the Inspector.");
    }

    private IEnumerable<List<AnomalyLockEntry>> AllLists()
    {
        yield return _mutationAnomalyLocks;
        yield return _behaviorAnomalyLocks;
        yield return _biologicalAnomalyLocks;
        yield return _documentationAnomalyLocks;
        yield return _environmentAnomalyLocks;
    }
}
