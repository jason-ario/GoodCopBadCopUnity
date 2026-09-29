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
    /// <summary>Coupons actually dispensed by the ATM for this verdict (after the integrity multiplier).</summary>
    public int CouponsEarned;
    public ShiftSubjectVerdict Verdict;

    /// <summary><see cref="SuspectData"/> asset name, used by clients to resolve the ID photo locally.</summary>
    public string SuspectAssetName;
    public string IDNumber;
    /// <summary>True when the subject was an uncanny replacement, so the replacement ID photo is shown.</summary>
    public bool UsedReplacementPhoto;

    /// <summary>False for results restored from saves written before integrity was recorded.</summary>
    public bool HasIntegrity;
    /// <summary>Checkpoint integrity multiplier (0–1, 1 = 100%) at the moment this subject was processed.</summary>
    public float IntegrityAtProcessing;

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

        string assetName = SuspectAssetName ?? string.Empty;
        serializer.SerializeValue(ref assetName);
        SuspectAssetName = assetName;

        string idNumber = IDNumber ?? string.Empty;
        serializer.SerializeValue(ref idNumber);
        IDNumber = idNumber;

        serializer.SerializeValue(ref UsedReplacementPhoto);
        serializer.SerializeValue(ref HasIntegrity);
        serializer.SerializeValue(ref IntegrityAtProcessing);
    }

    /// <summary>
    /// Resolves this subject's ID photo on the local peer from the run's <see cref="SuspectSet"/>.
    /// Returns null if the suspect can't be found (e.g. a debug/test entry).
    /// </summary>
    public UnityEngine.Texture2D ResolveIDPhoto()
    {
        if (string.IsNullOrEmpty(SuspectAssetName))
            return null;

        SuspectSet set = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.allSuspects : null;
        if (set == null || set.suspects == null)
            return null;

        foreach (SuspectData data in set.suspects)
        {
            if (data == null || data.name != SuspectAssetName)
                continue;

            if (UsedReplacementPhoto && data.replacementIDPhoto != null)
                return data.replacementIDPhoto;

            return data.IDPhoto;
        }

        return null;
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

    /// <summary>Mean checkpoint integrity (0–1) across subjects that recorded it; -1 when none did.</summary>
    public float AverageIntegrity { get; private set; } = -1f;

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
        float integritySum = 0f;
        int integrityCount = 0;
        foreach (ShiftSubjectResult subject in Subjects)
        {
            TotalCouponsEarned += subject.CouponsEarned;
            if (subject.HasIntegrity)
            {
                integritySum += subject.IntegrityAtProcessing;
                integrityCount++;
            }

            switch (subject.Verdict)
            {
                case ShiftSubjectVerdict.Passed: PassedCount++; break;
                case ShiftSubjectVerdict.Quarantined: QuarantinedCount++; break;
                case ShiftSubjectVerdict.Killed: KilledCount++; break;
                case ShiftSubjectVerdict.Fled: FledCount++; break;
            }
        }

        if (integrityCount > 0)
            AverageIntegrity = integritySum / integrityCount;
    }
}
