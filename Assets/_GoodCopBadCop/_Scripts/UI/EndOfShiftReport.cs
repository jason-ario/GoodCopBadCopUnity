using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The end-of-shift report screen: an informational daily earnings report / shift summary. Lists
/// every processed subject with the anomalies caught and the coupons that verdict paid, then the
/// passed / quarantined / killed tallies, then the day's total. It never awards money — every
/// coupon shown was already paid out at verdict time by <see cref="SuspectController"/>.
///
/// Rows are pooled: the serialized <see cref="rows"/> are used first and extra rows are cloned from
/// the first one on demand, inside an optional <see cref="_rowScroll"/> that follows each new row.
///
/// This screen is a full-screen modal that disables player control, so it is the single most
/// dangerous place in the game to get stuck. Everything below is built so that
/// <b>the player can always leave</b>:
///
/// 1. The affordance is shown by <see cref="DriveReportRoutine"/>, which is separate from and
///    watches over the reveal. A reveal that stalls or dies cannot suppress the button.
/// 2. Every reveal wait is bounded (see <see cref="TMPTextReveal.RevealTextBounded"/>) and the whole
///    sequence is capped by <see cref="_maxTotalRevealDuration"/>.
/// 3. Any input skips the remaining animation.
/// 4. Both players get a working Continue button — not just the host.
/// 5. Pressing Continue starts <see cref="WatchdogAfterContinue"/>: if the transition has not torn
///    this screen down in time, the screen dismisses itself and restores control.
/// </summary>
public class EndOfShiftReportUI : MonoBehaviour
{
    /// <summary>One rendered report line.</summary>
    private struct ReportLine
    {
        public string Label;
        public string Value; // null = label only
        public EndOfShiftReportRow.Tone Tone;
    }

    [Header("Rows")]
    [Tooltip("Pre-placed rows, used first. Extra rows are cloned from the first entry when needed.")]
    [SerializeField] private List<EndOfShiftReportRow> rows = new List<EndOfShiftReportRow>();

    [Header("Scrolling")]
    [Tooltip("Optional scroll view around the rows. Auto-scrolls to each row as it is revealed.")]
    [SerializeField] private ScrollRect _rowScroll;
    [Tooltip("Seconds to ease the scroll view down to a newly revealed row.")]
    [SerializeField] private float _autoScrollDuration = 0.25f;

    [Header("Residents Who Fully Mutated")]
    [SerializeField] private GameObject residentsMutatedRoot;
    [SerializeField] private TextMeshProUGUI residentsMutatedText;
    [SerializeField] private TMPTextReveal residentsMutatedReveal;
    [SerializeField] private TMPWobbleText residentsMutatedWobble;

    [Header("Civilians Killed")]
    [SerializeField] private GameObject civiliansKilledRoot;
    [SerializeField] private TextMeshProUGUI civiliansKilledText;
    [SerializeField] private TMPTextReveal civiliansKilledReveal;
    [SerializeField] private TMPWobbleText civiliansKilledWobble;

    [Header("Net Earnings")]
    [SerializeField] private GameObject netEarningsRoot;
    [SerializeField] private TextMeshProUGUI netEarningsText;
    [SerializeField] private TMPTextReveal netEarningsReveal;
    [SerializeField] private TMPWobbleText netEarningsWobble;

    [Header("Current Population")]
    [SerializeField] private GameObject currentPopulationRoot;
    [SerializeField] private TextMeshProUGUI currentPopulationText;
    [SerializeField] private TMPTextReveal currentPopulationReveal;
    [SerializeField] private TMPWobbleText currentPopulationWobble;

    [Header("Continue")]
    [SerializeField] private GameObject continueButton;
    [Tooltip("Optional 'waiting' label shown to a non-host player after they press Continue, while the host's transition runs.")]
    [SerializeField] private GameObject waitingForHostText;

    [Header("Layout")]
    [Tooltip("The Container child of BG that holds all report content. Deactivated on Continue so the BG remains as an overlay while the screen fades.")]
    [SerializeField] private GameObject _contentContainer;

    [Header("Timing")]
    [SerializeField] private float initialDelay = 0.35f;
    [Tooltip("Seconds the content panel takes to fade in after the banner appears.")]
    [SerializeField] private float contentFadeDuration = 0.3f;
    [Tooltip("Seconds each row takes to fade and ease into place.")]
    [SerializeField] private float rowFadeDuration = 0.45f;
    [SerializeField] private float rewardRevealDelay = 0.12f;
    [SerializeField] private float lineRevealDelay = 0.3f;
    [SerializeField] private float finalDelayBeforeContinue = 0.4f;

