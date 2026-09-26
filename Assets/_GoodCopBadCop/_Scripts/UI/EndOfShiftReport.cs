using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The end-of-shift report: an itemized, informational shift summary. Lists every processed
/// subject (anomalies caught, verdict, coupons issued), tallies the verdicts, and totals the day's
/// earnings. It never awards money — every coupon shown was already paid out at verdict time by
/// <see cref="SuspectController"/>.
///
/// This screen is a full-screen modal that disables player control, so it is the single most
/// dangerous place in the game to get stuck. Everything below is built so that
/// <b>the player can always leave</b>:
///
/// 1. The Continue affordance is shown by <see cref="DriveReportRoutine"/>, which supervises the
///    reveal. A reveal that stalls or dies cannot suppress the button.
/// 2. Every reveal wait is bounded and the whole sequence is capped by <see cref="_maxTotalRevealDuration"/>.
/// 3. Any input skips the remaining animation.
/// 4. Both players get a working Continue button — not just the host.
/// 5. Pressing Continue starts <see cref="WatchdogAfterContinue"/>: if the transition has not torn
///    this screen down in time, the screen dismisses itself and restores control.
/// </summary>
public class EndOfShiftReportUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private GameObject banner;
    [SerializeField] private TMPTextReveal subHeaderText;

    [Header("Subjects")]
    [Tooltip("Parent the itemized subject rows are cloned into.")]
    [SerializeField] private RectTransform _subjectListRoot;
    [Tooltip("Inactive row used as the clone source for every subject.")]
    [SerializeField] private EndOfShiftReportRow _subjectRowTemplate;
    [Tooltip("Shown instead of rows when no subjects were processed.")]
    [SerializeField] private GameObject _emptyListLabel;
    [Tooltip("Scroll view wrapping the subject list. Auto-scrolls to each new row as it is revealed.")]
    [SerializeField] private ScrollRect _subjectScroll;
    [Tooltip("Seconds to ease the scroll view down to a newly revealed row.")]
    [SerializeField] private float _autoScrollDuration = 0.25f;
    [Tooltip("Maximum rows before the remainder collapses into a single '+N more' line. 0 = unlimited (list scrolls).")]
    [SerializeField] private int _maxListedSubjects = 0;

    [Header("Verdict Tallies")]
    [SerializeField] private CanvasGroup _talliesGroup;
    [SerializeField] private TextMeshProUGUI _passedCountText;
    [SerializeField] private TextMeshProUGUI _quarantinedCountText;
    [SerializeField] private TextMeshProUGUI _killedCountText;
    [SerializeField] private TextMeshProUGUI _fledCountText;
    [Tooltip("Fled tally cell — hidden on days nobody fled.")]
    [SerializeField] private GameObject _fledCell;

    [Header("Total Earnings")]
    [SerializeField] private CanvasGroup _totalGroup;
    [SerializeField] private TextMeshProUGUI _totalEarnedText;
    [SerializeField] private TMPTextReveal _totalEarnedReveal;
    [SerializeField] private TMPWobbleText _totalEarnedWobble;
    [SerializeField] private TMPWobbleProfile _totalWobbleProfile;

    [Header("Population Footer")]
    [SerializeField] private CanvasGroup _footerGroup;
    [SerializeField] private TextMeshProUGUI _populationFooterText;

    [Header("Continue")]
    [SerializeField] private GameObject continueButton;
    [Tooltip("Optional 'waiting' label shown to a non-host player after they press Continue, while the host's transition runs.")]
    [SerializeField] private GameObject waitingForHostText;

    [Header("Layout")]
    [Tooltip("The Container child of BG that holds all report content. Deactivated on Continue so the BG remains as an overlay while the screen fades.")]
    [SerializeField] private GameObject _contentContainer;

    [Header("Timing")]
    [SerializeField] private float initialDelay = 0.35f;
    [SerializeField] private float rowFadeDuration = 0.18f;
    [SerializeField] private float rowRevealDelay = 0.12f;
    [SerializeField] private float sectionFadeDuration = 0.3f;
    [SerializeField] private float sectionRevealDelay = 0.3f;
    [SerializeField] private float finalDelayBeforeContinue = 0.4f;

    [Header("Failsafes")]
    [Tooltip("Hard cap on any single text reveal. Past this the line snaps to its final state and the report moves on.")]
    [SerializeField] private float _maxSingleRevealDuration = 6f;
    [Tooltip("Hard cap on the entire reveal sequence. Past this the report snaps to its final state and shows Continue immediately.")]
    [SerializeField] private float _maxTotalRevealDuration = 45f;
    [Tooltip("After pressing Continue, how long to wait for the shift transition to tear this screen down before dismissing it locally so the player is never trapped.")]
    [SerializeField] private float _continueWatchdogTimeout = 15f;
    [Tooltip("When true, any key / click / gamepad press skips the rest of the reveal animation.")]
    [SerializeField] private bool _allowSkipInput = true;

    [Header("Verdict Colors")]
    [SerializeField] private Color passedColor = new Color(0.22f, 0.42f, 0.16f);
    [SerializeField] private Color quarantinedColor = new Color(0.78f, 0.42f, 0.08f);
    [SerializeField] private Color killedColor = new Color(0.66f, 0.13f, 0.1f);
    [SerializeField] private Color fledColor = new Color(0.45f, 0.4f, 0.35f);
    [SerializeField] private Color mutedColor = new Color(0.36f, 0.22f, 0.04f, 0.45f);

    private readonly List<EndOfShiftReportRow> _spawnedRows = new List<EndOfShiftReportRow>();

    private Coroutine driverRoutine;
    private Coroutine revealRoutine;

    private ShiftReportData _data;

    private bool _revealComplete;
    private bool _skipRequested;
    private bool _affordanceShown;
    private bool _continuePressed;
    private Button _continueButtonComponent;

    private void Awake()
    {
        if (_subjectRowTemplate != null)
            _subjectRowTemplate.gameObject.SetActive(false);

        HideAll();
    }

    public void PlayReport(ShiftReportData data)
    {
        StopAllReportRoutines();

        _data = data ?? new ShiftReportData(0, null, -1, 0, 0);

        _revealComplete = false;
        _skipRequested = false;
        _affordanceShown = false;
        _continuePressed = false;

        gameObject.SetActive(true);

        // Deliberately no payout here: coupons are issued per subject at verdict time.
        driverRoutine = StartCoroutine(DriveReportRoutine());
    }

    public void HideAll()
    {
        if (banner != null)
            banner.SetActive(false);
        if (subHeaderText != null)
            subHeaderText.SetTextInstant(" ");
        _contentContainer?.SetActive(false);

        ClearRows();

        if (_emptyListLabel != null)
            _emptyListLabel.SetActive(false);

        SetGroupAlpha(_talliesGroup, 0f);
        SetGroupAlpha(_totalGroup, 0f);
        SetGroupAlpha(_footerGroup, 0f);

        if (_totalEarnedReveal != null)
            _totalEarnedReveal.Clear();
        else if (_totalEarnedText != null)
            _totalEarnedText.text = "";

        if (_totalEarnedWobble != null)
            _totalEarnedWobble.StopWobble();

        if (continueButton != null)
            continueButton.SetActive(false);

        if (waitingForHostText != null)
            waitingForHostText.SetActive(false);
    }

    /// <summary>
    /// Owns the reveal and, unconditionally, the appearance of the Continue affordance. Whether the
    /// reveal completes, is skipped, or hangs past <see cref="_maxTotalRevealDuration"/>, control
    /// returns here and the button appears.
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

        yield return WaitUnscaled(0.5f);

        // The sub-header lives inside the content container, so the container must be active
        // before its text can animate — StartCoroutine is a no-op on an inactive GameObject.
        _contentContainer?.SetActive(true);

        if (subHeaderText != null)
            yield return subHeaderText.RevealTextBounded(SubHeaderLabel(), _maxSingleRevealDuration);

        // Build every row up front (invisible) so the layout is final before anything animates.
        PopulateStaticContent();

        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_skipRequested)
                break;

            yield return RevealRow(_spawnedRows[i]);
            yield return WaitUnscaled(rowRevealDelay);
        }

        yield return WaitUnscaled(sectionRevealDelay);
        yield return FadeGroup(_talliesGroup, sectionFadeDuration);

        yield return WaitUnscaled(sectionRevealDelay);
        yield return RevealTotal();

        yield return WaitUnscaled(sectionRevealDelay);
        yield return FadeGroup(_footerGroup, sectionFadeDuration);

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

        _contentContainer?.SetActive(true);

        if (subHeaderText != null)
            subHeaderText.SetTextInstant(SubHeaderLabel());

        if (_spawnedRows.Count == 0)
            PopulateStaticContent();

        foreach (EndOfShiftReportRow row in _spawnedRows)
        {
            row.gameObject.SetActive(true);
            row.SetAlpha(1f);
        }

        RebuildSubjectLayout();
        SetScrollPosition(0f);

        SetGroupAlpha(_talliesGroup, 1f);
        SetGroupAlpha(_totalGroup, 1f);
        SetGroupAlpha(_footerGroup, 1f);

        if (_totalEarnedReveal != null)
            _totalEarnedReveal.SetTextInstant(TotalLabel());
        else if (_totalEarnedText != null)
            _totalEarnedText.text = TotalLabel();
    }

    /// <summary>
    /// Writes every non-animated value (rows, tallies, footer) and leaves each section transparent
    /// so the reveal only has to fade them in. Idempotent: rebuilds rows from scratch each call.
    /// </summary>
    private void PopulateStaticContent()
    {
        BuildRows();

        SetCount(_passedCountText, _data.PassedCount);
        SetCount(_quarantinedCountText, _data.QuarantinedCount);
        SetCount(_killedCountText, _data.KilledCount);
        SetCount(_fledCountText, _data.FledCount);
        if (_fledCell != null)
            _fledCell.SetActive(_data.FledCount > 0);

        if (_populationFooterText != null)
            _populationFooterText.text = FooterLabel();

        RebuildSubjectLayout();
        SetScrollPosition(1f);
    }

    /// <summary>
    /// Switches a row on, then fades it in while easing the scroll view down so the newest row is
    /// always in view. Caller-driven on unscaled time, like every other reveal wait.
    /// </summary>
    private IEnumerator RevealRow(EndOfShiftReportRow row)
    {
        row.gameObject.SetActive(true);
        row.SetAlpha(0f);
        RebuildSubjectLayout();

        if (_subjectScroll != null)
            _subjectScroll.StopMovement();

        float startScroll = _subjectScroll != null ? _subjectScroll.verticalNormalizedPosition : 0f;
        float duration = Mathf.Max(rowFadeDuration, _autoScrollDuration);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            row.SetAlpha(rowFadeDuration > 0f ? Mathf.Clamp01(elapsed / rowFadeDuration) : 1f);

            if (_subjectScroll != null)
            {
                float t = _autoScrollDuration > 0f ? Mathf.Clamp01(elapsed / _autoScrollDuration) : 1f;
                float eased = 1f - (1f - t) * (1f - t);
                _subjectScroll.verticalNormalizedPosition = Mathf.Lerp(startScroll, 0f, eased);
            }

            yield return null;
        }

        row.SetAlpha(1f);
        SetScrollPosition(0f);
    }

    private void RebuildSubjectLayout()
    {
        if (_subjectListRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_subjectListRoot);

        if (_subjectScroll != null && _subjectScroll.viewport != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_subjectScroll.viewport);
    }

    /// <summary>1 = top, 0 = bottom.</summary>
    private void SetScrollPosition(float normalized)
    {
        if (_subjectScroll == null)
            return;

        _subjectScroll.StopMovement();
        _subjectScroll.verticalNormalizedPosition = normalized;
    }

    private void BuildRows()
    {
        ClearRows();

        List<ShiftSubjectResult> subjects = _data.Subjects;
        bool hasSubjects = subjects.Count > 0;

        if (_emptyListLabel != null)
            _emptyListLabel.SetActive(!hasSubjects);

        if (!hasSubjects || _subjectRowTemplate == null || _subjectListRoot == null)
            return;

        int maxRows = _maxListedSubjects > 0 ? _maxListedSubjects : int.MaxValue;
        bool overflow = subjects.Count > maxRows;
        int listed = overflow ? maxRows - 1 : subjects.Count;

        for (int i = 0; i < listed; i++)
        {
            ShiftSubjectResult subject = subjects[i];
            EndOfShiftReportRow row = SpawnRow();
            row.SetSubject(subject, VerdictLabel(subject.Verdict), VerdictColor(subject.Verdict), mutedColor);
        }

        if (overflow)
        {
            int hiddenCoupons = 0;
            for (int i = listed; i < subjects.Count; i++)
                hiddenCoupons += subjects[i].CouponsEarned;

            SpawnRow().SetOverflow(subjects.Count - listed, hiddenCoupons, mutedColor);
        }
    }

    private EndOfShiftReportRow SpawnRow()
    {
        EndOfShiftReportRow row = Instantiate(_subjectRowTemplate, _subjectListRoot);
        row.name = $"Subject Row {_spawnedRows.Count + 1}";
        // Rows stay inactive until revealed, so the scroll content only grows as rows appear.
        row.gameObject.SetActive(false);
        row.SetAlpha(0f);
        _spawnedRows.Add(row);
        return row;
    }

    private void ClearRows()
    {
        foreach (EndOfShiftReportRow row in _spawnedRows)
        {
            if (row == null)
                continue;

            // Deactivate first: Destroy is deferred, and a still-active row would otherwise be
            // counted by the layout rebuild that immediately follows.
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        _spawnedRows.Clear();
    }

    private IEnumerator RevealTotal()
    {
        if (_totalGroup != null)
            SetGroupAlpha(_totalGroup, 1f);

        if (_totalEarnedWobble != null && _totalWobbleProfile != null)
        {
            _totalEarnedWobble.SetProfile(_totalWobbleProfile, true);
            _totalEarnedWobble.StartWobble();
        }

        if (_totalEarnedReveal != null)
            yield return _totalEarnedReveal.RevealTextBounded(TotalLabel(), _maxSingleRevealDuration);
        else if (_totalEarnedText != null)
            _totalEarnedText.text = TotalLabel();
    }

    private string SubHeaderLabel() =>
        _data != null && _data.Day > 0 ? $"Day {_data.Day}  -  Shift Summary" : "Shift Summary";

    private string TotalLabel() => _data.TotalCouponsEarned.ToString();

    private string FooterLabel()
    {
        var parts = new List<string>(3);
        if (_data.PopulationAlive >= 0)
            parts.Add($"City Population: {_data.PopulationAlive}");
        parts.Add($"Civilians Lost Overnight: {Mathf.Max(0, _data.CiviliansKilledOvernight)}");
        parts.Add($"Residents Fully Mutated: {Mathf.Max(0, _data.ResidentsMutatedOvernight)}");
        return string.Join("     |     ", parts);
    }

    private static string VerdictLabel(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Passed: return "PASSED";
            case ShiftSubjectVerdict.Quarantined: return "QUARANTINED";
            case ShiftSubjectVerdict.Killed: return "KILLED";
            default: return "FLED";
        }
    }

    private Color VerdictColor(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Passed: return passedColor;
            case ShiftSubjectVerdict.Quarantined: return quarantinedColor;
            case ShiftSubjectVerdict.Killed: return killedColor;
            default: return fledColor;
        }
    }

    private static void SetCount(TextMeshProUGUI text, int count)
    {
        if (text != null)
            text.text = count.ToString();
    }

    private static void SetGroupAlpha(CanvasGroup group, float alpha)
    {
        if (group != null)
            group.alpha = alpha;
    }

    /// <summary>Unscaled, caller-driven fade; snaps if the group is missing or inactive.</summary>
    private static IEnumerator FadeGroup(CanvasGroup group, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f || !group.gameObject.activeInHierarchy)
        {
            group.alpha = 1f;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        group.alpha = 1f;
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
    /// The final safety net: if the shift transition has not deactivated this screen within
    /// <see cref="_continueWatchdogTimeout"/> seconds of pressing Continue, dismiss it locally.
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
