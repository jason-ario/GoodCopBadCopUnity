using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a single campaign save slot on the campaign selection screen.
/// Populates itself from <see cref="SaveDataManager"/> and notifies
/// <see cref="CampaignScreenController"/> when the player interacts with it.
/// </summary>
public class CampaignSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Hover Scale")]
    [SerializeField] private float hoverScale = 1.05f;
    [Tooltip("Higher = snappier. Exponential smoothing rate per second.")]
    [SerializeField] private float scaleSpeed = 12f;

    private Vector3 _baseScale = Vector3.one;
    private float _targetScaleMultiplier = 1f;
    private float _currentScaleMultiplier = 1f;

    [Header("Layout")]
    [SerializeField] private GameObject emptySlotContainer;
    [SerializeField] private GameObject occupiedSlotContainer;

    [Header("Occupied Slot UI")]
    [SerializeField] private TextMeshProUGUI slotNameText;
    [SerializeField] private TextMeshProUGUI dayNumberText;
    [SerializeField] private TextMeshProUGUI cashAmountText;
    [SerializeField] private TextMeshProUGUI lastSavedText;
    [SerializeField] private TextMeshProUGUI hoursPlayedText;
    [SerializeField] private Button deleteButton;

    [Header("Thumbnail")]
    [Tooltip("Optional RawImage that displays the save-slot screenshot. Leave unassigned to skip thumbnail display.")]
    [SerializeField] private RawImage thumbnailImage;

    [Header("Slot Index")]
    [SerializeField] private int slotIndex;

    [Header("Audio")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;

    private CampaignScreenController _screen;
    private Texture2D _thumbnailTexture;

    // ---------------------------------------------------------------------------
    // Initialisation
    // ---------------------------------------------------------------------------

    /// <summary>Called by <see cref="CampaignScreenController"/> after instantiation.</summary>
    public void Initialise(CampaignScreenController screen, int index)
    {
        _screen = screen;
        slotIndex = index;
        Refresh();
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>Re-reads save data and refreshes displayed values.</summary>
    public void Refresh()
    {
        SaveSlot slot = SaveDataManager.Instance.GetSlot(slotIndex);
        bool occupied = slot != null && slot.IsOccupied;

        emptySlotContainer.SetActive(!occupied);
        occupiedSlotContainer.SetActive(occupied);

        if (!occupied)
        {
            ClearThumbnail();
            return;
        }

        slotNameText.text = slot.SlotName;
        // CurrentDay is 0 for a fresh, never-started slot and otherwise already the 1-indexed
        // day the player is on (matches CampaignManager.StartCampaign's own Mathf.Max(1, ...)
        // clamp) — do not add 1 here, that double-counts and shows one day ahead of reality.
        int displayDay = Mathf.Max(1, slot.CurrentDay);
        dayNumberText.text = slot.IsDuskCheckpointFor(slot.CurrentDay) ? $"Day {displayDay} - Dusk" : $"Day {displayDay}";
        cashAmountText.text = $"{slot.TotalCashEarned:N0}";
        lastSavedText.text = slot.LastSaved != default
            ? slot.LastSaved.ToLocalTime().ToString("MMM d, yyyy")
            : string.Empty;

        if (hoursPlayedText != null)
            hoursPlayedText.text = FormatPlaytime(slot.PlaytimeSeconds);

        LoadThumbnail(slotIndex);
    }

    private static string FormatPlaytime(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalHours}h {t.Minutes:D2}m";
    }

    private void OnDestroy()
    {
        ClearThumbnail();
    }

    private void LoadThumbnail(int index)
    {
        if (thumbnailImage == null)
        {
            Debug.LogWarning($"[CampaignSlot] Slot {index} — thumbnailImage is not assigned in the Inspector. Skipping thumbnail load.");
            return;
        }

        ClearThumbnail();

        string path = SaveScreenshotManager.GetScreenshotPath(index);
        Debug.Log($"[CampaignSlot] Slot {index} — looking for thumbnail at: {path}");

        if (!File.Exists(path))
        {
            Debug.Log($"[CampaignSlot] Slot {index} — no screenshot file found at that path.");
            return;
        }

        byte[] data = File.ReadAllBytes(path);
        Debug.Log($"[CampaignSlot] Slot {index} — loaded {data.Length} bytes, decoding PNG.");

        _thumbnailTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        _thumbnailTexture.filterMode = FilterMode.Point;
        if (_thumbnailTexture.LoadImage(data))
        {
            thumbnailImage.texture = _thumbnailTexture;
            thumbnailImage.gameObject.SetActive(true);
            Debug.Log($"[CampaignSlot] Slot {index} — thumbnail displayed ({_thumbnailTexture.width}x{_thumbnailTexture.height}).");
        }
        else
        {
            Debug.LogWarning($"[CampaignSlot] Slot {index} — LoadImage failed. PNG data may be corrupt.");
            ClearThumbnail();
        }
    }

    private void ClearThumbnail()
    {
        if (thumbnailImage != null)
            thumbnailImage.texture = null;

        if (_thumbnailTexture != null)
        {
            Destroy(_thumbnailTexture);
            _thumbnailTexture = null;
        }
    }

    // ---------------------------------------------------------------------------
    // Button Handlers (wire up in the Inspector)
    // ---------------------------------------------------------------------------

    /// <summary>Called by the slot's main button — selects this slot and proceeds.</summary>
    public void OnSlotSelected()
    {
        SFXController.Instance?.Play(clickClip);
        SaveDataManager.Instance.SelectSlot(slotIndex);
        _screen?.OnSlotChosen(slotIndex);
    }

    /// <summary>Called by the delete button — deletes save data after confirmation.</summary>
    public void OnDeletePressed()
    {
        SFXController.Instance?.Play(clickClip);
        _screen?.RequestDeleteSlot(slotIndex);
    }

    /// <summary>Plays the hover sound when the pointer enters the slot.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        SFXController.Instance?.Play(hoverClip);
        _targetScaleMultiplier = hoverScale;
    }

    /// <summary>Scales the slot back to its resting size when the pointer leaves.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        _targetScaleMultiplier = 1f;
    }

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void OnDisable()
    {
        // Snap back so a slot hidden mid-hover doesn't reappear enlarged.
        _targetScaleMultiplier = _currentScaleMultiplier = 1f;
        transform.localScale = _baseScale;
    }

    private void Update()
    {
        if (Mathf.Approximately(_currentScaleMultiplier, _targetScaleMultiplier)) return;

        // Unscaled time so the animation works while the game is paused (timeScale = 0).
        float t = 1f - Mathf.Exp(-scaleSpeed * Time.unscaledDeltaTime);
        _currentScaleMultiplier = Mathf.Lerp(_currentScaleMultiplier, _targetScaleMultiplier, t);
        if (Mathf.Abs(_currentScaleMultiplier - _targetScaleMultiplier) < 0.0005f)
            _currentScaleMultiplier = _targetScaleMultiplier;

        transform.localScale = _baseScale * _currentScaleMultiplier;
    }
}
