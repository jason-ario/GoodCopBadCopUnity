using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

    [Header("Layout")]
    [Tooltip("The Container child of BG that holds all report content. Deactivated on Continue so the BG remains as an overlay while the screen fades.")]
    [SerializeField] private GameObject _contentContainer;

    [Header("Timing")]
    [SerializeField] private float initialDelay = 0.35f;
    [SerializeField] private float rewardRevealDelay = 0.18f;
    [SerializeField] private float lineRevealDelay = 0.45f;
    [SerializeField] private float finalDelayBeforeContinue = 0.4f;

    [Header("Colors")]
    [SerializeField] private Color rewardColor = Color.white;
    [SerializeField] private Color penaltyColor = new Color(1f, 0.3f, 0.3f);

    [Header("Wobble Profiles")]
    [SerializeField] private TMPWobbleProfile normalLabelProfile;
    [SerializeField] private TMPWobbleProfile rewardValueProfile;
    [SerializeField] private TMPWobbleProfile penaltyValueProfile;
    [SerializeField] private TMPWobbleProfile positiveTotalProfile;
    [SerializeField] private TMPWobbleProfile negativeTotalProfile;

    private Coroutine revealRoutine;

    [SerializeField] private GameObject banner; 
    [SerializeField] TMPTextReveal subHeaderText;
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
        if (revealRoutine != null)
            StopCoroutine(revealRoutine);
        
        gameObject.SetActive(true);

        revealRoutine = StartCoroutine(RevealReportRoutine(
            reportRows, residentsFullyMutatedOvernight, civiliansKilledOvernight, currentPopulation));
    }

    public void HideAll()
    {
        banner.SetActive(false);
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
    }

    private IEnumerator RevealReportRoutine(
        List<ReportRowData> reportRows,
        int residentsFullyMutatedOvernight,
        int civiliansKilledOvernight,
        int currentPopulation)
    {
        HideAll();

        yield return new WaitForSeconds(initialDelay); 
        banner.SetActive(true);
        yield return new WaitForSeconds(.5f);
        yield return subHeaderText.RevealText("Checkpoint Performance Summary");
        _contentContainer?.SetActive(true);
        
        int count = Mathf.Min(reportRows.Count, rows.Count);
        int total = 0;

        for (int i = 0; i < count; i++)
        {
            ReportRowData data = reportRows[i];
            EndOfShiftReportRow row = rows[i];

            if (row == null)
                continue;

            row.Show(); 
            row.Clear();
            
            yield return row.RevealLabel(data.label, normalLabelProfile);

            yield return new WaitForSeconds(rewardRevealDelay);

            string valueText = FormatValue(data.amount, data.isPenalty);
            Color valueColor = data.isPenalty ? penaltyColor : rewardColor;
            TMPWobbleProfile valueProfile = data.isPenalty ? penaltyValueProfile : rewardValueProfile;

            if (!data.isHeader)
            {
                if (data.amount != 0)
                {
                    yield return row.RevealValue(valueText, valueColor, valueProfile);
                }
            }

            total += data.isPenalty ? -Mathf.Abs(data.amount) : Mathf.Abs(data.amount);

            yield return new WaitForSeconds(lineRevealDelay);
        }

        if (GlobalHostVariables.Instance.IsServer)
            GlobalHostVariables.Instance.AddMoney(total);
        
        yield return RevealNetTotal(total);

        // Reveal residents who fully mutated overnight and went on to kill civilians.
        yield return RevealResidentsMutated(residentsFullyMutatedOvernight);

        // Reveal overnight civilians killed panel (purely informational — no monetary impact).
        yield return RevealCiviliansKilled(civiliansKilledOvernight);

        // Reveal the updated current population, accounting for any overnight civilian deaths.
        yield return RevealCurrentPopulation(currentPopulation);

        yield return new WaitForSeconds(finalDelayBeforeContinue);

        if (continueButton != null)
            continueButton.SetActive(true);

        revealRoutine = null;
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
            yield return residentsMutatedReveal.RevealText(label);
        else if (residentsMutatedText != null)
            residentsMutatedText.text = label;

        yield return new WaitForSeconds(lineRevealDelay);
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
            yield return civiliansKilledReveal.RevealText(label);
        else if (civiliansKilledText != null)
            civiliansKilledText.text = label;

        yield return new WaitForSeconds(lineRevealDelay);
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
            yield return currentPopulationReveal.RevealText(label);
        else if (currentPopulationText != null)
            currentPopulationText.text = label;

        yield return new WaitForSeconds(lineRevealDelay);
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
            yield return netEarningsReveal.RevealText(totalString);
            netEarningsText.text = totalString + " <sprite=0>";
        }
        else if (netEarningsText != null)
            netEarningsText.text = totalString + " <sprite=0>";
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
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }

        // Hide only the content — leave BG active as an overlay while the screen fades.
        _contentContainer?.SetActive(false);
        ShiftManager.Instance.StartInBetweenShiftSequence();
    }
    
}