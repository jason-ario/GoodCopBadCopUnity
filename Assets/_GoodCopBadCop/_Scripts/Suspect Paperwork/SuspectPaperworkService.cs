using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodCopBadCop.SuspectPaperwork
{
    public interface ISuspectPaperworkService
    {
        SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex);
        SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex);
    }

    public sealed class SuspectPaperworkService : ISuspectPaperworkService
    {
        private readonly SuspectPaperworkModel model;

        public SuspectPaperworkService(SuspectPaperworkModel model)
        {
            this.model = model;
        }

        public SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex)
        {
            if (suspect == null || suspect.Data == null)
            {
                model.SetCurrent(SuspectPaperworkState.Empty);
                return SuspectPaperworkState.Empty;
            }

            IEnumerable<string> activeAnomalyTypeNames = suspect.AnomalyController != null
                ? suspect.AnomalyController.activeAnomalies
                    .Where(anomaly => anomaly != null)
                    .Select(anomaly => anomaly.GetType().Name)
                : Enumerable.Empty<string>();

            SuspectPaperworkState state = BuildState(
                suspect.Data,
                suspect.IDPhoto,
                activeAnomalyTypeNames,
                currentDay,
                suspectIndex,
                suspect.ChosenEntryReasonIndex);

            model.SetCurrent(state);
            return state;
        }

        public SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex)
        {
            SuspectPaperworkState state = BuildState(
                data,
                idPhoto != null ? idPhoto : data != null ? data.IDPhoto : null,
                activeAnomalyTypeNames,
                currentDay,
                suspectIndex,
                chosenEntryReasonIndex: 0);

            model.SetCurrent(state);
            return state;
        }

        private static SuspectPaperworkState BuildState(
            global::SuspectData data,
            Texture idPhoto,
            IEnumerable<string> activeAnomalyTypeNames,
            int currentDay,
            int suspectIndex,
            int chosenEntryReasonIndex)
        {
            if (data == null)
                return SuspectPaperworkState.Empty;

            var active = new HashSet<string>(activeAnomalyTypeNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            string fullName = $"{data.FirstName} {data.LastName}".Trim();
            string birthDate = data.DateOfBirth;
            string sex = data.Sex;
            string idNumber = data.IDNumber;
            string applicationFullName = fullName;
            string applicationBirthDate = birthDate;
            string applicationSex = sex;
            string applicationIdNumber = idNumber;
            string expirationDate = data.EntryPermitExpiryDate;
            string entryReason = ResolveEntryReason(data, currentDay, chosenEntryReasonIndex, useInvalidReasons: Has(active, nameof(InvalidEntryReason)));
            bool applicationVisible = !Has(active, nameof(MissingDocumentAnomaly));

            if (Has(active, nameof(NameWrong)))
                applicationFullName = MutateName(applicationFullName, BuildSeed(data, currentDay, suspectIndex, nameof(NameWrong), applicationFullName));

            if (Has(active, nameof(BirthDateWrong)))
                applicationBirthDate = MutateDigits(applicationBirthDate, BuildSeed(data, currentDay, suspectIndex, nameof(BirthDateWrong), applicationBirthDate), preserve: '/');

            if (Has(active, nameof(IDNumberWrong)) || Has(active, nameof(FakeIdAnomaly)))
                applicationIdNumber = MutateDigits(applicationIdNumber, BuildSeed(data, currentDay, suspectIndex, nameof(IDNumberWrong), applicationIdNumber), preserve: '\0');

            if (Has(active, nameof(SexWrong)))
                applicationSex = MutateSex(applicationSex);

            if (Has(active, nameof(ExpirationDateAnomaly)))
                expirationDate = string.IsNullOrWhiteSpace(expirationDate) ? "EXPIRED" : "EXPIRED " + expirationDate;

            return new SuspectPaperworkState(
                fullName,
                birthDate,
                sex,
                idNumber,
                applicationFullName,
                applicationBirthDate,
                applicationSex,
                applicationIdNumber,
                entryReason,
                expirationDate,
                data.IsResident,
                applicationVisible,
                idPhoto);
        }

        private static bool Has(HashSet<string> activeAnomalyTypeNames, string typeName)
            => activeAnomalyTypeNames.Contains(typeName);

        private static string ResolveEntryReason(global::SuspectData data, int currentDay, int chosenEntryReasonIndex, bool useInvalidReasons)
        {
            global::SuspectData.EntryReasonSet reasonSet = useInvalidReasons
                ? data.invalidEntryReasons
                : data.entryReasons;

            string[] reasons = GetReasonsForDay(reasonSet, currentDay);
            string reason = GetReasonAtOrFirst(reasons, chosenEntryReasonIndex);
            if (!string.IsNullOrWhiteSpace(reason))
                return reason;

            if (useInvalidReasons)
                return string.Empty;

            return "Entry";
        }

        private static string[] GetReasonsForDay(global::SuspectData.EntryReasonSet reasonSet, int currentDay)
        {
            if (currentDay < 11)
                return reasonSet.earlyDaysReasons;
            if (currentDay < 21)
                return reasonSet.midDaysReasons;
            return reasonSet.finalDaysReasons;
        }

        private static string GetReasonAtOrFirst(string[] reasons, int index)
        {
            if (reasons == null || reasons.Length == 0)
                return null;

            if (index >= 0 && index < reasons.Length && !string.IsNullOrWhiteSpace(reasons[index]))
                return reasons[index];

            return reasons.FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        }

        private static string MutateName(string value, int seed)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "WRONG NAME";

            char[] chars = value.ToCharArray();
            List<int> candidates = new();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsWhiteSpace(chars[i]))
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return value + "?";

            int changes = Mathf.Min(candidates.Count, 1 + PositiveModulo(seed, Mathf.Min(3, candidates.Count)));
            for (int i = 0; i < changes; i++)
            {
                int candidateIndex = PositiveModulo(Mix(seed, i), candidates.Count);
                int charIndex = candidates[candidateIndex];
                chars[charIndex] = MutateLetterPreservingCase(chars[charIndex], Mix(seed, i + 17));
                candidates.RemoveAt(candidateIndex);
            }

            return new string(chars);
        }

        private static char MutateLetterPreservingCase(char original, int seed)
        {
            char replacement = (char)('a' + PositiveModulo(seed, 26));
            return char.IsUpper(original) ? char.ToUpperInvariant(replacement) : replacement;
        }

        private static readonly char[][] SimilarDigitReplacements =
        {
            new[] { '8', '9' },
            new[] { '7' },
            new[] { '3' },
            new[] { '5', '8' },
            new[] { '7', '9' },
            new[] { '3', '6' },
            new[] { '5', '8' },
            new[] { '1', '4' },
            new[] { '0', '3', '6', '9' },
            new[] { '0', '8' }
        };

        private static string MutateDigits(string value, int seed, char preserve)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "0000000";

            char[] chars = value.ToCharArray();
            List<int> digitIndices = new();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == preserve)
                    continue;

                if (char.IsDigit(chars[i]))
                    digitIndices.Add(i);
            }

            if (digitIndices.Count == 0)
                return value + "?";

            int changes = Mathf.Min(digitIndices.Count, 1 + PositiveModulo(seed, Mathf.Min(2, digitIndices.Count)));
            for (int i = 0; i < changes; i++)
            {
                int candidateIndex = PositiveModulo(Mix(seed, i), digitIndices.Count);
                int charIndex = digitIndices[candidateIndex];
                chars[charIndex] = MutateDigitToSimilar(chars[charIndex], Mix(seed, i + 31));
                digitIndices.RemoveAt(candidateIndex);
            }

            return new string(chars);
        }

        private static char MutateDigitToSimilar(char digit, int seed)
        {
            char[] replacements = SimilarDigitReplacements[digit - '0'];
            return replacements[PositiveModulo(seed, replacements.Length)];
        }

        private static string MutateSex(string sex)
        {
            if (string.Equals(sex, "Male", StringComparison.OrdinalIgnoreCase))
                return "Female";
            if (string.Equals(sex, "Female", StringComparison.OrdinalIgnoreCase))
                return "Male";
            return string.IsNullOrWhiteSpace(sex) ? "Unknown" : sex + "?";
        }

        private static int BuildSeed(global::SuspectData data, int currentDay, int suspectIndex, string anomalyTypeName, string sourceValue)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + currentDay;
                hash = hash * 31 + suspectIndex;
                hash = hash * 31 + StableHash(data != null ? data.name : string.Empty);
                hash = hash * 31 + StableHash(anomalyTypeName);
                hash = hash * 31 + StableHash(sourceValue);
                return hash;
            }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 5381;
                if (value == null)
                    return hash;

                for (int i = 0; i < value.Length; i++)
                    hash = ((hash << 5) + hash) ^ value[i];

                return hash;
            }
        }

        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                int value = seed + salt * 0x6D2B79F5;
                value ^= value >> 15;
                value *= 0x2C1B3C6D;
                value ^= value >> 12;
                value *= 0x297A2D39;
                value ^= value >> 15;
                return value;
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            if (divisor <= 0)
                return 0;

            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