    [Header("Audio")]
    [Tooltip("Played once when the report page opens (also when the reveal is skipped).")]
    [SerializeField] private AudioClip openSound;
    [SerializeField, Range(0f, 1f)] private float openSoundVolume = 0.8f;
    [Tooltip("Played as each row fades in. Not played for rows filled instantly by a skip.")]
    [SerializeField] private AudioClip rowSound;
    [SerializeField, Range(0f, 1f)] private float rowSoundVolume = 0.6f;
    [Tooltip("Random pitch range for the row sound so repeated rows don't sound mechanical.")]
    [SerializeField] private Vector2 rowSoundPitchRange = new Vector2(0.94f, 1.06f);

    [Header("Failsafes")]
    [Tooltip("Hard cap on any single text reveal. Past this the line snaps to its final state and the report moves on.")]
    [SerializeField] private float _maxSingleRevealDuration = 6f;
    [Tooltip("Hard cap on the entire reveal sequence. Past this the report snaps to its final state and shows Continue immediately.")]
    [SerializeField] private float _maxTotalRevealDuration = 45f;
    [Tooltip("After pressing Continue, how long to wait for the shift transition to tear this screen down before dismissing it locally so the player is never trapped.")]
    [SerializeField] private float _continueWatchdogTimeout = 15f;
    [Tooltip("When true, any key / click / gamepad press skips the rest of the reveal animation.")]
    [SerializeField] private bool _allowSkipInput = true;

    [Header("Colors")]
    [SerializeField] private Color rewardColor = Color.white;
    [SerializeField] private Color penaltyColor = new Color(1f, 0.3f, 0.3f);

    [Header("Wobble Profiles")]
    [SerializeField] private TMPWobbleProfile normalLabelProfile;
    [SerializeField] private TMPWobbleProfile rewardValueProfile;
    [SerializeField] private TMPWobbleProfile penaltyValueProfile;
    [SerializeField] private TMPWobbleProfile positiveTotalProfile;
    [SerializeField] private TMPWobbleProfile negativeTotalProfile;

    [SerializeField] private GameObject banner; 
    [SerializeField] TMPTextReveal subHeaderText;

    private const string SubHeaderLabel = "Checkpoint Performance Summary";

    // Rows cloned beyond the serialized pool. Kept and reused across reports.
    private readonly List<EndOfShiftReportRow> _extraRows = new List<EndOfShiftReportRow>();

    private Coroutine driverRoutine;
    private Coroutine revealRoutine;
    private Coroutine scrollRoutine;

    // Cached payload for the current report, so the failsafe path can snap straight to the
    // final state without re-deriving anything.
    private ShiftReportData _data;
    private readonly List<ReportLine> _lines = new List<ReportLine>();

    private bool _revealComplete;
    private bool _skipRequested;
    private bool _affordanceShown;
    private bool _continuePressed;
    private bool _openSoundPlayed;
    private Button _continueButtonComponent;
    private CanvasGroup _contentCanvasGroup;

    private void Awake()
    {
        HideAll();
    }

    public void PlayReport(ShiftReportData data)
    {
        StopAllReportRoutines();

        _data = data ?? new ShiftReportData(0, null, -1, 0, 0);
        BuildLines();

        _revealComplete = false;
        _skipRequested = false;
        _affordanceShown = false;
        _continuePressed = false;
        _openSoundPlayed = false;

        gameObject.SetActive(true);

        // Deliberately no payout here: coupons are issued per subject at verdict time.
        driverRoutine = StartCoroutine(DriveReportRoutine());
    }

