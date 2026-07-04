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

    [Header("Net Earnings")]
    [SerializeField] private GameObject netEarningsRoot;
    [SerializeField] private TextMeshProUGUI netEarningsText;
    [SerializeField] private TMPTextReveal netEarningsReveal;
    [SerializeField] private TMPWobbleText netEarningsWobble;

    [Header("Continue")]
    [SerializeField] private GameObject continueButton;

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

    public void PlayReport(List<ReportRowData> reportRows)
    {
        if (revealRoutine != null)
            StopCoroutine(revealRoutine);
        
        gameObject.SetActive(true);

        revealRoutine = StartCoroutine(RevealReportRoutine(reportRows));
    }

    public void HideAll()
    {
        banner.SetActive(false);
        subHeaderText.SetTextInstant(" ");

        if (rows != null)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    rows[i].Hide();
            }
        }
        
        Canvas.ForceUpdateCanvases();

        if (netEarningsRoot != null)
            netEarningsRoot.SetActive(false);

        if (netEarningsReveal != null)
            netEarningsReveal.Clear();
        else if (netEarningsText != null)
            netEarningsText.text = "";

        if (netEarningsWobble != null)
            netEarningsWobble.StopWobble();

        if (continueButton != null)
            continueButton.SetActive(false);
    }

    private IEnumerator RevealReportRoutine(List<ReportRowData> reportRows)
    {
        HideAll();

        yield return new WaitForSeconds(initialDelay); 
        banner.SetActive(true);
        yield return new WaitForSeconds(.5f);
        yield return subHeaderText.RevealText("Checkpoint Performance Summary");
        
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

        yield return new WaitForSeconds(finalDelayBeforeContinue);

        if (continueButton != null)
            continueButton.SetActive(true);

        revealRoutine = null;
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
            yield return netEarningsReveal.RevealText(totalString);
        else if (netEarningsText != null)
            netEarningsText.text = totalString;
    }

    private string FormatValue(int amount, bool isPenalty)
    {
        int absAmount = Mathf.Abs(amount);
        if (amount == 0)
        {
            return "0";
        }
        return isPenalty ? $"Penalty -{absAmount}" : $"Reward +{absAmount}";
    }

    private string FormatSignedNumber(int value)
    {
        if (value > 0) return $"+{value}";
        if (value < 0) return value.ToString();
        return "0";
    }

    public void OnContinueButtonPressed()
    {
        UIController.Instance.HideEndOfShiftReport();
        ShiftManager.Instance.StartInBetweenShiftSequence();
    }
    
}