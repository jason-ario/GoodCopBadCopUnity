using System.Collections.Generic;
using UnityEngine;

public class EndOfShiftReportTester : MonoBehaviour
{
    [SerializeField] private EndOfShiftReportUI reportUI;
    [Tooltip("Number of fake subjects to generate. Use more than fit on screen to exercise scrolling.")]
    [SerializeField] private int subjectCount = 14;

    private static readonly string[] FirstNames = { "Olga", "Dmitri", "Boris", "Anya", "Yuri", "Irina", "Pavel", "Katya", "Sergei", "Nadia" };
    private static readonly string[] LastNames = { "Petrova", "Volkov", "Ivanov", "Sokolova", "Morozov", "Orlova", "Popov", "Lebedeva", "Kozlov", "Novikova" };

    [ContextMenu("Test Report")]
    public void TestReport()
    {
        var subjects = new List<ShiftSubjectResult>();
        for (int i = 0; i < subjectCount; i++)
        {
            // Mostly Passed/Quarantined/Killed, with an occasional Fled.
            ShiftSubjectVerdict verdict = i % 7 == 6 ? ShiftSubjectVerdict.Fled : (ShiftSubjectVerdict)(i % 3);
            bool fled = verdict == ShiftSubjectVerdict.Fled;
            int total = Random.Range(0, 5);

            subjects.Add(new ShiftSubjectResult
            {
                SubjectName = $"{FirstNames[i % FirstNames.Length]} {LastNames[(i * 3) % LastNames.Length]}",
                AnomaliesCaught = fled ? ShiftSubjectResult.NotAssessed : Random.Range(0, total + 1),
                AnomaliesTotal = total,
                CouponsEarned = fled ? 0 : Random.Range(3, 16),
                Verdict = verdict
            });
        }

        reportUI.PlayReport(new ShiftReportData(3, subjects, 96, 2, 1));
    }
}
