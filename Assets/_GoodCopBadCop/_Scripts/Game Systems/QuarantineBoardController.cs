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

        for (int i = 0; i < _polaroidSlots.Length; i++)
        {
            if (_polaroidSlots[i] == null) continue;

            if (i < quarantined.Count && quarantined[i].SuspectData != null)
            {
                int daysLeft = SuspectRunRecords.Instance.GetRemainingQuarantineDays(quarantined[i], currentDay);
                _polaroidSlots[i].Setup(quarantined[i].SuspectData, daysLeft);
            }
            else
            {
                _polaroidSlots[i].Hide();
            }
        }

        if (_titleText != null)
        {
            // The board's font renders every digit one higher than its value, so we compensate by
            // subtracting 1 from each number before writing the string.
            int displayCount = Mathf.Max(0, quarantined.Count - 1);
            int displayLimit = SuspectRunRecords.QuarantineSlotLimit - 1;
            _titleText.text = $"Quarantine {displayCount}/{displayLimit}";
        }
    }
}
