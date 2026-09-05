using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The end-of-shift report screen. Plays an animated reveal of the day's results and then offers a
/// Continue button that advances the campaign to the next day.
///
/// This screen is a full-screen modal that disables player control, so it is the single most
/// dangerous place in the game to get stuck: if the Continue affordance never appears, or appears
/// but does nothing, the player's only recourse is to quit. Everything below is therefore built so
/// that <b>the player can always leave</b>:
///
/// 1. The affordance is shown by <see cref="DriveReportRoutine"/>, which is separate from and
///    watches over the reveal. A reveal that stalls or dies cannot suppress the button.
/// 2. Every reveal wait is bounded (see <see cref="TMPTextReveal.RevealTextBounded"/>) and the whole
///    sequence is capped by <see cref="_maxTotalRevealDuration"/>.
/// 3. Any input skips the remaining animation.
/// 4. Both players get a working Continue button — not just the host.
/// 5. Pressing Continue starts <see cref="WatchdogAfterContinue"/>: if the transition has not torn
///    this screen down in time, the screen dismisses itself and restores control.
/// 6. Payout is applied before the animation, so aborting or skipping it never costs the player money.
/// </summary>
public class EndOfShiftReportUI : MonoBehaviour
{
    [System.Serializable]
    public class ReportRowData
    {
        public string label;
        public int amount;
        public bool isPenalty;
        public bool isHeader;
        
        public ReportRowData(string label, int amount, bool isPenalty = false, bool isHeader = false)
        {
            this.label = label;
            this.amount = amount;
            this.isPenalty = isPenalty;
            this.isHeader = isHeader;
        }
    }

    [Header("Rows")]
    [SerializeField] private List<EndOfShiftReportRow> rows = new List<EndOfShiftReportRow>();

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
    [SerializeField] private float rewardRevealDelay = 0.18f;
    [SerializeField] private float lineRevealDelay = 0.45f;
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

    private Coroutine driverRoutine;
    private Coroutine revealRoutine;

    // Cached payload for the current report, so the failsafe path can snap straight to the
    // final state without re-deriving anything.
    private List<ReportRowData> _reportRows;
    private int _residentsMutated;
    private int _civiliansKilled;
    private int _currentPopulation;
    private int _netTotal;

    private bool _revealComplete;
    private bool _skipRequested;
    private bool _affordanceShown;
    private bool _continuePressed;
    private Button _continueButtonComponent;

    private void Awake()
    {
        HideAll();
    }

    public void PlayReport(
        List<ReportRowData> reportRows,
        int residentsFullyMutatedOvernight = 0,
        int civiliansKilledOvernight = 0,
        int currentPopulation = 0)
    {
        StopAllReportRoutines();

        _reportRows = reportRows ?? new List<ReportRowData>();
        _residentsMutated = residentsFullyMutatedOvernight;
        _civiliansKilled = civiliansKilledOvernight;
        _currentPopulation = currentPopulation;
        _netTotal = ComputeNetTotal(_reportRows);

        _revealComplete = false;
        _skipRequested = false;
        _affordanceShown = false;
        _continuePressed = false;

        gameObject.SetActive(true);

        // Award the day's pay up front rather than partway through the reveal. The payout must not
        // depend on an animation running to completion — a skipped, stalled, or interrupted reveal
        // would otherwise silently rob the player of the entire day's earnings.
        if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.IsServer)
            GlobalHostVariables.Instance.AddMoney(_netTotal);

        driverRoutine = StartCoroutine(DriveReportRoutine());
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
    /// Owns the reveal and, unconditionally, the appearance of the Continue affordance.
    ///
    /// The affordance used to be the last statement of the reveal coroutine itself, which meant any
    /// stall anywhere in that long chain of nested animation waits left the player on a modal
    /// screen with no button. Here the reveal is a supervised child: whether it completes, is
    /// skipped, or hangs past <see cref="_maxTotalRevealDuration"/>, control returns to this method
    /// and the button appears.
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

        yield return WaitUnscaled(0.5f);

        // The sub-header lives inside the content container, so the container must be active
        // before its text can animate — StartCoroutine is a no-op on an inactive GameObject.
        _contentContainer?.SetActive(true);

        if (subHeaderText != null)
            yield return subHeaderText.RevealTextBounded("Checkpoint Performance Summary", _maxSingleRevealDuration);

