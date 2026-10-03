using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Square popup over the Shift Report that shows one subject in detail: ID photo, name, ID number,
/// verdict, summary values, and an itemized list of which anomalies the player caught, which they
/// missed, and which checklist categories they flagged with nothing present.
///
/// Opened by <see cref="EndOfShiftReportUI"/> when a row is clicked after the reveal. Purely local
/// and informational. It can always be closed with the X button, a click on the backdrop, or
/// Escape / gamepad B, so it never traps the player on the report.
/// </summary>
public class ShiftReportSubjectDetailPopup : MonoBehaviour
{
    [Header("Structure")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("The square window. Scaled during the open pop.")]
    [SerializeField] private RectTransform window;
    [SerializeField] private Button closeButton;
    [Tooltip("Optional full-screen backdrop button. Clicking outside the window closes the popup.")]
    [SerializeField] private Button backdropButton;

    [Header("Subject")]
    [SerializeField] private RawImage photo;
    [SerializeField] private GameObject photoPlaceholder;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI idText;
    [SerializeField] private TextMeshProUGUI verdictText;
    [SerializeField] private TextMeshProUGUI summaryText;

    [Header("Details")]
    [SerializeField] private ScrollRect detailScroll;
    [Tooltip("Inactive template for section headers (CAUGHT, MISSED, ...).")]
    [SerializeField] private TextMeshProUGUI sectionHeaderTemplate;
    [Tooltip("Inactive template for one detail line.")]
    [SerializeField] private ShiftReportDetailLine lineTemplate;

    [Header("Colors")]
    [SerializeField] private Color caughtColor = new Color(0.22f, 0.42f, 0.2f, 1f);
    [SerializeField] private Color missedColor = new Color(0.62f, 0.13f, 0.1f, 1f);
    [SerializeField] private Color falseFlagColor = new Color(0.66f, 0.45f, 0.05f, 1f);
    [SerializeField] private Color neutralColor = new Color(0.36f, 0.32f, 0.27f, 1f);
    [SerializeField] private Color caughtTint = new Color(0.22f, 0.42f, 0.2f, 0.08f);
    [SerializeField] private Color missedTint = new Color(0.62f, 0.13f, 0.1f, 0.08f);
    [SerializeField] private Color falseFlagTint = new Color(0.9f, 0.72f, 0.1f, 0.12f);

    [Header("Verdict Colors")]
    [SerializeField] private Color passedColor = new Color(0.22f, 0.42f, 0.2f, 1f);
    [SerializeField] private Color quarantinedColor = new Color(0.66f, 0.45f, 0.05f, 1f);
    [SerializeField] private Color killedColor = new Color(0.62f, 0.13f, 0.1f, 1f);
    [SerializeField] private Color fledColor = new Color(0.36f, 0.32f, 0.27f, 1f);

    [Header("Animation")]
    [SerializeField] private float openDuration = 0.18f;
    [SerializeField] private float openStartScale = 0.9f;

    [Header("Audio")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.6f;

    private readonly List<TextMeshProUGUI> _headers = new List<TextMeshProUGUI>();
    private readonly List<ShiftReportDetailLine> _lines = new List<ShiftReportDetailLine>();
    private int _headersUsed;
    private int _linesUsed;
    private Coroutine _openRoutine;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (sectionHeaderTemplate != null)
            sectionHeaderTemplate.gameObject.SetActive(false);
        if (lineTemplate != null)
            lineTemplate.gameObject.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }
        if (backdropButton != null)
        {
            backdropButton.onClick.RemoveListener(Close);
            backdropButton.onClick.AddListener(Close);
        }

