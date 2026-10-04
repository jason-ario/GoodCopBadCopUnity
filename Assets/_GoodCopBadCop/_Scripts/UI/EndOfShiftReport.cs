using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The end-of-shift "Shift Report": a paper document that fills with one row per processed subject
/// (ID photo, name, ID number, anomalies caught / total, coupons earned, verdict icon), followed by
/// a Shift Summary — average checkpoint integrity across those subjects, total earnings, and current
/// population — and finally a Continue button. It never awards money: every coupon shown was
/// already paid out at verdict time by <see cref="SuspectController"/>.
///
/// Rows are pooled clones of <see cref="rowTemplate"/> inside <see cref="rowScroll"/>, which
/// follows each new row as it pops in.
///
/// This screen is a full-screen modal that disables player control, so it is the single most
/// dangerous place in the game to get stuck. Everything below is built so that
/// <b>the player can always leave</b>:
///
/// 1. The affordance is shown by <see cref="DriveReportRoutine"/>, which is separate from and
///    watches over the reveal. A reveal that stalls or dies cannot suppress the button.
/// 2. Every row animation runs inside this component's own coroutine (never a child-owned
///    Coroutine handle), uses unscaled time, and the whole sequence is capped by
///    <see cref="maxTotalRevealDuration"/>.
/// 3. Any input skips the remaining animation.
/// 4. Both players get a working Continue button — not just the host.
/// 5. Pressing Continue starts <see cref="WatchdogAfterContinue"/>: if the transition has not torn
///    this screen down in time, the screen dismisses itself and restores control.
/// </summary>
public class EndOfShiftReportUI : MonoBehaviour
{
    /// <summary>A summary line (label + value) that fades in and counts up its value.</summary>
    [Serializable]
    public class SummaryLine
    {
        public CanvasGroup root;
        public TextMeshProUGUI valueText;
    }

    [Header("Document")]
    [Tooltip("Full-screen darkening behind the paper.")]
    [SerializeField] private CanvasGroup dimmer;
    [Tooltip("The paper document. Hidden on Continue so the dimmer remains as an overlay while the screen fades.")]
    [SerializeField] private RectTransform paper;
    [Tooltip("Filled with the report's day. {0} = day number.")]
    [SerializeField] private TextMeshProUGUI dayText;
    [SerializeField] private string dayTextFormat = "Shift Day {0}";
    [Tooltip("Filled with a report reference number. {0} = day, {1} = subjects processed.")]
    [SerializeField] private TextMeshProUGUI reportNumberText;
    [SerializeField] private string reportNumberFormat = "Report No. {0:000}-{1}";

    [Header("Rows")]
    [Tooltip("Inactive template row. Rows are cloned from it on demand and pooled across reports.")]
    [SerializeField] private ShiftReportSubjectRow rowTemplate;
    [SerializeField] private ScrollRect rowScroll;
    [Tooltip("Shown in the row area when no subjects were processed this shift.")]
    [SerializeField] private GameObject emptyState;
    [Tooltip("Seconds to ease the scroll view down to a newly revealed row.")]
    [SerializeField] private float autoScrollDuration = 0.25f;
    [Tooltip("Optional popup opened by clicking a row once the reveal is finished. Shows that subject's caught / missed anomalies.")]
    [SerializeField] private ShiftReportSubjectDetailPopup detailPopup;

    [Header("Summary")]
    [SerializeField] private CanvasGroup summaryHeader;
    [SerializeField] private SummaryLine integrityLine = new SummaryLine();
    [SerializeField] private SummaryLine earningsLine = new SummaryLine();
    [SerializeField] private SummaryLine populationLine = new SummaryLine();
    [Tooltip("Optional decorative stamp that slams onto the paper once the summary is complete.")]
    [SerializeField] private RectTransform closingStamp;

    [Header("Continue")]
    [SerializeField] private GameObject continueButton;
    [Tooltip("Optional 'waiting' label shown to a non-host player after they press Continue, while the host's transition runs.")]
    [SerializeField] private GameObject waitingForHostText;

