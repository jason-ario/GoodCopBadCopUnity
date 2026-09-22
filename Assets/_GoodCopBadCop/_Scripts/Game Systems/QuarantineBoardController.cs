using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Drives the world-space Quarantine Board: keeps the five polaroid slots in sync
/// with whoever is currently in quarantine, and updates the title count text.
///
/// Refreshes on:
///   - Start (covers initial load / save restore)
///   - CampaignManager.OnDayChanged  (day advance reduces remaining time; removes expired entries)
///   - SuspectController.OnSuspectQuarantined  (new entry right after the verdict is committed)
/// </summary>
public class QuarantineBoardController : MonoBehaviour
{
    [SerializeField] private QuarantinePolaroid[] _polaroidSlots;
    [SerializeField] private TextMeshPro           _titleText;

    // Sticky slot assignments: once a suspect is placed in a polaroid slot, they keep that
    // slot for as long as they remain quarantined. Without this, GetActiveQuarantineRecords()
    // is re-filtered from the master suspect list on every refresh, so adding or removing an
    // unrelated quarantined suspect can shift everyone else's position in that filtered list —
    // which reassigns slot indices and makes it look like a still-quarantined suspect's photo
    // was swapped out / removed early, when they actually just moved to a different slot (or
    // got pushed past _polaroidSlots.Length and stopped rendering at all).
    private readonly Dictionary<SuspectData, int> _slotAssignments = new Dictionary<SuspectData, int>();

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Auto-discover polaroid slots when not wired manually in the Inspector.
        if (_polaroidSlots == null || _polaroidSlots.Length == 0)
            _polaroidSlots = GetComponentsInChildren<QuarantinePolaroid>(true);

        // Auto-discover title text when not wired manually.
        if (_titleText == null)
        {
            Transform t = transform.Find("Text (TMP)");
            if (t != null) _titleText = t.GetComponent<TextMeshPro>();
        }
    }

    private void OnEnable()
    {
        CampaignManager.OnDayChanged              += OnDayChanged;
        SuspectController.OnSuspectQuarantined    += RefreshBoard;
    }

    private void OnDisable()
    {
        CampaignManager.OnDayChanged              -= OnDayChanged;
        SuspectController.OnSuspectQuarantined    -= RefreshBoard;
    }

    private void Start()
    {
        RefreshBoard();
    }

    // -------------------------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------------------------

    private void OnDayChanged(int _) => RefreshBoard();

    // -------------------------------------------------------------------------
    // Board Update
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queries the live quarantine records and updates every slot.
    /// Empty slots are hidden; occupied slots show name, photo, and remaining days.
    /// The title text reflects the current occupied / max count.
    /// </summary>
    public void RefreshBoard()
    {
        if (SuspectRunRecords.Instance == null || CampaignManager.Instance == null)
            return;

        int currentDay = CampaignManager.Instance.CurrentDay;
        List<SuspectRecord> quarantined = SuspectRunRecords.Instance.GetActiveQuarantineRecords(currentDay);

        UpdateSlotAssignments(quarantined);

        for (int i = 0; i < _polaroidSlots.Length; i++)
        {
            if (_polaroidSlots[i] != null) _polaroidSlots[i].Hide();
        }

        foreach (SuspectRecord record in quarantined)
        {
            if (record.SuspectData == null) continue;
            if (!_slotAssignments.TryGetValue(record.SuspectData, out int slot)) continue;
            if (slot < 0 || slot >= _polaroidSlots.Length || _polaroidSlots[slot] == null) continue;

            int daysLeft = SuspectRunRecords.Instance.GetRemainingQuarantineDays(record, currentDay);
            _polaroidSlots[slot].Setup(record.SuspectData, daysLeft);
        }

        if (_titleText != null)
        {
            _titleText.text = $"Quarantine {quarantined.Count}/{SuspectRunRecords.QuarantineSlotLimit}";
        }
    }

    /// <summary>
    /// Keeps <see cref="_slotAssignments"/> in sync with who is actually quarantined right now:
    /// drops anyone no longer in the active list (so their slot can be reused), then hands each
    /// newly-quarantined suspect the lowest free slot index. Everyone already assigned keeps
    /// their existing slot, so a polaroid never changes identity while its suspect is still
    /// serving their quarantine.
    /// </summary>
    private void UpdateSlotAssignments(List<SuspectRecord> quarantined)
    {
        var active = new HashSet<SuspectData>();
        foreach (SuspectRecord record in quarantined)
        {
            if (record.SuspectData != null) active.Add(record.SuspectData);
        }

        List<SuspectData> stale = null;
        foreach (SuspectData data in _slotAssignments.Keys)
        {
            if (!active.Contains(data))
            {
                stale ??= new List<SuspectData>();
                stale.Add(data);
            }
        }
        if (stale != null)
        {
            foreach (SuspectData data in stale) _slotAssignments.Remove(data);
        }

        foreach (SuspectRecord record in quarantined)
        {
            if (record.SuspectData == null || _slotAssignments.ContainsKey(record.SuspectData))
                continue;

            for (int slot = 0; slot < _polaroidSlots.Length; slot++)
            {
                if (!_slotAssignments.ContainsValue(slot))
                {
                    _slotAssignments[record.SuspectData] = slot;
                    break;
                }
            }
        }
    }
}
