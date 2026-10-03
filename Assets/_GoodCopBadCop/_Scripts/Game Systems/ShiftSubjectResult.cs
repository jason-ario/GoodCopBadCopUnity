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

/// <summary>The five exam-page checklist categories, as flags so a subject's ticked boxes fit in one byte.</summary>
[Flags]
public enum ShiftAnomalyCategory : byte
{
    None = 0,
    Documentation = 1 << 0,
    Vitals = 1 << 1,
    Behavior = 1 << 2,
    Physical = 1 << 3,
    Supernatural = 1 << 4
}

/// <summary>One anomaly that was active on a subject, for the shift report detail popup.</summary>
[Serializable]
public struct ShiftAnomalyResult : INetworkSerializable
{
    public string Name;
    public ShiftAnomalyCategory Category;
    /// <summary>True when the player ticked this anomaly's checklist category.</summary>
    public bool Caught;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        string name = Name ?? string.Empty;
        serializer.SerializeValue(ref name);
        Name = name;

        byte category = (byte)Category;
        serializer.SerializeValue(ref category);
        Category = (ShiftAnomalyCategory)category;

        serializer.SerializeValue(ref Caught);
    }
}

/// <summary>Maps anomaly types and checklist type names to <see cref="ShiftAnomalyCategory"/> and labels.</summary>
public static class ShiftAnomalyCategories
{
    public static readonly ShiftAnomalyCategory[] All =
    {
        ShiftAnomalyCategory.Documentation,
        ShiftAnomalyCategory.Vitals,
        ShiftAnomalyCategory.Behavior,
        ShiftAnomalyCategory.Physical,
        ShiftAnomalyCategory.Supernatural
    };

    /// <summary>Maps a category base-class name (e.g. "PhysicalAnomaly") to its flag.</summary>
    public static ShiftAnomalyCategory FromTypeName(string typeName)
    {
        switch (typeName)
        {
            case "DocumentationAnomaly": return ShiftAnomalyCategory.Documentation;
            case "VitalsAnomaly": return ShiftAnomalyCategory.Vitals;
            case "BehaviorAnomaly": return ShiftAnomalyCategory.Behavior;
            case "PhysicalAnomaly": return ShiftAnomalyCategory.Physical;
            case "SupernaturalAnomaly": return ShiftAnomalyCategory.Supernatural;
            default: return ShiftAnomalyCategory.None;
        }
    }

    public static ShiftAnomalyCategory FromTypeNames(IEnumerable<string> typeNames)
    {
        ShiftAnomalyCategory mask = ShiftAnomalyCategory.None;
        if (typeNames != null)
        {
            foreach (string typeName in typeNames)
                mask |= FromTypeName(typeName);
        }
        return mask;
    }

    /// <summary>Walks the anomaly's type hierarchy up to its category base class (same rule as scoring).</summary>
    public static ShiftAnomalyCategory Of(Anomaly anomaly)
    {
        if (anomaly == null)
            return ShiftAnomalyCategory.None;

        Type t = anomaly.GetType();
        while (t != null && t != typeof(Anomaly))
        {
            ShiftAnomalyCategory category = FromTypeName(t.Name);
            if (category != ShiftAnomalyCategory.None)
                return category;
            t = t.BaseType;
        }
        return ShiftAnomalyCategory.None;
    }

    public static string Label(ShiftAnomalyCategory category)
    {
        switch (category)
        {
            case ShiftAnomalyCategory.Documentation: return "Documentation";
            case ShiftAnomalyCategory.Vitals: return "Vitals";
            case ShiftAnomalyCategory.Behavior: return "Behavior";
            case ShiftAnomalyCategory.Physical: return "Physical";
            case ShiftAnomalyCategory.Supernatural: return "Supernatural";
            default: return "Other";
        }
    }

    /// <summary>"BlackEyesAnomaly" -> "Black Eyes", "IDNumberWrongAnomaly" -> "ID Number Wrong".</summary>
    public static string PrettifyTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return "Unknown Anomaly";

        const string suffix = "Anomaly";
        if (typeName.Length > suffix.Length && typeName.EndsWith(suffix, StringComparison.Ordinal))
            typeName = typeName.Substring(0, typeName.Length - suffix.Length);

        return System.Text.RegularExpressions.Regex.Replace(
            typeName, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }
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

    /// <summary>False for results restored from saves written before the per-anomaly breakdown was recorded.</summary>
    public bool HasBreakdown;
    /// <summary>Checklist categories the player ticked on the exam page (None when not assessed).</summary>
    public ShiftAnomalyCategory FlaggedCategories;
    /// <summary>Every anomaly active on the subject at processing time, and whether the player caught it.</summary>
    public ShiftAnomalyResult[] Anomalies;

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

        serializer.SerializeValue(ref HasBreakdown);

        byte flagged = (byte)FlaggedCategories;
        serializer.SerializeValue(ref flagged);
        FlaggedCategories = (ShiftAnomalyCategory)flagged;

        int anomalyCount = Anomalies != null ? Anomalies.Length : 0;
        serializer.SerializeValue(ref anomalyCount);
        if (serializer.IsReader)
            Anomalies = new ShiftAnomalyResult[anomalyCount];
        for (int i = 0; i < anomalyCount; i++)
            Anomalies[i].NetworkSerialize(serializer);
    }

    /// <summary>
    /// Captures the per-anomaly breakdown for this subject. Pass <paramref name="checkedCategoryTypeNames"/>
    /// = null when the paperwork was never assessed (killed / fled): anomalies are still listed, none caught.
    /// </summary>
    public void CaptureBreakdown(IReadOnlyList<Anomaly> activeAnomalies, ICollection<string> checkedCategoryTypeNames)
    {
        HasBreakdown = true;
        FlaggedCategories = ShiftAnomalyCategories.FromTypeNames(checkedCategoryTypeNames);

        var results = new List<ShiftAnomalyResult>();
        if (activeAnomalies != null)
        {
            foreach (Anomaly anomaly in activeAnomalies)
            {
                if (anomaly == null)
                    continue;

                ShiftAnomalyCategory category = ShiftAnomalyCategories.Of(anomaly);
                results.Add(new ShiftAnomalyResult
                {
                    Name = anomaly.ReportDisplayName,
                    Category = category,
                    Caught = category != ShiftAnomalyCategory.None && (FlaggedCategories & category) != 0
                });
            }
        }

        Anomalies = results.ToArray();
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
