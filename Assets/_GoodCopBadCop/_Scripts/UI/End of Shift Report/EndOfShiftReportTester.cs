using System.Collections.Generic;
using UnityEngine;

public class EndOfShiftReportTester : MonoBehaviour
{
    [SerializeField] private EndOfShiftReportUI reportUI;
    [Tooltip("Number of fake subjects to generate. Use more than fit on screen to exercise scrolling.")]
    [SerializeField] private int subjectCount = 8;

    private static readonly string[] FirstNames = { "Olga", "Dmitri", "Boris", "Anya", "Yuri", "Irina", "Pavel", "Katya", "Sergei", "Nadia" };
    private static readonly string[] LastNames = { "Petrova", "Volkov", "Ivanov", "Sokolova", "Morozov", "Orlova", "Popov", "Lebedeva", "Kozlov", "Novikova" };

    [ContextMenu("Test Report")]
    public void TestReport()
    {
        // Real suspects (when the run records exist) so ID photos resolve; otherwise placeholders.
        List<SuspectData> roster = new List<SuspectData>();
        SuspectSet set = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.allSuspects : null;
        if (set != null && set.suspects != null)
        {
            foreach (SuspectData data in set.suspects)
            {
                if (data != null)
                    roster.Add(data);
            }
        }

        var subjects = new List<ShiftSubjectResult>();
        for (int i = 0; i < subjectCount; i++)
        {
            // Mostly Passed/Quarantined/Killed, with an occasional Fled.
            ShiftSubjectVerdict verdict = i % 7 == 6 ? ShiftSubjectVerdict.Fled : (ShiftSubjectVerdict)(i % 3);
            bool notAssessed = verdict == ShiftSubjectVerdict.Fled;
            int total = Random.Range(1, 5);

            SuspectData data = roster.Count > 0 ? roster[i % roster.Count] : null;
            string name = data != null
                ? $"{data.FirstName} {data.LastName}".Trim()
                : $"{FirstNames[i % FirstNames.Length]} {LastNames[(i * 3) % LastNames.Length]}";

            subjects.Add(new ShiftSubjectResult
            {
                SubjectName = name,
                AnomaliesCaught = notAssessed ? ShiftSubjectResult.NotAssessed : Random.Range(0, total + 1),
                AnomaliesTotal = total,
                CouponsEarned = notAssessed ? 0 : Random.Range(3, 16),
                Verdict = verdict,
                SuspectAssetName = data != null ? data.name : string.Empty,
                IDNumber = data != null && !string.IsNullOrEmpty(data.IDNumber) ? data.IDNumber : Random.Range(1000000, 9999999).ToString(),
                HasIntegrity = true,
                IntegrityAtProcessing = Random.Range(0.6f, 1f)
            });
        }

        reportUI.PlayReport(new ShiftReportData(3, subjects, 1284, 2, 1));
    }
}