    [Header("Timing")]
    [SerializeField] private float initialDelay = 0.25f;
    [Tooltip("Seconds the black backdrop takes to fade in. Always completes before the paper appears, even if the player skips.")]
    [SerializeField] private float dimmerFadeDuration = 0.6f;
    [Tooltip("Pause on the fully black backdrop before the paper slides in.")]
    [SerializeField] private float postDimmerDelay = 0.15f;
    [Tooltip("Seconds the paper takes to slide up and fade in.")]
    [SerializeField] private float paperInDuration = 0.45f;
    [SerializeField] private float paperSlideDistance = 60f;
    [Tooltip("Seconds each row takes to pop into place.")]
    [SerializeField] private float rowPopDuration = 0.3f;
    [Tooltip("Pause between a row popping in and its verdict being stamped.")]
    [SerializeField] private float verdictDelay = 0.1f;
    [SerializeField] private float verdictStampDuration = 0.22f;
    [SerializeField] private float killStrikeDuration = 0.25f;
    [Tooltip("Pause after each row before the next one pops in.")]
    [SerializeField] private float rowInterval = 0.12f;
    [SerializeField] private float summaryLineFadeDuration = 0.25f;
    [SerializeField] private float summaryCountDuration = 0.6f;
    [SerializeField] private float summaryLineInterval = 0.15f;
    [SerializeField] private float closingStampDuration = 0.25f;
    [SerializeField] private float finalDelayBeforeContinue = 0.25f;

    [Header("Audio")]
    [Tooltip("Played once when the report opens (also when the reveal is skipped).")]
    [SerializeField] private AudioClip openSound;
    [SerializeField, Range(0f, 1f)] private float openSoundVolume = 0.8f;
    [Tooltip("Paper sounds played as each row pops in. One is picked at random per row (never the same twice in a row).")]
    [SerializeField] private AudioClip[] rowSounds;
    [SerializeField, Range(0f, 1f)] private float rowSoundVolume = 0.6f;
    [Tooltip("Played as each verdict is stamped (and the closing stamp).")]
    [SerializeField] private AudioClip stampSound;
    [SerializeField, Range(0f, 1f)] private float stampSoundVolume = 0.7f;
    [Tooltip("Played when the Shift Summary section appears.")]
    [SerializeField] private AudioClip summarySound;
    [SerializeField, Range(0f, 1f)] private float summarySoundVolume = 0.6f;
    [Tooltip("Played when a row is clicked to open its detail popup.")]
    [SerializeField] private AudioClip rowClickSound;
    [SerializeField, Range(0f, 1f)] private float rowClickSoundVolume = 0.6f;
    [Tooltip("Played when Continue is pressed and the report closes.")]
    [SerializeField] private AudioClip closeSound;
    [SerializeField, Range(0f, 1f)] private float closeSoundVolume = 0.8f;
    [Tooltip("Random pitch range so repeated sounds don't feel mechanical.")]
    [SerializeField] private Vector2 soundPitchRange = new Vector2(0.94f, 1.06f);

    [Header("Failsafes")]
    [Tooltip("Hard cap on the entire reveal sequence. Past this the report snaps to its final state and shows Continue immediately.")]
    [SerializeField] private float maxTotalRevealDuration = 45f;
    [Tooltip("After pressing Continue, how long to wait for the shift transition to tear this screen down before dismissing it locally so the player is never trapped.")]
    [SerializeField] private float continueWatchdogTimeout = 15f;
    [Tooltip("When true, any key / click / gamepad press skips the rest of the reveal animation.")]
    [SerializeField] private bool allowSkipInput = true;

    // Rows cloned from the template. Kept and reused across reports.
    private readonly List<ShiftReportSubjectRow> _rows = new List<ShiftReportSubjectRow>();

    private Coroutine _driverRoutine;
    private Coroutine _revealRoutine;
    private Coroutine _scrollRoutine;

    private ShiftReportData _data;