        int count = Mathf.Min(_reportRows.Count, rows.Count);

        for (int i = 0; i < count; i++)
        {
            if (_skipRequested)
                break;

            ReportRowData data = _reportRows[i];
            EndOfShiftReportRow row = rows[i];

            if (row == null)
                continue;

            row.Show(); 
            row.Clear();
            
            yield return row.RevealLabel(data.label, normalLabelProfile, _maxSingleRevealDuration);

            yield return WaitUnscaled(rewardRevealDelay);

            if (!data.isHeader && data.amount != 0)
            {
                yield return row.RevealValue(
                    FormatValue(data.amount, data.isPenalty),
                    data.isPenalty ? penaltyColor : rewardColor,
                    data.isPenalty ? penaltyValueProfile : rewardValueProfile,
                    _maxSingleRevealDuration);
            }

            yield return WaitUnscaled(lineRevealDelay);
        }

        yield return RevealNetTotal(_netTotal);

        // Reveal residents who fully mutated overnight and went on to kill civilians.
        yield return RevealResidentsMutated(_residentsMutated);

        // Reveal overnight civilians killed panel (purely informational — no monetary impact).
        yield return RevealCiviliansKilled(_civiliansKilled);

        // Reveal the updated current population, accounting for any overnight civilian deaths.
        yield return RevealCurrentPopulation(_currentPopulation);

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
            subHeaderText.SetTextInstant("Checkpoint Performance Summary");

        int count = Mathf.Min(_reportRows.Count, rows.Count);
        for (int i = 0; i < count; i++)
        {
            ReportRowData data = _reportRows[i];
            if (rows[i] == null)
                continue;

            rows[i].SetInstant(
                data.label,
                FormatValue(data.amount, data.isPenalty),
                data.isPenalty ? penaltyColor : rewardColor,
                showValue: !data.isHeader && data.amount != 0);
        }

        SnapPanel(netEarningsRoot, netEarningsReveal, netEarningsText,
            $"Net Daily Earnings: {FormatSignedNumber(_netTotal)}");
        if (netEarningsText != null)
            netEarningsText.color = _netTotal < 0 ? penaltyColor : rewardColor;

        SnapPanel(residentsMutatedRoot, residentsMutatedReveal, residentsMutatedText,
            $"Residents Who Fully Mutated: {_residentsMutated}");
        SnapPanel(civiliansKilledRoot, civiliansKilledReveal, civiliansKilledText,
            $"Civilians Killed: {_civiliansKilled}");
        SnapPanel(currentPopulationRoot, currentPopulationReveal, currentPopulationText,
            $"Current Population: {_currentPopulation}");
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
    /// Reveals the Continue button to <b>every</b> player.
    ///
    /// Previously the button was host-only and clients got a passive "waiting for host" label. That
    /// is a dead end by construction: a client whose transition never arrives — because the host's
    /// coroutine stalled, or the RPC landed while this client was mid-reveal — has no affordance at
    /// all and cannot leave the screen. Letting either player continue is safe because
    /// <see cref="ShiftManager.StartInBetweenShiftSequence"/> is latched server-side, so duplicate
    /// or simultaneous presses collapse into a single transition.
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

    private static int ComputeNetTotal(List<ReportRowData> reportRows)
    {
        int total = 0;
        for (int i = 0; i < reportRows.Count; i++)
        {
            ReportRowData data = reportRows[i];
            total += data.isPenalty ? -Mathf.Abs(data.amount) : Mathf.Abs(data.amount);
        }
        return total;
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

    private string FormatValue(int amount, bool isPenalty)
    {
        int absAmount = Mathf.Abs(amount);
        if (amount == 0)
        {
            return "0";
        }
        return isPenalty ? $"Penalty {absAmount}" : $"Rewards {absAmount}";
    }

    private string FormatSignedNumber(int value)
    {
        if (value > 0) return $"+{value}";
        if (value < 0) return value.ToString();
        return "0";
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
    /// seconds, the press effectively did nothing and the player is stuck behind a modal overlay with
    /// no remaining input. That is the exact "buttons do nothing" trap this guards against — most
    /// often hit when the other player already advanced the shift, so the server had already consumed
    /// the transition and this peer's own request was dropped as a duplicate.
    ///
    /// Note this coroutine lives on the report root, so the success case cancels it automatically:
    /// deactivating the screen destroys the coroutine before it can ever fire.
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