    /// <summary>
    /// Header, one line per subject (anomalies caught + coupons that verdict paid), then the verdict
    /// tallies. Tallies carry no value — no earnings are attached to a verdict type.
    /// </summary>
    private void BuildLines()
    {
        _lines.Clear();

        _lines.Add(new ReportLine
        {
            Label = $"Citizens Processed: {_data.Subjects.Count}",
            Tone = EndOfShiftReportRow.Tone.Neutral
        });

        foreach (ShiftSubjectResult subject in _data.Subjects)
        {
            string name = string.IsNullOrWhiteSpace(subject.SubjectName) ? "Unknown Subject" : subject.SubjectName;
            string detail = subject.AnomaliesCaught == ShiftSubjectResult.NotAssessed
                ? "Fled"
                : $"{subject.AnomaliesCaught}/{subject.AnomaliesTotal} Anomalies";

            _lines.Add(new ReportLine
            {
                Label = $"{name}: {detail}",
                Value = subject.CouponsEarned > 0 ? $"Earned {subject.CouponsEarned}" : null,
                Tone = ToneForVerdict(subject.Verdict)
            });
        }

        _lines.Add(new ReportLine { Label = $"Passed: {_data.PassedCount}", Tone = EndOfShiftReportRow.Tone.Positive });
        _lines.Add(new ReportLine { Label = $"Quarantined: {_data.QuarantinedCount}", Tone = EndOfShiftReportRow.Tone.Positive });
        _lines.Add(new ReportLine { Label = $"Killed: {_data.KilledCount}", Tone = EndOfShiftReportRow.Tone.Negative });

        if (_data.FledCount > 0)
            _lines.Add(new ReportLine { Label = $"Fled Wounded: {_data.FledCount}", Tone = EndOfShiftReportRow.Tone.Negative });
    }