    private bool _revealComplete;
    private bool _skipRequested;
    private bool _affordanceShown;
    private bool _continuePressed;
    private bool _openSoundPlayed;
    private bool _backdropReady;
    private int _lastRowSoundIndex = -1;
    private Button _continueButtonComponent;
    private CanvasGroup _paperGroup;
    private Vector2 _paperRestPosition;
    private bool _paperRestCaptured;

    private void Awake()
    {
        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);

        CachePaperRest();
        WireContinueButton();
        HideAll();
    }

    private void CachePaperRest()
    {
        if (_paperRestCaptured || paper == null)
            return;

        _paperRestPosition = paper.anchoredPosition;
        _paperRestCaptured = true;
    }

    private void WireContinueButton()
    {
        Button button = GetContinueButton();
        if (button == null)
            return;

        // Wired in code so the prefab never depends on a persistent onClick entry. Re-entry is
        // guarded in OnContinueButtonPressed, so an extra persistent call is harmless.
        button.onClick.RemoveListener(OnContinueButtonPressed);
        button.onClick.AddListener(OnContinueButtonPressed);
    }

    public void PlayReport(ShiftReportData data)
    {
        StopAllReportRoutines();

        _data = data ?? new ShiftReportData(0, null, -1, 0, 0);

        _revealComplete = false;
        _skipRequested = false;
        _affordanceShown = false;
        _continuePressed = false;
        _openSoundPlayed = false;
        _backdropReady = false;

        gameObject.SetActive(true);
        CachePaperRest();
        WireContinueButton();

        // Deliberately no payout here: coupons are issued per subject at verdict time.
        _driverRoutine = StartCoroutine(DriveReportRoutine());
    }

    public void HideAll()
    {
        if (detailPopup != null)
            detailPopup.CloseImmediate();

        SetGroupAlpha(dimmer, 0f);

        if (paper != null)
        {
            paper.gameObject.SetActive(false);
            if (_paperRestCaptured)
                paper.anchoredPosition = _paperRestPosition;
        }
        SetGroupAlpha(GetPaperGroup(), 0f);

        foreach (ShiftReportSubjectRow row in _rows)
        {
            if (row != null)
                row.Hide();
        }

        if (emptyState != null)
            emptyState.SetActive(false);

        SetGroupAlpha(summaryHeader, 0f);
        HideSummaryLine(integrityLine);
        HideSummaryLine(earningsLine);
        HideSummaryLine(populationLine);

        if (closingStamp != null)
            closingStamp.gameObject.SetActive(false);

        if (continueButton != null)
            continueButton.SetActive(false);

        if (waitingForHostText != null)
            waitingForHostText.SetActive(false);

        SetScrollPosition(1f);
    }

    /// <summary>
    /// Owns the reveal and, unconditionally, the appearance of the Continue affordance. Whether the
    /// reveal completes, is skipped, or hangs past <see cref="maxTotalRevealDuration"/>, control
    /// returns to this method and the button appears.
    /// </summary>
    private IEnumerator DriveReportRoutine()
    {
        HideAll();
        FillHeader();

        // The solid black backdrop always fades in fully before anything else appears. It is not
        // skippable (skip input is ignored until _backdropReady) and is bounded by its own
        // duration, so it cannot delay the Continue affordance by more than a second or so.
        yield return WaitUnscaled(initialDelay);
        yield return FadeBackdrop(dimmerFadeDuration);
        yield return WaitUnscaled(postDimmerDelay);
        _backdropReady = true;

        _revealRoutine = StartCoroutine(RevealReportRoutine());

        float elapsed = 0f;
        while (!_revealComplete && !_skipRequested && elapsed < maxTotalRevealDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!_revealComplete)
        {
            if (!_skipRequested)
            {
                Debug.LogWarning(
                    $"[EndOfShiftReportUI] Reveal did not finish within {maxTotalRevealDuration:0.#}s — " +
                    "snapping the report to its final state so the player can continue.");
            }

            if (_revealRoutine != null)
            {
                StopCoroutine(_revealRoutine);
                _revealRoutine = null;
            }

            SnapToFinalState();
        }

        ShowContinueAffordance();
        _driverRoutine = null;
    }

    private void Update()
    {
        if (!allowSkipInput || !_backdropReady || _skipRequested || _affordanceShown)
            return;

        if (AnySkipInputThisFrame())
            _skipRequested = true;
    }

    /// <summary>
    /// True on the frame the player presses anything that should skip the reveal. Deliberately
    /// broad — on a screen whose only job is "show numbers, then let me leave", impatience must
    /// never be punished with a wait the player cannot shorten.
    /// </summary>
    private static bool AnySkipInputThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            return true;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            return true;

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null &&
            (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame))
            return true;

        return false;
    }

    private IEnumerator RevealReportRoutine()
    {
        SetGroupAlpha(dimmer, 1f);

        PlayOpenSound();

        if (paper != null)
            paper.gameObject.SetActive(true);
        yield return PaperIn(paperInDuration);

        IReadOnlyList<ShiftSubjectResult> subjects = _data.Subjects;

        if (subjects.Count == 0 && emptyState != null)
            emptyState.SetActive(true);

        for (int i = 0; i < subjects.Count; i++)
        {
            if (_skipRequested)
                yield break;

            ShiftReportSubjectRow row = GetRow(i);
            if (row == null)
                continue;

            // Bind while invisible, then pop the whole row in as one piece.
            row.Bind(i, subjects[i]);
            row.Show();
            FollowNewestRow();

            PlayRowSound();
            yield return row.PopIn(rowPopDuration);

            yield return WaitUnscaled(verdictDelay);

            PlaySound(stampSound, stampSoundVolume);
            yield return row.StampVerdict(verdictStampDuration);

            if (row.IsKilled)
            {
                PlaySound(stampSound, stampSoundVolume * 0.8f);
                yield return row.StrikePhoto(killStrikeDuration);
            }

            yield return WaitUnscaled(rowInterval);
        }

        PlaySound(summarySound, summarySoundVolume);
        yield return FadeGroup(summaryHeader, summaryLineFadeDuration);

        yield return RevealSummaryLine(integrityLine, _data.AverageIntegrity < 0f ? -1 : Mathf.RoundToInt(_data.AverageIntegrity * 100f), FormatIntegrity);
        yield return WaitUnscaled(summaryLineInterval);

        yield return RevealSummaryLine(earningsLine, _data.TotalCouponsEarned, FormatEarnings);
        yield return WaitUnscaled(summaryLineInterval);

        yield return RevealSummaryLine(populationLine, _data.PopulationAlive, FormatPopulation);

        if (closingStamp != null)
        {
            yield return WaitUnscaled(summaryLineInterval);
            PlaySound(stampSound, stampSoundVolume);
            yield return StampIn(closingStamp, closingStampDuration);
        }

        yield return WaitUnscaled(finalDelayBeforeContinue);

        _revealComplete = true;
        _revealRoutine = null;
    }

    /// <summary>
    /// Fills the entire report instantly from the cached payload. Used when the reveal is skipped or
    /// had to be abandoned, so the player still sees real results rather than a half-drawn screen.
    /// </summary>
    private void SnapToFinalState()
    {
        FillHeader();
        PlayOpenSound();

        SetGroupAlpha(dimmer, 1f);
        if (paper != null)
        {
            paper.gameObject.SetActive(true);
            if (_paperRestCaptured)
                paper.anchoredPosition = _paperRestPosition;
        }
        SetGroupAlpha(GetPaperGroup(), 1f);

        IReadOnlyList<ShiftSubjectResult> subjects = _data.Subjects;
        for (int i = 0; i < subjects.Count; i++)
        {
            ShiftReportSubjectRow row = GetRow(i);
            if (row == null)
                continue;

            row.Bind(i, subjects[i]);
            row.SnapFinal();
        }

        if (emptyState != null)
            emptyState.SetActive(subjects.Count == 0);

        StopScrollRoutine();
        RebuildRowLayout();
        SetScrollPosition(1f);

        SetGroupAlpha(summaryHeader, 1f);
        SnapSummaryLine(integrityLine, FormatIntegrity(_data.AverageIntegrity < 0f ? -1 : Mathf.RoundToInt(_data.AverageIntegrity * 100f)));
        SnapSummaryLine(earningsLine, FormatEarnings(_data.TotalCouponsEarned));
        SnapSummaryLine(populationLine, FormatPopulation(_data.PopulationAlive));

        if (closingStamp != null)
        {
            closingStamp.gameObject.SetActive(true);
            closingStamp.localScale = Vector3.one;
        }
    }

    private void FillHeader()
    {
        if (dayText != null)
            dayText.text = string.Format(dayTextFormat, _data.Day);

        if (reportNumberText != null)
            reportNumberText.text = string.Format(reportNumberFormat, _data.Day, _data.Subjects.Count);
    }

    // ----- Rows -----

    /// <summary>Returns the pooled row for <paramref name="index"/>, cloning the template on demand.</summary>
    private ShiftReportSubjectRow GetRow(int index)
    {
        if (rowTemplate == null)
            return null;

        while (_rows.Count <= index)
        {
            ShiftReportSubjectRow clone = Instantiate(rowTemplate, rowTemplate.transform.parent);
            clone.name = $"{rowTemplate.name} {_rows.Count + 1:00}";
            clone.Hide();
            clone.DetailsRequested += OnRowDetailsRequested;
            _rows.Add(clone);
        }

        return _rows[index];
    }

    /// <summary>Makes every bound row clickable (or not). Only enabled once the reveal is over.</summary>
    private void SetRowDetailsInteractable(bool interactable)
    {
        bool canOpen = interactable && detailPopup != null;
        int count = _data != null ? _data.Subjects.Count : 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                _rows[i].SetDetailsInteractable(canOpen && i < count);
        }
    }

    private void OnRowDetailsRequested(ShiftReportSubjectRow row)
    {
        // Only after the reveal and before Continue — never competes with skip or the transition.
        if (detailPopup == null || row == null || !_affordanceShown || _continuePressed)
            return;

        PlayUISound(rowClickSound, rowClickSoundVolume);
        detailPopup.Open(row.Index, row.Subject);
    }

    /// <summary>Eases the scroll view to the bottom so the newest row stays in view.</summary>
    private void FollowNewestRow()
    {
        if (rowScroll == null)
            return;

        RebuildRowLayout();
        StopScrollRoutine();

        // Owned by this component (never a child), so it cannot strand the reveal if it dies.
        _scrollRoutine = StartCoroutine(ScrollToBottomRoutine());
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        rowScroll.StopMovement();
        float start = rowScroll.verticalNormalizedPosition;

        float elapsed = 0f;
        while (elapsed < autoScrollDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / autoScrollDuration);
            float eased = 1f - (1f - t) * (1f - t);
            rowScroll.verticalNormalizedPosition = Mathf.Lerp(start, 0f, eased);
            yield return null;
        }

        rowScroll.verticalNormalizedPosition = 0f;
        _scrollRoutine = null;
    }

    private void StopScrollRoutine()
    {
        if (_scrollRoutine != null)
        {
            StopCoroutine(_scrollRoutine);
            _scrollRoutine = null;
        }
    }

    private void RebuildRowLayout()
    {
        if (rowScroll == null)
            return;

        if (rowScroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowScroll.content);
        if (rowScroll.viewport != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowScroll.viewport);
    }

    /// <summary>1 = top, 0 = bottom.</summary>
    private void SetScrollPosition(float normalized)
    {
        if (rowScroll == null)
            return;

        rowScroll.StopMovement();
        rowScroll.verticalNormalizedPosition = normalized;
    }

    // ----- Summary -----

    private static string FormatIntegrity(int percent) => percent < 0 ? "--" : $"{percent}%";
    private static string FormatEarnings(int coupons) => coupons.ToString("N0");
    private static string FormatPopulation(int population) => population < 0 ? "--" : population.ToString("N0");

    private IEnumerator RevealSummaryLine(SummaryLine line, int target, Func<int, string> format)
    {
        if (line == null || line.root == null)
            yield break;

        if (line.valueText != null)
            line.valueText.text = format(target < 0 ? target : 0);

        yield return FadeGroup(line.root, summaryLineFadeDuration);

        if (line.valueText == null)
            yield break;

        // Counts up from zero; unknown values (negative) are shown as-is.
        if (target > 0 && summaryCountDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < summaryCountDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / summaryCountDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                line.valueText.text = format(Mathf.RoundToInt(target * eased));
                yield return null;
            }
        }

        line.valueText.text = format(target);
    }

    private static void SnapSummaryLine(SummaryLine line, string value)
    {
        if (line == null)
            return;

        SetGroupAlpha(line.root, 1f);
        if (line.valueText != null)
            line.valueText.text = value;
    }

    private static void HideSummaryLine(SummaryLine line)
    {
        if (line == null)
            return;

        SetGroupAlpha(line.root, 0f);
        if (line.valueText != null)
            line.valueText.text = string.Empty;
    }

    // ----- Generic animation helpers (all run inside this component's coroutines) -----

    private CanvasGroup GetPaperGroup()
    {
        if (_paperGroup != null || paper == null)
            return _paperGroup;

        _paperGroup = paper.GetComponent<CanvasGroup>();
        if (_paperGroup == null)
            _paperGroup = paper.gameObject.AddComponent<CanvasGroup>();

        return _paperGroup;
    }

    private IEnumerator PaperIn(float duration)
    {
        CanvasGroup group = GetPaperGroup();
        Vector2 from = _paperRestPosition - new Vector2(0f, paperSlideDistance);

        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            SetGroupAlpha(group, eased);
            if (paper != null && _paperRestCaptured)
                paper.anchoredPosition = Vector2.LerpUnclamped(from, _paperRestPosition, eased);

            yield return null;
        }

        SetGroupAlpha(group, 1f);
        if (paper != null && _paperRestCaptured)
            paper.anchoredPosition = _paperRestPosition;
    }

    /// <summary>
    /// Fades the black backdrop to fully opaque. Unlike <see cref="FadeGroup"/> it ignores skip
    /// requests, so the backdrop is always solid before the paper appears.
    /// </summary>
    private IEnumerator FadeBackdrop(float duration)
    {
        if (dimmer == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-in-out so the screen darkens smoothly rather than snapping at either end.
            dimmer.alpha = t * t * (3f - 2f * t);
            yield return null;
        }

        dimmer.alpha = 1f;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float duration)
    {
        if (group == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = 1f - (1f - t) * (1f - t);
            yield return null;
        }

        group.alpha = 1f;
    }

    private static IEnumerator StampIn(RectTransform target, float duration)
    {
        target.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(2f, 1f, 1f - Mathf.Pow(1f - t, 3f));
            target.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        target.localScale = Vector3.one;
    }

    private static void SetGroupAlpha(CanvasGroup group, float alpha)
    {
        if (group != null)
            group.alpha = alpha;
    }

    /// <summary>Unscaled wait so the report always progresses, even if something zeroed timeScale.</summary>
    private static IEnumerator WaitUnscaled(float seconds)
    {
        if (seconds <= 0f)
            yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ----- Audio -----

    private void PlayOpenSound()
    {
        if (_openSoundPlayed)
            return;

        _openSoundPlayed = true;

        if (openSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(openSound, openSoundVolume);
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (_skipRequested || clip == null || SFXController.Instance == null)
            return;

        float pitch = UnityEngine.Random.Range(soundPitchRange.x, soundPitchRange.y);
        SFXController.Instance.Play(clip, volume, pitch);
    }

    /// <summary>Picks a random paper sound for a row, avoiding an immediate repeat.</summary>
    private void PlayRowSound()
    {
        if (rowSounds == null || rowSounds.Length == 0)
            return;

        int index = UnityEngine.Random.Range(0, rowSounds.Length);
        if (rowSounds.Length > 1 && index == _lastRowSoundIndex)
            index = (index + 1 + UnityEngine.Random.Range(0, rowSounds.Length - 1)) % rowSounds.Length;

        _lastRowSoundIndex = index;
        PlaySound(rowSounds[index], rowSoundVolume);
    }

    /// <summary>Interaction feedback — plays regardless of the reveal's skip state.</summary>
    private static void PlayUISound(AudioClip clip, float volume)
    {
        if (clip != null && SFXController.Instance != null)
            SFXController.Instance.Play(clip, volume);
    }

    // ----- Continue -----

    /// <summary>
    /// Reveals the Continue button to <b>every</b> player. Letting either player continue is safe
    /// because <see cref="ShiftManager.StartInBetweenShiftSequence"/> is latched server-side, so
    /// duplicate or simultaneous presses collapse into a single transition.
    /// </summary>
    private void ShowContinueAffordance()
    {
        if (_affordanceShown)
            return;

        _affordanceShown = true;

        if (waitingForHostText != null)
            waitingForHostText.SetActive(false);

        if (continueButton != null)
            continueButton.SetActive(true);

        SetContinueInteractable(true);
        SetRowDetailsInteractable(true);
    }

    private Button GetContinueButton()
    {
        if (_continueButtonComponent == null && continueButton != null)
            _continueButtonComponent = continueButton.GetComponent<Button>()
                                       ?? continueButton.GetComponentInChildren<Button>(true);

        return _continueButtonComponent;
    }

    private void SetContinueInteractable(bool interactable)
    {
        Button button = GetContinueButton();
        if (button != null)
            button.interactable = interactable;
    }

    public void OnContinueButtonPressed()
    {
        // Guard re-entry: a double-click, or both the button and a skip-input landing on the same
        // frame, must not queue two transitions.
        if (_continuePressed)
            return;

        _continuePressed = true;

        PlayUISound(closeSound, closeSoundVolume);
        StopAllReportRoutines();
        SetContinueInteractable(false);
        SetRowDetailsInteractable(false);
        if (detailPopup != null)
            detailPopup.CloseImmediate();

        // Only a non-host sees a "waiting for host" label — the host is the one doing the work.
        bool isHost = GlobalHostVariables.Instance == null || GlobalHostVariables.Instance.IsServer;
        if (!isHost && waitingForHostText != null)
            waitingForHostText.SetActive(true);

        // Hide only the paper — leave the dimmer as an overlay while the screen fades.
        if (paper != null)
            paper.gameObject.SetActive(false);

        if (ShiftManager.Instance == null)
        {
            Debug.LogError("[EndOfShiftReportUI] ShiftManager.Instance is null — nothing can advance the " +
                           "shift. Dismissing the report locally rather than leaving the player behind a " +
                           "dead overlay.");
            UIController.Instance?.ForceDismissEndOfShiftReport();
            return;
        }

        ShiftManager.Instance.StartInBetweenShiftSequence();

        StartCoroutine(WatchdogAfterContinue());
    }

    /// <summary>
    /// The final safety net. Pressing Continue is supposed to end with the shift transition
    /// deactivating this screen; if that has not happened within <see cref="continueWatchdogTimeout"/>
    /// seconds, the screen dismisses itself so the player is never trapped behind a dead overlay.
    /// Lives on the report root, so the success case cancels it automatically on deactivation.
    /// </summary>
    private IEnumerator WatchdogAfterContinue()
    {
        float elapsed = 0f;
        while (elapsed < continueWatchdogTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning(
            $"[EndOfShiftReportUI] Shift transition did not dismiss the report within " +
            $"{continueWatchdogTimeout:0.#}s of pressing Continue — dismissing locally so the player " +
            "is not trapped on the report screen.");

        UIController.Instance?.ForceDismissEndOfShiftReport();
    }

    private void StopAllReportRoutines()
    {
        StopScrollRoutine();

        if (_driverRoutine != null)
        {
            StopCoroutine(_driverRoutine);
            _driverRoutine = null;
        }

        if (_revealRoutine != null)
        {
            StopCoroutine(_revealRoutine);
            _revealRoutine = null;
        }
    }
}
