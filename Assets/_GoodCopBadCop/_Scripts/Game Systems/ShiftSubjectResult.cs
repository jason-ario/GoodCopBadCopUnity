using System;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>How a subject left the checkpoint during a shift.</summary>
public enum ShiftSubjectVerdict : byte
{
    Passed,
    Quarantined,
    Killed,
    Fled
}

/// <summary>
/// One processed subject's outcome for the end-of-shift report. Recorded on the server at the
/// moment the verdict payout is issued (see <see cref="SuspectController"/>), so the report shows
/// exactly what the player was paid rather than re-deriving earnings from a separate table.
/// Serializable for the workday save and <see cref="INetworkSerializable"/> for the report RPC.
/// </summary>
[Serializable]
public struct ShiftSubjectResult : INetworkSerializable
{
    /// <summary>Sentinel for <see cref="AnomaliesCaught"/> when no paperwork was assessed (e.g. the subject fled).</summary>
    public const int NotAssessed = -1;

    public string SubjectName;
    public int AnomaliesCaught;
    public int AnomaliesTotal;
    public int CouponsEarned;
    public ShiftSubjectVerdict Verdict;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        string subjectName = SubjectName ?? string.Empty;
        serializer.SerializeValue(ref subjectName);
        SubjectName = subjectName;

        serializer.SerializeValue(ref AnomaliesCaught);
        serializer.SerializeValue(ref AnomaliesTotal);
        serializer.SerializeValue(ref CouponsEarned);

        byte verdict = (byte)Verdict;
        serializer.SerializeValue(ref verdict);
        Verdict = (ShiftSubjectVerdict)verdict;
    }
}

/// <summary>
/// Client-side payload for <see cref="EndOfShiftReportUI"/>. Purely informational: every coupon
/// listed here was already paid out at verdict time, so the report never awards money itself.
/// </summary>
public class ShiftReportData
{
    public int Day;
    public List<ShiftSubjectResult> Subjects = new List<ShiftSubjectResult>();
    public int PopulationAlive = -1;
    public int CiviliansKilledOvernight;
    public int ResidentsMutatedOvernight;

    public int PassedCount { get; private set; }
    public int QuarantinedCount { get; private set; }
    public int KilledCount { get; private set; }
    public int FledCount { get; private set; }
    public int TotalCouponsEarned { get; private set; }

    public ShiftReportData(int day, IEnumerable<ShiftSubjectResult> subjects,
        int populationAlive, int civiliansKilledOvernight, int residentsMutatedOvernight)
    {
        Day = day;
        PopulationAlive = populationAlive;
        CiviliansKilledOvernight = civiliansKilledOvernight;
        ResidentsMutatedOvernight = residentsMutatedOvernight;

        if (subjects != null)
            Subjects.AddRange(subjects);

        // Tallies are derived from the same list as the itemized rows so the two can never disagree.
        foreach (ShiftSubjectResult subject in Subjects)
        {
            TotalCouponsEarned += subject.CouponsEarned;
            switch (subject.Verdict)
            {
                case ShiftSubjectVerdict.Passed: PassedCount++; break;
                case ShiftSubjectVerdict.Quarantined: QuarantinedCount++; break;
                case ShiftSubjectVerdict.Killed: KilledCount++; break;
                case ShiftSubjectVerdict.Fled: FledCount++; break;
            }
        }
    }
}