    /// <summary>Passed / Quarantined read green, Killed / Fled read orange — matching the tally rows.</summary>
    private static EndOfShiftReportRow.Tone ToneForVerdict(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Passed:
            case ShiftSubjectVerdict.Quarantined:
                return EndOfShiftReportRow.Tone.Positive;
            default:
                return EndOfShiftReportRow.Tone.Negative;
        }
    }

    public void HideAll()
    {
        if (banner != null)
            banner.SetActive(false);
        if (subHeaderText != null)
            subHeaderText.SetTextInstant(" ");
        _contentContainer?.SetActive(false);

        if (rows != null)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    rows[i].Hide();
            }
        }

        foreach (EndOfShiftReportRow row in _extraRows)
        {
            if (row != null)
                row.Hide();
        }

        SetScrollPosition(1f);
        
        Canvas.ForceUpdateCanvases();

        if (residentsMutatedRoot != null)
            residentsMutatedRoot.SetActive(false);
        if (residentsMutatedReveal != null)
            residentsMutatedReveal.Clear();
        else if (residentsMutatedText != null)
            residentsMutatedText.text = "";
        if (residentsMutatedWobble != null)
            residentsMutatedWobble.StopWobble();

        if (civiliansKilledRoot != null)
            civiliansKilledRoot.SetActive(false);
        if (civiliansKilledReveal != null)
            civiliansKilledReveal.Clear();
        else if (civiliansKilledText != null)
            civiliansKilledText.text = "";
        if (civiliansKilledWobble != null)
            civiliansKilledWobble.StopWobble();

        if (netEarningsRoot != null)
            netEarningsRoot.SetActive(false);

        if (netEarningsReveal != null)
            netEarningsReveal.Clear();
        else if (netEarningsText != null)
            netEarningsText.text = "";

        if (netEarningsWobble != null)
            netEarningsWobble.StopWobble();

        if (currentPopulationRoot != null)
            currentPopulationRoot.SetActive(false);
        if (currentPopulationReveal != null)
            currentPopulationReveal.Clear();
        else if (currentPopulationText != null)
            currentPopulationText.text = "";
        if (currentPopulationWobble != null)
            currentPopulationWobble.StopWobble();

        if (continueButton != null)
            continueButton.SetActive(false);

        if (waitingForHostText != null)
            waitingForHostText.SetActive(false);
    }

    /// <summary>
    /// Owns the reveal and, unconditionally, the appearance of the Continue affordance. Whether the
    /// reveal completes, is skipped, or hangs past <see cref="_maxTotalRevealDuration"/>, control
    /// returns to this method and the button appears.
    /// </summary>
    private IEnumerator DriveReportRoutine()
    {
        revealRoutine = StartCoroutine(RevealReportRoutine());

        float elapsed = 0f;
        while (!_revealComplete && !_skipRequested && elapsed < _maxTotalRevealDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!_revealComplete)
        {
            if (!_skipRequested)
            {
                Debug.LogWarning(
                    $"[EndOfShiftReportUI] Reveal did not finish within {_maxTotalRevealDuration:0.#}s — " +
                    "snapping the report to its final state so the player can continue.");
            }

            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }

            SnapToFinalState();
        }

        ShowContinueAffordance();
        driverRoutine = null;
    }

    private void Update()
    {
        if (!_allowSkipInput || _skipRequested || _affordanceShown)
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
        HideAll();

        yield return WaitUnscaled(initialDelay);

        if (banner != null)
            banner.SetActive(true);

        PlayOpenSound();

        yield return WaitUnscaled(0.5f);

        // The sub-header lives inside the content container, so the container must be active
        // before its text can animate — StartCoroutine is a no-op on an inactive GameObject.
        _contentContainer?.SetActive(true);
        yield return FadeContent(contentFadeDuration);

        if (subHeaderText != null)
            yield return subHeaderText.RevealTextBounded(SubHeaderLabel, _maxSingleRevealDuration);

        for (int i = 0; i < _lines.Count; i++)
        {
            if (_skipRequested)
                break;

            ReportLine line = _lines[i];
            EndOfShiftReportRow row = GetRow(i);

            if (row == null)
                continue;

            // Prepare the row fully (tone + label) while invisible, then fade it in as one piece
            // so the layout doesn't jump and the text isn't typed onto an empty bar.
            row.SetFade(0f);
            row.Show();
            row.Clear();
            row.SetTone(line.Tone);
            row.SetLabelInstant(line.Label, normalLabelProfile);
            FollowNewestRow();

            PlayRowSound();
            yield return row.FadeIn(rowFadeDuration);

            if (_skipRequested)
                break;

            if (line.Value != null)
            {
                yield return WaitUnscaled(rewardRevealDelay);
                yield return row.RevealValue(line.Value, rewardValueProfile, _maxSingleRevealDuration);
            }

            yield return WaitUnscaled(lineRevealDelay);
        }

        yield return RevealNetTotal(_data.TotalCouponsEarned);

        // Reveal residents who fully mutated overnight and went on to kill civilians.
        yield return RevealResidentsMutated(_data.ResidentsMutatedOvernight);

        // Reveal overnight civilians killed panel (purely informational — no monetary impact).
        yield return RevealCiviliansKilled(_data.CiviliansKilledOvernight);

        // Reveal the updated current population, accounting for any overnight civilian deaths.
        yield return RevealCurrentPopulation(_data.PopulationAlive);

        yield return WaitUnscaled(finalDelayBeforeContinue);

        _revealComplete = true;
        revealRoutine = null;
    }

    /// <summary>
    /// Fills the entire report instantly from the cached payload. Used when the reveal is skipped or
    /// had to be abandoned, so the player still sees real results rather than a half-drawn screen.
    /// </summary>
    private void SnapToFinalState()
    {
        if (banner != null)
            banner.SetActive(true);

        PlayOpenSound();

        _contentContainer?.SetActive(true);
        SetContentAlpha(1f);

        if (subHeaderText != null)
            subHeaderText.SetTextInstant(SubHeaderLabel);

        for (int i = 0; i < _lines.Count; i++)
        {
            EndOfShiftReportRow row = GetRow(i);
            if (row == null)
                continue;

            ReportLine line = _lines[i];
            row.SetInstant(line.Label, line.Value, line.Tone);
        }

        StopScrollRoutine();
        RebuildRowLayout();
        SetScrollPosition(0f);

        SnapPanel(netEarningsRoot, netEarningsReveal, netEarningsText,
            $"Net Daily Earnings: {FormatSignedNumber(_data.TotalCouponsEarned)}");
        if (netEarningsText != null)
            netEarningsText.color = _data.TotalCouponsEarned < 0 ? penaltyColor : rewardColor;

        SnapPanel(residentsMutatedRoot, residentsMutatedReveal, residentsMutatedText,
            $"Residents Who Fully Mutated: {_data.ResidentsMutatedOvernight}");
        SnapPanel(civiliansKilledRoot, civiliansKilledReveal, civiliansKilledText,
            $"Civilians Killed: {_data.CiviliansKilledOvernight}");
        SnapPanel(currentPopulationRoot, currentPopulationReveal, currentPopulationText,
            $"Current Population: {_data.PopulationAlive}");
    }

    private static void SnapPanel(GameObject root, TMPTextReveal reveal, TextMeshProUGUI text, string label)
    {
        if (root != null)
            root.SetActive(true);

        if (reveal != null)
            reveal.SetTextInstant(label);
        else if (text != null)
            text.text = label;
    }

    /// <summary>
    /// Returns the row for line <paramref name="index"/>: a serialized row first, otherwise a clone
    /// of the first serialized row, created on demand and pooled for later reports.
    /// </summary>
    private EndOfShiftReportRow GetRow(int index)
    {
        if (rows == null || rows.Count == 0 || rows[0] == null)
            return null;

        if (index < rows.Count)
            return rows[index];

        int extraIndex = index - rows.Count;
        while (_extraRows.Count <= extraIndex)
        {
            EndOfShiftReportRow template = rows[0];
            EndOfShiftReportRow clone = Instantiate(template, template.transform.parent);
            clone.name = $"{template.name} (Extra {_extraRows.Count + 1})";
            clone.Hide();
            _extraRows.Add(clone);
        }

        return _extraRows[extraIndex];
    }

    /// <summary>Eases the scroll view to the bottom so the newest row stays in view.</summary>
    private void FollowNewestRow()
    {
        if (_rowScroll == null)
            return;

        RebuildRowLayout();
        StopScrollRoutine();

        // Owned by this component (never a child), so it cannot strand the reveal if it dies.
        scrollRoutine = StartCoroutine(ScrollToBottomRoutine());
    }

    private IEnumerator ScrollToBottomRoutine()
    {
        _rowScroll.StopMovement();
        float start = _rowScroll.verticalNormalizedPosition;

        float elapsed = 0f;
        while (elapsed < _autoScrollDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _autoScrollDuration);
            float eased = 1f - (1f - t) * (1f - t);
            _rowScroll.verticalNormalizedPosition = Mathf.Lerp(start, 0f, eased);
            yield return null;
        }

        _rowScroll.verticalNormalizedPosition = 0f;
        scrollRoutine = null;
    }

    private void StopScrollRoutine()
    {
        if (scrollRoutine != null)
        {
            StopCoroutine(scrollRoutine);
            scrollRoutine = null;
        }
    }

    private void RebuildRowLayout()
    {
        if (_rowScroll == null)
            return;

        if (_rowScroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rowScroll.content);
        if (_rowScroll.viewport != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rowScroll.viewport);
    }

    /// <summary>1 = top, 0 = bottom.</summary>
    private void SetScrollPosition(float normalized)
    {
        if (_rowScroll == null)
            return;

        _rowScroll.StopMovement();
        _rowScroll.verticalNormalizedPosition = normalized;
    }

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
    }

    private void SetContinueInteractable(bool interactable)
    {
        if (continueButton == null)
            return;

        if (_continueButtonComponent == null)
            _continueButtonComponent = continueButton.GetComponent<Button>()
                                       ?? continueButton.GetComponentInChildren<Button>(true);

        if (_continueButtonComponent != null)
            _continueButtonComponent.interactable = interactable;
    }

    private IEnumerator RevealResidentsMutated(int count)
    {
        if (residentsMutatedRoot == null)
            yield break;

        residentsMutatedRoot.SetActive(true);

        string label = $"Residents Who Fully Mutated: {count}";

        if (residentsMutatedWobble != null && penaltyValueProfile != null)
        {
            residentsMutatedWobble.SetProfile(penaltyValueProfile, true);
            residentsMutatedWobble.StartWobble();
        }

        if (residentsMutatedReveal != null)
            yield return residentsMutatedReveal.RevealTextBounded(label, _maxSingleRevealDuration);
        else if (residentsMutatedText != null)
            residentsMutatedText.text = label;

        yield return WaitUnscaled(lineRevealDelay);
    }

    private IEnumerator RevealCiviliansKilled(int count)
    {
        if (civiliansKilledRoot == null)
            yield break;

        civiliansKilledRoot.SetActive(true);

        string label = $"Civilians Killed: {count}";

        if (civiliansKilledWobble != null && penaltyValueProfile != null)
        {
            civiliansKilledWobble.SetProfile(penaltyValueProfile, true);
            civiliansKilledWobble.StartWobble();
        }

        if (civiliansKilledReveal != null)
            yield return civiliansKilledReveal.RevealTextBounded(label, _maxSingleRevealDuration);
        else if (civiliansKilledText != null)
            civiliansKilledText.text = label;

        yield return WaitUnscaled(lineRevealDelay);
    }

    private IEnumerator RevealCurrentPopulation(int count)
    {
        if (currentPopulationRoot == null)
            yield break;

        currentPopulationRoot.SetActive(true);

        string label = $"Current Population: {count}";

        if (currentPopulationWobble != null && positiveTotalProfile != null)
        {
            currentPopulationWobble.SetProfile(positiveTotalProfile, true);
            currentPopulationWobble.StartWobble();
        }

        if (currentPopulationReveal != null)
            yield return currentPopulationReveal.RevealTextBounded(label, _maxSingleRevealDuration);
        else if (currentPopulationText != null)
            currentPopulationText.text = label;

        yield return WaitUnscaled(lineRevealDelay);
    }

    private IEnumerator RevealNetTotal(int total)
    {
        if (netEarningsRoot != null)
            netEarningsRoot.SetActive(true);

        if (netEarningsText != null)
            netEarningsText.color = total < 0 ? penaltyColor : rewardColor;

        TMPWobbleProfile profileToUse = total < 0 ? negativeTotalProfile : positiveTotalProfile;
        string totalString = $"Net Daily Earnings: {FormatSignedNumber(total)}";

        if (netEarningsWobble != null && profileToUse != null)
        {
            netEarningsWobble.SetProfile(profileToUse, true);
            netEarningsWobble.StartWobble();
        }

        if (netEarningsReveal != null)
        {
            yield return netEarningsReveal.RevealTextBounded(totalString, _maxSingleRevealDuration);
            if (netEarningsText != null)
                netEarningsText.text = totalString;
        }
        else if (netEarningsText != null)
            netEarningsText.text = totalString;
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

    private string FormatSignedNumber(int value)
    {
        if (value > 0) return $"+{value}";
        if (value < 0) return value.ToString();
        return "0";
    }

    // ----- Content fade -----

    private CanvasGroup GetContentCanvasGroup()
    {
        if (_contentCanvasGroup != null || _contentContainer == null)
            return _contentCanvasGroup;

        _contentCanvasGroup = _contentContainer.GetComponent<CanvasGroup>();
        if (_contentCanvasGroup == null)
            _contentCanvasGroup = _contentContainer.AddComponent<CanvasGroup>();

        return _contentCanvasGroup;
    }

    private void SetContentAlpha(float alpha)
    {
        CanvasGroup group = GetContentCanvasGroup();
        if (group != null)
            group.alpha = alpha;
    }

    /// <summary>Runs inside the reveal coroutine; a skip snaps the alpha to 1 via SnapToFinalState.</summary>
    private IEnumerator FadeContent(float duration)
    {
        SetContentAlpha(0f);

        float elapsed = 0f;
        while (elapsed < duration && !_skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetContentAlpha(1f - (1f - t) * (1f - t));
            yield return null;
        }

        SetContentAlpha(1f);
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

    private void PlayRowSound()
    {
        if (_skipRequested || rowSound == null || SFXController.Instance == null)
            return;

        float pitch = Random.Range(rowSoundPitchRange.x, rowSoundPitchRange.y);
        SFXController.Instance.Play(rowSound, rowSoundVolume, pitch);
    }

    public void OnContinueButtonPressed()
    {
        // Guard re-entry: a double-click, or both the button and a skip-input landing on the same
        // frame, must not queue two transitions.
        if (_continuePressed)
            return;

        _continuePressed = true;

        StopAllReportRoutines();
        SetContinueInteractable(false);

        // Only a non-host sees a "waiting for host" label — the host is the one doing the work.
        bool isHost = GlobalHostVariables.Instance == null || GlobalHostVariables.Instance.IsServer;
        if (!isHost && waitingForHostText != null)
            waitingForHostText.SetActive(true);

        // Hide only the content — leave BG active as an overlay while the screen fades.
        _contentContainer?.SetActive(false);

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
    /// deactivating this screen; if that has not happened within <see cref="_continueWatchdogTimeout"/>
    /// seconds, the screen dismisses itself so the player is never trapped behind a dead overlay.
    /// Lives on the report root, so the success case cancels it automatically on deactivation.
    /// </summary>
    private IEnumerator WatchdogAfterContinue()
    {
        float elapsed = 0f;
        while (elapsed < _continueWatchdogTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning(
            $"[EndOfShiftReportUI] Shift transition did not dismiss the report within " +
            $"{_continueWatchdogTimeout:0.#}s of pressing Continue — dismissing locally so the player " +
            "is not trapped on the report screen.");

        UIController.Instance?.ForceDismissEndOfShiftReport();
    }

    private void StopAllReportRoutines()
    {
        StopScrollRoutine();

        if (driverRoutine != null)
        {
            StopCoroutine(driverRoutine);
            driverRoutine = null;
        }

        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }
    }
}