        CloseImmediate();
    }

    private void OnDisable()
    {
        // Closing via deactivation (e.g. the report being force-dismissed) must not leave stale state.
        IsOpen = false;
        _openRoutine = null;
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
            (gamepad != null && gamepad.buttonEast.wasPressedThisFrame))
        {
            Close();
        }
    }

    public void Open(int index, ShiftSubjectResult subject)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        FillHeader(index, subject);
        FillDetails(subject);

        IsOpen = true;
        SetVisible(true);

        if (detailScroll != null)
        {
            if (detailScroll.content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(detailScroll.content);
            detailScroll.StopMovement();
            detailScroll.verticalNormalizedPosition = 1f;
        }

        PlaySound(openSound);

        if (_openRoutine != null)
            StopCoroutine(_openRoutine);
        _openRoutine = StartCoroutine(OpenRoutine());
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        PlaySound(closeSound);
        CloseImmediate();
    }

    /// <summary>Hides the popup with no sound or animation. Safe to call at any time.</summary>
    public void CloseImmediate()
    {
        IsOpen = false;

        if (_openRoutine != null)
        {
            StopCoroutine(_openRoutine);
            _openRoutine = null;
        }

        SetVisible(false);
        if (window != null)
            window.localScale = Vector3.one;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
        else
        {
            gameObject.SetActive(visible);
        }
    }

    private IEnumerator OpenRoutine()
    {
        if (window == null || openDuration <= 0f)
        {
            _openRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / openDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float scale = Mathf.Lerp(openStartScale, 1f, eased);
            window.localScale = new Vector3(scale, scale, 1f);
            if (canvasGroup != null)
                canvasGroup.alpha = eased;
            yield return null;
        }

        window.localScale = Vector3.one;
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
        _openRoutine = null;
    }

    // ----- Header -----

    private void FillHeader(int index, ShiftSubjectResult subject)
    {
        if (nameText != null)
        {
            string name = string.IsNullOrWhiteSpace(subject.SubjectName) ? "Unknown Subject" : subject.SubjectName;
            nameText.text = $"{index + 1:00}. {name}";
        }

        if (idText != null)
            idText.text = ShiftReportSubjectRow.FormatId(subject.IDNumber);

        if (verdictText != null)
        {
            verdictText.text = $"VERDICT: {VerdictLabel(subject.Verdict)}";
            verdictText.color = VerdictColor(subject.Verdict);
        }

        if (summaryText != null)
            summaryText.text = BuildSummary(subject);

        Texture2D texture = subject.ResolveIDPhoto();
        bool hasPhoto = texture != null;
        if (photo != null)
        {
            photo.texture = texture;
            photo.enabled = hasPhoto;
            if (hasPhoto)
                photo.uvRect = ShiftReportSubjectRow.CoverUv(texture, photo.rectTransform.rect.size);
        }
        if (photoPlaceholder != null)
            photoPlaceholder.SetActive(!hasPhoto);
    }

    private static string BuildSummary(ShiftSubjectResult subject)
    {
        var parts = new List<string>();

        if (subject.AnomaliesCaught == ShiftSubjectResult.NotAssessed)
            parts.Add("Anomalies: Not Assessed");
        else
            parts.Add($"Anomalies: {subject.AnomaliesCaught} / {subject.AnomaliesTotal}");

        parts.Add($"Coupons: {subject.CouponsEarned}");

        if (subject.HasIntegrity)
            parts.Add($"Integrity: {Mathf.RoundToInt(subject.IntegrityAtProcessing * 100f)}%");

        return string.Join("     ", parts);
    }

    private static string VerdictLabel(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Quarantined: return "QUARANTINED";
            case ShiftSubjectVerdict.Killed: return "KILLED";
            case ShiftSubjectVerdict.Fled: return "FLED";
            default: return "PASSED";
        }
    }

    private Color VerdictColor(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Quarantined: return quarantinedColor;
            case ShiftSubjectVerdict.Killed: return killedColor;
            case ShiftSubjectVerdict.Fled: return fledColor;
            default: return passedColor;
        }
    }

    // ----- Details -----

    private void FillDetails(ShiftSubjectResult subject)
    {
        BeginList();

        Color clear = new Color(0f, 0f, 0f, 0f);

        if (!subject.HasBreakdown)
        {
            AddHeader("DETAILS");
            AddLine(null, neutralColor, "No detailed record was kept for this subject.", null, clear);
            EndList();
            return;
        }

        ShiftAnomalyResult[] anomalies = subject.Anomalies ?? System.Array.Empty<ShiftAnomalyResult>();
        bool notAssessed = subject.AnomaliesCaught == ShiftSubjectResult.NotAssessed;

        if (notAssessed)
        {
            string reason = subject.Verdict == ShiftSubjectVerdict.Fled
                ? "Subject fled before their paperwork was assessed."
                : "Subject was killed before their paperwork was assessed.";
            AddHeader("NOT ASSESSED");
            AddLine(null, neutralColor, reason, null, clear);

            AddHeader($"ANOMALIES ON RECORD ({anomalies.Length})");
            if (anomalies.Length == 0)
                AddLine(null, neutralColor, "No anomalies on record.", null, clear);
            foreach (ShiftAnomalyResult anomaly in anomalies)
                AddLine("UNCHECKED", neutralColor, anomaly.Name, ShiftAnomalyCategories.Label(anomaly.Category), clear);

            EndList();
            return;
        }

        var caught = new List<ShiftAnomalyResult>();
        var missed = new List<ShiftAnomalyResult>();
        ShiftAnomalyCategory present = ShiftAnomalyCategory.None;
        foreach (ShiftAnomalyResult anomaly in anomalies)
        {
            present |= anomaly.Category;
            (anomaly.Caught ? caught : missed).Add(anomaly);
        }

        var falseFlags = new List<ShiftAnomalyCategory>();
        foreach (ShiftAnomalyCategory category in ShiftAnomalyCategories.All)
        {
            if ((subject.FlaggedCategories & category) != 0 && (present & category) == 0)
                falseFlags.Add(category);
        }

        if (anomalies.Length == 0)
        {
            AddHeader("ANOMALIES");
            AddLine("CLEAN", caughtColor, "No anomalies were present.", null, clear);
        }

        if (caught.Count > 0)
        {
            AddHeader($"CAUGHT ({caught.Count})");
            foreach (ShiftAnomalyResult anomaly in caught)
                AddLine("CAUGHT", caughtColor, anomaly.Name, ShiftAnomalyCategories.Label(anomaly.Category), caughtTint);
        }

        if (missed.Count > 0)
        {
            AddHeader($"MISSED ({missed.Count})");
            foreach (ShiftAnomalyResult anomaly in missed)
                AddLine("MISSED", missedColor, anomaly.Name, ShiftAnomalyCategories.Label(anomaly.Category), missedTint);
        }

        if (falseFlags.Count > 0)
        {
            AddHeader($"FALSE FLAGS ({falseFlags.Count})");
            foreach (ShiftAnomalyCategory category in falseFlags)
                AddLine("FLAGGED", falseFlagColor, $"{ShiftAnomalyCategories.Label(category)} box ticked", "Nothing present", falseFlagTint);
        }

        EndList();
    }

    private void BeginList()
    {
        _headersUsed = 0;
        _linesUsed = 0;
    }

    private void EndList()
    {
        for (int i = _headersUsed; i < _headers.Count; i++)
            _headers[i].gameObject.SetActive(false);
        for (int i = _linesUsed; i < _lines.Count; i++)
            _lines[i].gameObject.SetActive(false);
    }

    private void AddHeader(string text)
    {
        if (sectionHeaderTemplate == null)
            return;

        if (_headersUsed >= _headers.Count)
        {
            TextMeshProUGUI clone = Instantiate(sectionHeaderTemplate, sectionHeaderTemplate.transform.parent);
            clone.name = $"{sectionHeaderTemplate.name} {_headers.Count + 1:00}";
            _headers.Add(clone);
        }

        TextMeshProUGUI header = _headers[_headersUsed++];
        header.text = text;
        header.gameObject.SetActive(true);
        header.transform.SetAsLastSibling();
    }

    private void AddLine(string status, Color statusColor, string label, string note, Color tint)
    {
        if (lineTemplate == null)
            return;

        if (_linesUsed >= _lines.Count)
        {
            ShiftReportDetailLine clone = Instantiate(lineTemplate, lineTemplate.transform.parent);
            clone.name = $"{lineTemplate.name} {_lines.Count + 1:00}";
            _lines.Add(clone);
        }

        ShiftReportDetailLine line = _lines[_linesUsed++];
        line.Set(status, statusColor, label, note, tint);
        line.gameObject.SetActive(true);
        line.transform.SetAsLastSibling();
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && SFXController.Instance != null)
            SFXController.Instance.Play(clip, soundVolume);
    }
}
