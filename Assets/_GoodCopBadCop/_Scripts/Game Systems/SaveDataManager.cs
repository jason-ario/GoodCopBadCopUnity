using System;
using System.IO;
using GoodCopBadCop.Population;
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

    /// <summary>
    /// Cash total as of the active slot's last Dusk checkpoint (see
    /// <see cref="SaveDuskCheckpoint"/>). Setting this persists immediately.
    /// </summary>
    public int CurrentCash
    {
        get => ActiveSlot?.TotalCashEarned ?? 0;
        set
        {
            if (ActiveSlot == null) return;
            ActiveSlot.TotalCashEarned = value;
            Save();
        }
    }

    /// <summary>
    /// Persists the Dusk checkpoint used by a death-retry that fast-forwards back into the
    /// post-shift phase: current coupon total and every live pickable's transform. Call once,
    /// the instant all suspects finish processing (see <see cref="ShiftManager.HandleAllSuspectsProcessed"/>),
    /// rather than setting <see cref="CurrentCash"/> and pickable state separately.
    /// </summary>
    public void SaveDuskCheckpoint(int cash, PickableObjectSaveData[] pickables)
    {
        if (ActiveSlot == null)
        {
            Debug.LogWarning("[SaveDataManager] SaveDuskCheckpoint called with no active slot.");
            return;
        }

        ActiveSlot.TotalCashEarned = cash;
        ActiveSlot.PickableObjects = pickables ?? new PickableObjectSaveData[0];
        Save();

        Debug.Log($"[SaveDataManager] Dusk checkpoint saved — cash: {cash}, pickables: {ActiveSlot.PickableObjects.Length}.");
    }

    // -------------------------------------------------------------------------
    // Daily Task Unlocks
    // -------------------------------------------------------------------------

    /// <summary>Returns the persisted daily task IDs that have been unlocked for the active slot.</summary>
    public string[] GetUnlockedDailyTaskIds()
    {
        return ActiveSlot?.UnlockedDailyTaskIds ?? new string[0];
    }

    /// <summary>Returns true if the daily task with the given ID has been unlocked for the active slot.</summary>
    public bool IsDailyTaskUnlocked(string taskId)
    {
        string[] unlocked = ActiveSlot?.UnlockedDailyTaskIds;
        if (unlocked == null) return false;
        return Array.IndexOf(unlocked, taskId) >= 0;
    }

    /// <summary>
    /// Marks the daily task ID as unlocked in the active slot and persists to disk.
    /// Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void UnlockDailyTask(string taskId)
    {
        if (ActiveSlot == null) return;
        if (IsDailyTaskUnlocked(taskId)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.UnlockedDailyTaskIds ?? new string[0]);
        list.Add(taskId);
        ActiveSlot.UnlockedDailyTaskIds = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] Daily task unlocked: '{taskId}'.");
    }

    // -------------------------------------------------------------------------
    // Anomaly Unlocks
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the anomaly with the given C# type name has been unlocked for the active slot.</summary>
    public bool IsAnomalyUnlocked(string typeName)
    {
        string[] unlocked = ActiveSlot?.UnlockedAnomalyTypeNames;
        if (unlocked == null) return false;
        return Array.IndexOf(unlocked, typeName) >= 0;
    }

    /// <summary>
    /// Marks the anomaly type name as unlocked in the active slot and persists to disk.
    /// Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void UnlockAnomaly(string typeName)
    {
        if (ActiveSlot == null) return;
        if (IsAnomalyUnlocked(typeName)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.UnlockedAnomalyTypeNames ?? new string[0]);
        list.Add(typeName);
        ActiveSlot.UnlockedAnomalyTypeNames = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] Anomaly unlocked: '{typeName}'.");
    }

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

    // -------------------------------------------------------------------------
    // World Object Unlocks (generic — used by WorldPurchaseActionInteractable
    // for one-off scene purchases such as the booth PC, Radio, TV, etc.)
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the world object with the given ID has been permanently unlocked for the active slot.</summary>
    public bool IsWorldObjectUnlocked(string objectId)
    {
        string[] unlocked = ActiveSlot?.UnlockedWorldObjectIds;
        if (unlocked == null) return false;
        return Array.IndexOf(unlocked, objectId) >= 0;
    }

    /// <summary>
    /// Marks the named world object as permanently unlocked in the active slot and persists to disk.
    /// Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void UnlockWorldObject(string objectId)
    {
        if (ActiveSlot == null) return;
        if (IsWorldObjectUnlocked(objectId)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.UnlockedWorldObjectIds ?? new string[0]);
        list.Add(objectId);
        ActiveSlot.UnlockedWorldObjectIds = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] World object unlocked and saved: '{objectId}'.");
    }

    // -------------------------------------------------------------------------
    // Suspect First-Encounter Tracking
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the named suspect's intro dialogue has already played for the active slot.</summary>
    public bool HasEncounteredSuspect(string suspectName)
    {
        if (string.IsNullOrEmpty(suspectName)) return false;
        string[] encountered = ActiveSlot?.EncounteredSuspectNames;
        if (encountered == null) return false;
        return Array.IndexOf(encountered, suspectName) >= 0;
    }

    /// <summary>
    /// Marks the named suspect as encountered (their intro dialogue has played) in the active
    /// slot and persists to disk. Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void MarkSuspectEncountered(string suspectName)
    {
        if (ActiveSlot == null || string.IsNullOrEmpty(suspectName)) return;
        if (HasEncounteredSuspect(suspectName)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.EncounteredSuspectNames ?? new string[0]);
        list.Add(suspectName);
        ActiveSlot.EncounteredSuspectNames = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] Suspect encountered: '{suspectName}'.");
    }

    /// <summary>Clears the encounter record for one specific suspect name in the active slot. Debug use only.</summary>
    public void ResetEncounteredSuspect(string suspectName)
    {
        if (ActiveSlot == null || string.IsNullOrEmpty(suspectName)) return;

        var list = new System.Collections.Generic.List<string>(ActiveSlot.EncounteredSuspectNames ?? new string[0]);
        if (list.Remove(suspectName))
        {
            ActiveSlot.EncounteredSuspectNames = list.ToArray();
            Save();
        }
    }

    /// <summary>Clears every suspect encounter record in the active slot. Debug use only.</summary>
    public void ResetAllEncounteredSuspects()
    {
        if (ActiveSlot == null) return;
        ActiveSlot.EncounteredSuspectNames = new string[0];
        Save();
    }

    // -------------------------------------------------------------------------
    // Lock State
    // -------------------------------------------------------------------------

    /// <summary>Returns true if the lock with the given ID has been unlocked in the active slot.</summary>
    public bool IsLockUnlocked(string lockId)
    {
        string[] unlocked = ActiveSlot?.UnlockedLockIds;
        if (unlocked == null) return false;
        return Array.IndexOf(unlocked, lockId) >= 0;
    }

    /// <summary>
    /// Records a lock as permanently unlocked in the active slot and persists to disk.
    /// Safe to call multiple times — duplicate entries are ignored.
    /// </summary>
    public void SaveUnlockedLock(string lockId)
    {
        if (ActiveSlot == null) return;
        if (IsLockUnlocked(lockId)) return;

        var list = new System.Collections.Generic.List<string>(
            ActiveSlot.UnlockedLockIds ?? new string[0]);
        list.Add(lockId);
        ActiveSlot.UnlockedLockIds = list.ToArray();
        Save();
        Debug.Log($"[SaveDataManager] Lock unlocked and saved: '{lockId}'.");
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

    // -------------------------------------------------------------------------
    // Suspect Records
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the current runtime suspect state (kill flags, quarantine cooldowns, infection scores)
    /// to the active save slot and flushes to disk.
    /// Call this whenever any suspect record changes: on kill, quarantine, and after each day advance.
    /// Server-only — only the host mutates suspect records.
    /// </summary>
    public void SaveSuspectRecords(System.Collections.Generic.List<SuspectRecord> records)
    {
        if (ActiveSlot == null) return;

        var entries = new SuspectSaveEntry[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            SuspectRecord r = records[i];
            entries[i] = new SuspectSaveEntry
            {
                SuspectName      = r.SuspectData != null ? r.SuspectData.name : string.Empty,
                IsKilled         = r.isKilled,
                HasEnteredCity   = r.hasEnteredCity,
                PopulationKillPending = r.populationKillPending,
                PopulationDeathRecorded = r.populationDeathRecorded,
                KilledOnDay      = r.killedOnDay,
                IsReplacement    = r.isReplacement,
                QuarantinedOnDay = r.quarantinedOnDay,
                InfectionScore   = r.infectionScore,
                IsLegacyMutant   = r.isLegacyMutant,
                DaysShown        = r.daysShown,
                LastDayShown     = r.lastDayShown,
            };
        }

        ActiveSlot.SuspectRecords = entries;
        Save();
        Debug.Log($"[SaveDataManager] Suspect records saved ({entries.Length} entries).");
    }

    /// <summary>
    /// Returns the persisted suspect entries for the active slot.
    /// Returns an empty array when no slot is active or no records have been saved yet.
    /// </summary>
    public SuspectSaveEntry[] GetSavedSuspectRecords()
    {
        return ActiveSlot?.SuspectRecords ?? new SuspectSaveEntry[0];
    }

    // -------------------------------------------------------------------------
    // Population
    // -------------------------------------------------------------------------

    public void SavePopulation(PopulationSaveData population)
    {
        if (ActiveSlot == null) return;

        ActiveSlot.Population = population ?? new PopulationSaveData();
        Save();
        Debug.Log("[SaveDataManager] Population saved.");
    }

    public PopulationSaveData GetSavedPopulation()
    {
        return ActiveSlot?.Population ?? new PopulationSaveData();
    }

    // -------------------------------------------------------------------------
    // Glass State
    // -------------------------------------------------------------------------

    /// <summary>True when the booth glass is saved as fully smashed in the active slot.</summary>
    public bool IsGlassSmashed => ActiveSlot?.IsGlassSmashed ?? false;

    /// <summary>
    /// Records the booth glass smashed/restored state in the active slot and persists to disk.
    /// Only the host writes to disk; clients update in-memory state only.
    /// </summary>
    public void SetGlassSmashed(bool smashed)
    {
        if (ActiveSlot == null) return;
        ActiveSlot.IsGlassSmashed = smashed;
        Save();
        Debug.Log($"[SaveDataManager] Glass smashed state saved: {smashed}.");
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

    /// <summary>
    /// Returns the index (0-based) of the most recently saved occupied slot,
    /// or -1 when no occupied slots exist. Used by the Continue button to resume
    /// the player's last session without requiring manual slot selection.
    /// </summary>
    public int GetMostRecentOccupiedSlotIndex()
    {
        int bestIndex = -1;
        DateTime bestTime = DateTime.MinValue;
        for (int i = 0; i < _saveData.Slots.Length; i++)
        {
            SaveSlot slot = _saveData.Slots[i];
            if (slot.IsOccupied && slot.LastSaved > bestTime)
            {
                bestTime = slot.LastSaved;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

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
        SaveScreenshotManager.DeleteScreenshot(index);
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

    /// <summary>
    /// Returns true when this peer is allowed to write save data to disk.
    /// In a networked session only the host persists save state; clients skip all disk writes.
    /// Outside of an active session (solo / pre-game) saving is always permitted.
    /// </summary>
    private bool CanSave()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return true;
        return nm.IsHost || nm.IsServer;
    }

    private void Save()
    {
        if (!CanSave())
        {
            Debug.Log("[SaveDataManager] Save skipped — client peer does not write to disk.");
            return;
        }

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

/// <summary>
/// Persistent state for a single suspect, serialized into the save slot.
/// Keyed by <see cref="SuspectData.name"/> (the ScriptableObject asset name),
/// which is the stable cross-session identifier for each character.
/// </summary>
[Serializable]
public class SuspectSaveEntry
{
    /// <summary>Matches <see cref="SuspectData.name"/> — the ScriptableObject asset name.</summary>
    public string SuspectName;

    /// <summary>True when this suspect was permanently eliminated and must never reappear.</summary>
    public bool IsKilled;

    /// <summary>True once this suspect was passed through the gate into the city.</summary>
    public bool HasEnteredCity;

    /// <summary>True when this suspect gets one pending night of background kills.</summary>
    public bool PopulationKillPending;

    /// <summary>True once this suspect has already reduced contactable population alive count.</summary>
    public bool PopulationDeathRecorded;

    /// <summary>
    /// Campaign day on which this suspect was killed (-1 = never killed).
    /// Used to determine when the replacement version activates (killedOnDay + replacementWindowDays).
    /// </summary>
    public int KilledOnDay = -1;

    /// <summary>
    /// True when the replacement version of this killed suspect has been activated.
    /// The replacement spawns as a doppelganger using the suspect's replacementConfig.
    /// </summary>
    public bool IsReplacement;

    /// <summary>
    /// Campaign day on which this suspect was most recently quarantined (-1 = never).
    /// Compared against the current day to enforce the one-shift cooldown.
    /// </summary>
    public int QuarantinedOnDay = -1;

    /// <summary>Accumulated infection score, advanced each day by <see cref="SuspectRunRecords.AdvanceDayInfection"/>.</summary>
    public int InfectionScore;

    /// <summary>
    /// True when this suspect escaped a full-mutant booth encounter alive (beaten and fled rather
    /// than killed) and is currently a candidate for <see cref="MutantSpawner"/>'s legacy-mutant pool.
    /// </summary>
    public bool IsLegacyMutant;

    /// <summary>
    /// Number of shifts this suspect has appeared in across the run. Drives "has this suspect been
    /// seen before" history checks (e.g. <see cref="PC.HasMetSuspect"/>) and DailySuspectManager's
    /// repeat-appearance logic.
    /// </summary>
    public int DaysShown;

    /// <summary>
    /// Campaign day this suspect was most recently shown on (-1/0 = never shown this run).
    /// Used to render the suspect's last newspaper/profile entry date.
    /// </summary>
    public int LastDayShown;
}

[Serializable]
public class SaveSlot
{
    public bool IsOccupied;
    public string SlotName;
    public bool HasSeenTutorial;
    public int CurrentDay;
    public int TotalCashEarned;

    /// <summary>
    /// C# type names of anomalies the player has explicitly unlocked through progression.
    /// Anomalies absent from this list and not in the default-unlocked set on
    /// <see cref="AnomalyUnlockManager"/> are shown as locked on exam page checklists.
    /// </summary>
    public string[] UnlockedAnomalyTypeNames = new string[0];

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

    /// <summary>
    /// IDs of padlocks that have been permanently unlocked.
    /// On load, any <see cref="LockController"/> whose lockId appears here is silently despawned
    /// and its target <see cref="ILockable"/> is immediately unlocked.
    /// </summary>
    public string[] UnlockedLockIds = new string[0];

    /// <summary>
    /// IDs of world objects (e.g. the booth PC, Radio, TV) that have been permanently purchased/unlocked
    /// via <see cref="WorldPurchaseActionInteractable"/>. On load, any interactable whose persistent
    /// unlock ID appears here immediately replays its purchase effect (without charging) and hides itself.
    /// </summary>
    public string[] UnlockedWorldObjectIds = new string[0];

    /// <summary>
    /// Stable task IDs (matching <see cref="IDailyTask.DailyTaskId"/>) that have been unlocked
    /// through gameplay progression and are eligible for selection by <see cref="DailyTaskScheduler"/>.
    /// </summary>
    public string[] UnlockedDailyTaskIds = new string[0];

    /// <summary>
    /// Unity asset names of suspects whose first-encounter intro dialogue
    /// (<see cref="SuspectData.introDialogue"/>) has already played for this save slot.
    /// Populated and consumed by <see cref="SuspectEncounterManager"/>.
    /// </summary>
    public string[] EncounteredSuspectNames = new string[0];

    /// <summary>
    /// Per-suspect persistent state (kill flags, quarantine cooldowns, infection scores).
    /// Populated and consumed by <see cref="SuspectRunRecords"/>.
    /// </summary>
    public SuspectSaveEntry[] SuspectRecords = new SuspectSaveEntry[0];

    /// <summary>Aggregate town population state for the active campaign run.</summary>
    public PopulationSaveData Population = new PopulationSaveData();

    /// <summary>
    /// True when the booth window glass has been fully smashed and not yet repaired.
    /// Persisted so the broken state survives across play sessions.
    /// </summary>
    public bool IsGlassSmashed;

    /// <summary>ISO-8601 string; use LastSavedTime for a parsed DateTime.</summary>
    public string LastSavedRaw;

    /// <summary>
    /// Snapshot of every pickable's position/rotation, captured at the Dusk checkpoint (see
    /// <see cref="ShiftManager.HandleAllSuspectsProcessed"/>). Restored on a death-retry that
    /// fast-forwards back into the post-shift phase (see <see cref="ShiftManager.RestartIntoPostShiftPhase"/>)
    /// so world clutter resets to where it was when Dusk began, instead of wherever it ended up
    /// during the failed attempt.
    /// </summary>
    public PickableObjectSaveData[] PickableObjects = new PickableObjectSaveData[0];

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
