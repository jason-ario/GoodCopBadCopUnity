using System.Collections.Generic;
using UnityEngine;

public class EndOfShiftReportTester : MonoBehaviour
{
    [SerializeField] private EndOfShiftReportUI reportUI;

    [ContextMenu("Test Report")]
    public void TestReport()
    {
        var rows = new List<EndOfShiftReportUI.ReportRowData>
        {
            new EndOfShiftReportUI.ReportRowData("Processed: 6 Citizens", 60),
            new EndOfShiftReportUI.ReportRowData("Passed: 2", 60),
            new EndOfShiftReportUI.ReportRowData("Quarantined: 1", 60),
            new EndOfShiftReportUI.ReportRowData("Killed: 3", 60),
            new EndOfShiftReportUI.ReportRowData("Infected: 2", 60),
            new EndOfShiftReportUI.ReportRowData("Non-Infected: 1", 30, true),
        };

        reportUI.PlayReport(rows);
    }
}