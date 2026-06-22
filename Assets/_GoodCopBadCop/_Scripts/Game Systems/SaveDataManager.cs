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

    // -------------------------------------------------------------------------
    // Anomaly Category Unlocks — removed; all categories are unlocked by default.
    // -------------------------------------------------------------------------

    /// <summary>
    /// True once the player has completed the full Day 1 tutorial sequence (including tool locker refill).
    /// When true, DayActivated() on Day 1 skips all tutorial gating and runs a free-play shift.
    /// </summary>
    public bool Day1TutorialComplete
    {
        get => ActiveSlot?.Day1TutorialComplete ?? false;
        set { if (ActiveSlot == null) return; ActiveSlot.Day1TutorialComplete = value; Save(); }
    }

    // -------------------------------------------------------------------------
    // Shop Item Unlocks
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the named shop item has been explicitly unlocked for the active slot.</summary>
    public bool IsShopItemUnlocked(string itemName)
    {
        string[] unlocked = ActiveSlot?.UnlockedShopItems;
        if (unlocked == null) return false;
        return Array.IndexOf(unlocked, itemName) >= 0;
    }

    /// <summary>
    /// Marks the named shop item as unlocked in the active slot and persists to disk.
    /// Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void UnlockShopItem(string itemName)
    {
        if (ActiveSlot == null) return;
        if (IsShopItemUnlocked(itemName)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.UnlockedShopItems ?? new string[0]);
        list.Add(itemName);
        ActiveSlot.UnlockedShopItems = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] Shop item unlocked: '{itemName}'.");
    }

    /// <summary>The current day number for the active slot. Persists to disk on set.</summary>
    public int CurrentDay
    {
        get => ActiveSlot?.CurrentDay ?? 1;
        set
        {
            if (ActiveSlot == null) return;
            ActiveSlot.CurrentDay = value;
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
    /// <summary>
    /// Sets the active slot index in memory only. Does not write to disk.
    /// Call <see cref="InitialiseActiveSlot"/> once the game actually starts to commit new slots.
    /// </summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            Debug.LogWarning($"[SaveDataManager] SelectSlot: index {index} out of range.");
            return;
        }

        ActiveSlotIndex = index;
        Debug.Log($"[SaveDataManager] Slot {index} selected (not yet committed to disk).");
    }

    /// <summary>
    /// Marks the active slot as occupied and saves to disk.
    /// Only call this once the player has confirmed they want to start/resume — not on slot selection.
    /// </summary>
    public void InitialiseActiveSlot()
    {
        if (ActiveSlot == null)
        {
            Debug.LogWarning("[SaveDataManager] InitialiseActiveSlot called with no active slot.");
            return;
        }

        if (!ActiveSlot.IsOccupied)
        {
            ActiveSlot.IsOccupied = true;
            ActiveSlot.SlotName = $"Save {ActiveSlotIndex + 1}";
            ActiveSlot.LastSaved = DateTime.UtcNow;
            Debug.Log($"[SaveDataManager] New save created in slot {ActiveSlotIndex}.");
        }
        else
        {
            Debug.Log($"[SaveDataManager] Resuming slot {ActiveSlotIndex} ('{ActiveSlot.SlotName}').");
        }

        Save();
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

    // Anomaly category unlock flags removed — all categories are unlocked by default.

    /// <summary>
    /// Set to true once the player completes the Day 1 tutorial sequence (tool locker refill).
    /// Causes Day 1 to skip all tutorial gating on subsequent runs.
    /// </summary>
    public bool Day1TutorialComplete;

    /// <summary>
    /// Names of shop items unlocked through gameplay progression.
    /// Items absent from this list (and with _unlockedByDefault = false) appear as '???' in the shop.
    /// </summary>
    public string[] UnlockedShopItems = new string[0];

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
