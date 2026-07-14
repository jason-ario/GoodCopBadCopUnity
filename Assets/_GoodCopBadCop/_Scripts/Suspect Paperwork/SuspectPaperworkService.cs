using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GoodCopBadCop.SuspectPaperwork
{
    public interface ISuspectPaperworkService
    {
        SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null);
        SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null);
    }

    public sealed class SuspectPaperworkService : ISuspectPaperworkService
    {
        private readonly SuspectPaperworkModel model;

        public SuspectPaperworkService(SuspectPaperworkModel model)
        {
            this.model = model;
        }

        public SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null)
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
                suspect.ChosenEntryReasonIndex,
                idPhotoPool);

            model.SetCurrent(state);
            return state;
        }

        public SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null)
        {
            SuspectPaperworkState state = BuildState(
                data,
                idPhoto != null ? idPhoto : data != null ? data.IDPhoto : null,
                activeAnomalyTypeNames,
                currentDay,
                suspectIndex,
                chosenEntryReasonIndex: 0,
                idPhotoPool);

            model.SetCurrent(state);
            return state;
        }

        private static SuspectPaperworkState BuildState(
            global::SuspectData data,
            Texture idPhoto,
            IEnumerable<string> activeAnomalyTypeNames,
            int currentDay,
            int suspectIndex,
            int chosenEntryReasonIndex,
            IEnumerable<Texture> idPhotoPool)
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
            string applicationExpirationDate = expirationDate;
            bool documentsVisible = !Has(active, nameof(MissingDocumentAnomaly));
            bool applicationVisible = documentsVisible;
            bool isFakeId = documentsVisible && Has(active, nameof(FakeIdAnomaly));
            Texture resolvedIdPhoto = ResolveIdPhoto(data, idPhoto, idPhotoPool, active, documentsVisible, currentDay, suspectIndex);
            string entryReason = ResolveEntryReason(data, currentDay, chosenEntryReasonIndex, useInvalidReasons: documentsVisible && Has(active, nameof(InvalidEntryReasonAnomaly)));

            if (documentsVisible && Has(active, nameof(NameWrongAnomaly)))
                applicationFullName = MutateName(applicationFullName, BuildSeed(data, currentDay, suspectIndex, nameof(NameWrongAnomaly), applicationFullName));

            if (documentsVisible && Has(active, nameof(BirthDateWrongAnomaly)))
                applicationBirthDate = MutateDocumentDate(applicationBirthDate, BuildSeed(data, currentDay, suspectIndex, nameof(BirthDateWrongAnomaly), applicationBirthDate));

            if (documentsVisible && Has(active, nameof(IDNumberWrongAnomaly)))
                applicationIdNumber = MutateDigits(applicationIdNumber, BuildSeed(data, currentDay, suspectIndex, nameof(IDNumberWrongAnomaly), applicationIdNumber), preserve: '\0');

            if (documentsVisible && Has(active, nameof(SexWrongAnomaly)))
                applicationSex = MutateSex(applicationSex);

            if (documentsVisible && Has(active, nameof(ExpirationDateAnomaly)))
                applicationExpirationDate = MutateDocumentDate(applicationExpirationDate, BuildSeed(data, currentDay, suspectIndex, nameof(ExpirationDateAnomaly), applicationExpirationDate));

            return new SuspectPaperworkState(
                fullName,
                birthDate,
                sex,
                idNumber,
                applicationFullName,
                applicationBirthDate,
                applicationSex,
                applicationIdNumber,
                applicationExpirationDate,
                entryReason,
                expirationDate,
                data.IsResident,
                documentsVisible,
                applicationVisible,
                isFakeId,
                resolvedIdPhoto);
        }

        private static Texture ResolveIdPhoto(
            global::SuspectData data,
            Texture idPhoto,
            IEnumerable<Texture> idPhotoPool,
            HashSet<string> active,
            bool documentsVisible,
            int currentDay,
            int suspectIndex)
        {
            if (!documentsVisible || !Has(active, nameof(PhotoIDMismatchAnomaly)))
                return idPhoto;

            List<Texture> candidates = (idPhotoPool ?? Enumerable.Empty<Texture>())
                .Where(photo => photo != null && !ReferenceEquals(photo, idPhoto))
                .GroupBy(photo => photo.name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(photo => photo.name, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0)
                return idPhoto;

            int seed = BuildSeed(data, currentDay, suspectIndex, nameof(PhotoIDMismatchAnomaly), idPhoto != null ? idPhoto.name : string.Empty);
            return candidates[PositiveModulo(seed, candidates.Count)];
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

        private readonly struct ParsedDocumentDate
        {
            public readonly DateTime Date;
            public readonly bool HasYear;
            public readonly bool UsesMonthName;
            public readonly bool UsesFullMonthName;
            public readonly bool MonthIsUppercase;

            public ParsedDocumentDate(DateTime date, bool hasYear, bool usesMonthName, bool usesFullMonthName, bool monthIsUppercase)
            {
                Date = date;
                HasYear = hasYear;
                UsesMonthName = usesMonthName;
                UsesFullMonthName = usesFullMonthName;
                MonthIsUppercase = monthIsUppercase;
            }
        }

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

        private static string MutateDocumentDate(string value, int seed)
        {
            if (!TryParseDocumentDate(value, out ParsedDocumentDate parsed))
                return value;

            string formatted = FormatDocumentDate(parsed.Date, parsed);
            if (TryMutateDateWithDigitReplacements(formatted, parsed, seed, similarOnly: true, out string mutated))
                return mutated;

            return TryMutateDateWithDigitReplacements(formatted, parsed, seed, similarOnly: false, out mutated)
                ? mutated
                : value;
        }

        private static bool TryParseDocumentDate(string value, out ParsedDocumentDate parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            string[] numericFormats = { "dd/MM/yyyy", "d/M/yyyy" };
            if (DateTime.TryParseExact(trimmed, numericFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime numericDate))
            {
                parsed = new ParsedDocumentDate(numericDate, hasYear: true, usesMonthName: false, usesFullMonthName: false, monthIsUppercase: false);
                return true;
            }

            string[] monthNameFormats = { "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy" };
            if (DateTime.TryParseExact(trimmed, monthNameFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime namedDateWithYear))
            {
                parsed = new ParsedDocumentDate(
                    namedDateWithYear,
                    hasYear: true,
                    usesMonthName: true,
                    usesFullMonthName: HasFullMonthName(trimmed),
                    monthIsUppercase: HasUppercaseMonthName(trimmed));
                return true;
            }

            string[] monthNameFormatsWithoutYear = { "dd MMM", "d MMM", "dd MMMM", "d MMMM" };
            if (DateTime.TryParseExact(trimmed, monthNameFormatsWithoutYear, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime namedDate))
            {
                parsed = new ParsedDocumentDate(
                    namedDate,
                    hasYear: false,
                    usesMonthName: true,
                    usesFullMonthName: HasFullMonthName(trimmed),
                    monthIsUppercase: HasUppercaseMonthName(trimmed));
                return true;
            }

            return false;
        }

        private static bool TryMutateDateWithDigitReplacements(string formatted, ParsedDocumentDate original, int seed, bool similarOnly, out string mutated)
        {
            mutated = null;

            char[] chars = formatted.ToCharArray();
            var digitIndices = new List<int>();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsDigit(chars[i]))
                    digitIndices.Add(i);
            }

            if (digitIndices.Count == 0)
                return false;

            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            AddDateCandidates(chars, digitIndices, original, candidates, seen, similarOnly, firstIndex: -1, firstReplacement: '\0');

            for (int i = 0; i < digitIndices.Count; i++)
            {
                int firstIndex = digitIndices[i];
                foreach (char firstReplacement in GetDateDigitReplacements(chars[firstIndex], similarOnly))
                    AddDateCandidates(chars, digitIndices, original, candidates, seen, similarOnly, firstIndex, firstReplacement);
            }

            if (candidates.Count == 0)
                return false;

            mutated = candidates[PositiveModulo(seed, candidates.Count)];
            return true;
        }

        private static void AddDateCandidates(
            char[] source,
            List<int> digitIndices,
            ParsedDocumentDate original,
            List<string> candidates,
            HashSet<string> seen,
            bool similarOnly,
            int firstIndex,
            char firstReplacement)
        {
            foreach (int secondIndex in digitIndices)
            {
                if (secondIndex == firstIndex)
                    continue;

                foreach (char secondReplacement in GetDateDigitReplacements(source[secondIndex], similarOnly))
                {
                    char[] chars = (char[])source.Clone();
                    if (firstIndex >= 0)
                        chars[firstIndex] = firstReplacement;
                    chars[secondIndex] = secondReplacement;

                    string candidate = new string(chars);
                    if (!seen.Add(candidate))
                        continue;

                    if (!TryParseDocumentDate(candidate, out ParsedDocumentDate parsed) || parsed.Date == original.Date)
                        continue;

                    candidates.Add(FormatDocumentDate(parsed.Date, original));
                }
            }
        }

        private static IEnumerable<char> GetDateDigitReplacements(char digit, bool similarOnly)
        {
            char[] similar = SimilarDigitReplacements[digit - '0'];
            if (similarOnly)
                return similar;

            var replacements = new List<char>(9);
            for (char candidate = '0'; candidate <= '9'; candidate++)
            {
                if (candidate != digit && !similar.Contains(candidate))
                    replacements.Add(candidate);
            }

            return replacements;
        }

        private static string FormatDocumentDate(DateTime date, ParsedDocumentDate parsed)
        {
            if (!parsed.UsesMonthName)
                return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            string monthFormat = parsed.UsesFullMonthName ? "MMMM" : "MMM";
            string month = date.ToString(monthFormat, CultureInfo.InvariantCulture);
            if (parsed.MonthIsUppercase)
                month = month.ToUpperInvariant();

            if (parsed.HasYear)
                return $"{date.ToString("dd", CultureInfo.InvariantCulture)} {month} {date.ToString("yyyy", CultureInfo.InvariantCulture)}";

            return $"{date.ToString("dd", CultureInfo.InvariantCulture)} {month}";
        }

        private static bool HasFullMonthName(string value)
            => GetMonthToken(value).Length > 3;

        private static bool HasUppercaseMonthName(string value)
        {
            string token = GetMonthToken(value);
            return token.Length > 0 && token.All(character => !char.IsLetter(character) || char.IsUpper(character));
        }

        private static string GetMonthToken(string value)
        {
            string[] tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length >= 2 ? tokens[1] : string.Empty;
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
