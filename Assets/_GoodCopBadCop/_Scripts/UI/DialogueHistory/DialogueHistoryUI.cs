using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Dialogue History panel in the bottom-left of the HUD.
/// Subscribes to <see cref="DialogueHistoryManager.OnEntryAdded"/> and appends
/// rich-text rows into a ScrollRect. The panel starts collapsed and can be
/// toggled via <see cref="_toggleKey"/> or the header button.
/// </summary>
public class DialogueHistoryUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector references
    // -------------------------------------------------------------------------

    [Header("Panel")]
    [SerializeField] private RectTransform _panel;
    [SerializeField] private GameObject _contentRoot;      // ScrollRect + content — hidden when collapsed

    [Header("Scroll")]
    [SerializeField] private ScrollRect _scrollRect;
    [SerializeField] private RectTransform _contentContainer;

    [Header("Entry Prefab")]
    [SerializeField] private TextMeshProUGUI _entryPrefab;

    [Header("Toggle Button")]
    [SerializeField] private Button _toggleButton;
    [SerializeField] private TextMeshProUGUI _toggleButtonLabel;

    [Header("Key Hint")]
    [SerializeField] private TextMeshProUGUI _keyHintLabel;
    [SerializeField] private KeyCode _toggleKey = KeyCode.H;

    [Header("Labels")]
    [SerializeField] private string _collapseLabel = "▼";
    [SerializeField] private string _expandLabel   = "▲";

    [Header("Colours")]
    [SerializeField] private Color _playerColor    = DialogueHistoryManager.PlayerColor;
    [SerializeField] private Color _suspectColor   = DialogueHistoryManager.SuspectColor;
    [SerializeField] private Color _megaphoneColor = DialogueHistoryManager.MegaphoneColor;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private bool _isExpanded = false;
    private readonly List<TextMeshProUGUI> _spawnedEntries = new();

    private const float ScrollToBottomDelay = 0.05f;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _toggleButton.onClick.AddListener(OnToggleClicked);
    }

    private void OnEnable()
    {
        DialogueHistoryManager.OnEntryAdded += HandleEntryAdded;

        // Rebuild from existing history in case the UI was spawned after entries were logged
        RebuildFromHistory();
    }

    private void OnDisable()
    {
        DialogueHistoryManager.OnEntryAdded -= HandleEntryAdded;
    }

    private void Start()
    {
        UpdateKeyHintLabel();
        SetExpanded(_isExpanded);
    }

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey))
            SetExpanded(!_isExpanded);
    }

    // -------------------------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------------------------

    private void HandleEntryAdded(DialogueHistoryManager.DialogueEntry entry)
    {
        AppendEntry(entry);
    }

    private void OnToggleClicked()
    {
        SetExpanded(!_isExpanded);
    }

    // -------------------------------------------------------------------------
    // Panel State
    // -------------------------------------------------------------------------

    private void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        _contentRoot.SetActive(expanded);
        _toggleButtonLabel.text = expanded ? _collapseLabel : _expandLabel;

        if (expanded)
            ScrollToBottom();
    }

    // -------------------------------------------------------------------------
    // Key Hint
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the current toggle key name into the hint label, e.g. "[H]".
    /// Called once on Start so the label always reflects the configured key.
    /// </summary>
    private void UpdateKeyHintLabel()
    {
        if (_keyHintLabel == null) return;
        _keyHintLabel.text = $"[{_toggleKey}]";
    }

    // -------------------------------------------------------------------------
    // Entry Display
    // -------------------------------------------------------------------------

    /// <summary>Clears and re-populates the list from the static history.</summary>
    private void RebuildFromHistory()
    {
        foreach (var entry in _spawnedEntries)
        {
            if (entry != null)
                Destroy(entry.gameObject);
        }
        _spawnedEntries.Clear();

        foreach (var entry in DialogueHistoryManager.History)
            AppendEntry(entry);
    }

    private void AppendEntry(DialogueHistoryManager.DialogueEntry entry)
    {
        var label = Instantiate(_entryPrefab, _contentContainer);
        label.text = BuildRichText(entry);
        label.gameObject.SetActive(true);
        _spawnedEntries.Add(label);

        if (_isExpanded)
            ScrollToBottomDelayed();
    }

    private string BuildRichText(DialogueHistoryManager.DialogueEntry entry)
    {
        Color nameColor = entry.SpeakerType switch
        {
            DialogueHistoryManager.SpeakerType.Player    => _playerColor,
            DialogueHistoryManager.SpeakerType.Suspect   => _suspectColor,
            DialogueHistoryManager.SpeakerType.Megaphone => _megaphoneColor,
            _                                            => Color.white
        };

        string hex = ColorUtility.ToHtmlStringRGB(nameColor);
        string displayName = string.IsNullOrEmpty(entry.SpeakerName) ? entry.SpeakerType.ToString() : entry.SpeakerName;

        // Bold colour-coded speaker name, then plain white body text
        return $"<color=#{hex}><b>{displayName}:</b></color> <color=#FFFFFF>{entry.Text}</color>";
    }

    // -------------------------------------------------------------------------
    // Scroll Helpers
    // -------------------------------------------------------------------------

    private void ScrollToBottom()
    {
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 0f;
    }

    private void ScrollToBottomDelayed()
    {
        // Wait one frame for the layout to rebuild before snapping
        StartCoroutine(ScrollAfterDelay());
    }

    private System.Collections.IEnumerator ScrollAfterDelay()
    {
        yield return new WaitForSeconds(ScrollToBottomDelay);
        ScrollToBottom();
    }
}
