using System;
using System.IO;
using UnityEngine;

public class SaveDataManager : MonoBehaviour
{
    public static SaveDataManager Instance { get; private set; }

    private const int SlotCount = 3;
    private const string SaveFileName = "savedata.json";

    private SaveData _saveData;
    private string _savePath;

    // The slot the player chose on the campaign screen. -1 = none selected.
    public int ActiveSlotIndex { get; private set; } = -1;

    /// <summary>The slot data for the currently active slot, or null if no slot is selected.</summary>
    public SaveSlot ActiveSlot => ActiveSlotIndex >= 0 ? _saveData.Slots[ActiveSlotIndex] : null;

    // ---------------------------------------------------------------------------
    // Legacy compat — kept so existing callers don't break while we migrate.
    // ---------------------------------------------------------------------------

    /// <summary>Returns true if any slot has meaningful progress.</summary>
    public bool HasSaveFile => Array.Exists(_saveData.Slots, s => s.IsOccupied);

    /// <summary>True once the active slot's intro tutorial has been seen.</summary>
    public bool HasSeenIntroTutorial
    {
        get => ActiveSlot?.HasSeenTutorial ?? false;
        set
        {
            if (ActiveSlot == null) return;
            ActiveSlot.HasSeenTutorial = value;
            Save();
        }
    }

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
        Load();
    }

    // ---------------------------------------------------------------------------
    // Slot Access
    // ---------------------------------------------------------------------------

    /// <summary>Returns the save slot at the given index (0-based).</summary>
    public SaveSlot GetSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            Debug.LogWarning($"[SaveDataManager] GetSlot: index {index} out of range.");
            return null;
        }

        return _saveData.Slots[index];
    }

    /// <summary>
    /// Selects a slot as the active campaign slot. If it is empty, initialises a new save in it.
    /// Call this when the player clicks a slot on the campaign screen.
    /// </summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            Debug.LogWarning($"[SaveDataManager] SelectSlot: index {index} out of range.");
            return;
        }

        ActiveSlotIndex = index;

        SaveSlot slot = _saveData.Slots[index];
        if (!slot.IsOccupied)
        {
            slot.IsOccupied = true;
            slot.SlotName = $"Save {index + 1}";
            slot.LastSaved = DateTime.UtcNow;
            Save();
            Debug.Log($"[SaveDataManager] New save created in slot {index}.");
        }
        else
        {
            Debug.Log($"[SaveDataManager] Slot {index} selected — resuming '{slot.SlotName}'.");
        }
    }

    /// <summary>Persists any in-memory changes to the active slot.</summary>
    public void SaveActiveSlot()
    {
        if (ActiveSlot == null)
        {
            Debug.LogWarning("[SaveDataManager] SaveActiveSlot called with no active slot.");
            return;
        }

        ActiveSlot.LastSaved = DateTime.UtcNow;
        Save();
    }

    /// <summary>Deletes the slot at the given index and persists the change.</summary>
    public void DeleteSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            Debug.LogWarning($"[SaveDataManager] DeleteSlot: index {index} out of range.");
            return;
        }

        _saveData.Slots[index] = new SaveSlot();
        Save();
        Debug.Log($"[SaveDataManager] Slot {index} deleted.");

        if (ActiveSlotIndex == index)
            ActiveSlotIndex = -1;
    }

    // ---------------------------------------------------------------------------
    // Legacy full-save delete (kept for ContextMenu / dev tooling)
    // ---------------------------------------------------------------------------

    [ContextMenu("Delete All Saves")]
    public void DeleteSave()
    {
        if (File.Exists(_savePath))
        {
            File.Delete(_savePath);
            Debug.Log("[SaveDataManager] Save file deleted.");
        }

        _saveData = new SaveData();
        ActiveSlotIndex = -1;
    }

    // ---------------------------------------------------------------------------
    // IO
    // ---------------------------------------------------------------------------

    private void Save()
    {
        string json = JsonUtility.ToJson(_saveData, prettyPrint: true);
        File.WriteAllText(_savePath, json);
        Debug.Log($"[SaveDataManager] Saved to: {_savePath}");
    }

    private void Load()
    {
        if (File.Exists(_savePath))
        {
            string json = File.ReadAllText(_savePath);
            _saveData = JsonUtility.FromJson<SaveData>(json);

            // Guard against old single-slot save files that have no Slots array.
            if (_saveData.Slots == null || _saveData.Slots.Length != SlotCount)
                _saveData.Slots = new SaveSlot[SlotCount];

            for (int i = 0; i < SlotCount; i++)
                _saveData.Slots[i] ??= new SaveSlot();

            Debug.Log("[SaveDataManager] Save file loaded.");
        }
        else
        {
            _saveData = new SaveData();
            Debug.Log("[SaveDataManager] No save file found — created new SaveData.");
        }
    }
}

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

[Serializable]
public class SaveData
{
    private const int SlotCount = 3;

    public SaveSlot[] Slots = new SaveSlot[SlotCount]
    {
        new SaveSlot(),
        new SaveSlot(),
        new SaveSlot()
    };
}

[Serializable]
public class SaveSlot
{
    public bool IsOccupied;
    public string SlotName;
    public bool HasSeenTutorial;
    public int CurrentDay;
    public int TotalCashEarned;

    /// <summary>ISO-8601 string; use LastSavedTime for a parsed DateTime.</summary>
    public string LastSavedRaw;

    [NonSerialized]
    private DateTime _lastSaved;

    public DateTime LastSaved
    {
        get
        {
            if (_lastSaved == default && !string.IsNullOrEmpty(LastSavedRaw))
                DateTime.TryParse(LastSavedRaw, out _lastSaved);
            return _lastSaved;
        }
        set
        {
            _lastSaved = value;
            LastSavedRaw = value.ToString("o");
        }
    }
}
