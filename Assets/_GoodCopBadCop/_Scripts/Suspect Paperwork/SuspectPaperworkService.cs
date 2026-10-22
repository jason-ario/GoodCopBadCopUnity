using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GoodCopBadCop.SuspectPaperwork
{
    public interface ISuspectPaperworkService
    {
        SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null, bool localize = false);
        SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null, bool localize = false);
    }

    /// <summary>
    /// Language-neutral description of a suspect's paperwork, replicated on documents so every client can rebuild the
    /// text in its OWN language (<see cref="SuspectPaperworkService.BuildFromSpec"/>). Only identifiers and the
    /// documentation-anomaly mask travel; misspellings and date mutations are re-derived locally from the same seeds.
    /// </summary>
    public struct PaperworkSpec : Unity.Netcode.INetworkSerializable, IEquatable<PaperworkSpec>
    {
        public Unity.Collections.FixedString64Bytes DataName;
        public int Day;
        public int SuspectIndex;
        public int EntryReasonIndex;
        public ushort Anomalies;

        public bool IsValid => DataName.Length > 0;

        public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
        {
            serializer.SerializeValue(ref DataName);
            serializer.SerializeValue(ref Day);
            serializer.SerializeValue(ref SuspectIndex);
            serializer.SerializeValue(ref EntryReasonIndex);
            serializer.SerializeValue(ref Anomalies);
        }

        public bool Equals(PaperworkSpec other) =>
            DataName.Equals(other.DataName) && Day == other.Day && SuspectIndex == other.SuspectIndex &&
            EntryReasonIndex == other.EntryReasonIndex && Anomalies == other.Anomalies;
    }

    /// <summary>
    /// Resolves the <see cref="global::SuspectData"/> named in a <see cref="PaperworkSpec"/> on any client: from the live
    /// suspect object when given, otherwise from loaded SuspectData assets (documents outlive their suspect in folders).
    /// </summary>
    public static class SuspectDataLookup
    {
        private static readonly Dictionary<string, global::SuspectData> Cache = new(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        public static global::SuspectData Find(string assetName, global::SuspectCharacter liveSuspect = null)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            if (liveSuspect != null && liveSuspect.Data != null && liveSuspect.Data.name == assetName)
                return liveSuspect.Data;

            if (Cache.TryGetValue(assetName, out global::SuspectData cached) && cached != null)
                return cached;

            Cache.Clear();
            foreach (global::SuspectData data in Resources.FindObjectsOfTypeAll<global::SuspectData>())
                if (data != null && !Cache.ContainsKey(data.name))
                    Cache[data.name] = data;

            return Cache.TryGetValue(assetName, out cached) ? cached : null;
        }
    }

    /// <summary>Bit mask of the anomaly types that change paperwork text (see <see cref="PaperworkSpec"/>).</summary>
    public static class PaperworkAnomalyMask
    {
        private static readonly string[] TypeNames =
        {
            nameof(NameWrongAnomaly), nameof(BirthDateWrongAnomaly), nameof(IDNumberWrongAnomaly), nameof(SexWrongAnomaly),
            nameof(ExpirationDateAnomaly), nameof(InvalidEntryReasonAnomaly), nameof(MissingDocumentAnomaly),
            nameof(FakeIdAnomaly), nameof(PhotoIDMismatchAnomaly)
        };

        public static ushort FromTypeNames(IEnumerable<string> typeNames)
        {
            ushort mask = 0;
            if (typeNames == null) return mask;
            foreach (string name in typeNames)
            {
                int bit = Array.IndexOf(TypeNames, name);
                if (bit >= 0) mask |= (ushort)(1 << bit);
            }
            return mask;
        }

        public static IEnumerable<string> ToTypeNames(ushort mask)
        {
            for (int bit = 0; bit < TypeNames.Length; bit++)
                if ((mask & (1 << bit)) != 0) yield return TypeNames[bit];
        }
    }

    public sealed class SuspectPaperworkService : ISuspectPaperworkService
    {
        private readonly SuspectPaperworkModel model;

        public SuspectPaperworkService(SuspectPaperworkModel model)
        {
            this.model = model;
        }

        public SuspectPaperworkState BuildForSuspect(global::SuspectCharacter suspect, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null, bool localize = false)
        {
            if (suspect == null || suspect.Data == null)
            {
                model.SetCurrent(SuspectPaperworkState.Empty);
                return SuspectPaperworkState.Empty;
            }

            SuspectPaperworkState state = BuildState(
                suspect.Data,
                suspect.IDPhoto,
                ActiveAnomalyTypeNames(suspect),
                currentDay,
                suspectIndex,
                suspect.ChosenEntryReasonIndex,
                idPhotoPool,
                localize);

            model.SetCurrent(state);
            return state;
        }

        public SuspectPaperworkState BuildForPreview(global::SuspectData data, Texture idPhoto, IEnumerable<string> activeAnomalyTypeNames, int currentDay, int suspectIndex, IEnumerable<Texture> idPhotoPool = null, bool localize = false)
        {
            SuspectPaperworkState state = BuildState(
                data,
                idPhoto != null ? idPhoto : data != null ? data.IDPhoto : null,
                activeAnomalyTypeNames,
                currentDay,
                suspectIndex,
                chosenEntryReasonIndex: 0,
                idPhotoPool,
                localize);

            model.SetCurrent(state);
            return state;
        }

        /// <summary>Language-neutral spec for a suspect's paperwork, replicated on the spawned documents.</summary>
        public static PaperworkSpec BuildSpec(global::SuspectCharacter suspect, int currentDay, int suspectIndex)
        {
            if (suspect == null || suspect.Data == null) return default;
            return new PaperworkSpec
            {
                DataName = new Unity.Collections.FixedString64Bytes(suspect.Data.name),
                Day = currentDay,
                SuspectIndex = suspectIndex,
                EntryReasonIndex = suspect.ChosenEntryReasonIndex,
                Anomalies = PaperworkAnomalyMask.FromTypeNames(ActiveAnomalyTypeNames(suspect))
            };
        }

        /// <summary>
        /// Rebuilds paperwork text from a replicated <see cref="PaperworkSpec"/> in the LOCAL language. Misspelled names
        /// use the local alphabet, month names and sex/entry-reason text are translated, and every mutation uses the same
        /// seeds as the server, so each client sees the same anomaly in its own language. The photo is not resolved here
        /// (documents replicate the photo name separately).
        /// </summary>
        public static SuspectPaperworkState BuildFromSpec(global::SuspectData data, PaperworkSpec spec)
        {
            return BuildState(data, null, PaperworkAnomalyMask.ToTypeNames(spec.Anomalies), spec.Day, spec.SuspectIndex,
                spec.EntryReasonIndex, null, localize: true);
        }

        private static IEnumerable<string> ActiveAnomalyTypeNames(global::SuspectCharacter suspect)
        {
            return suspect.AnomalyController != null
                ? suspect.AnomalyController.activeAnomalies
                    .Where(anomaly => anomaly != null)
                    .Select(anomaly => anomaly.GetType().Name)
                    .ToList()
                : Enumerable.Empty<string>();
        }

        private static SuspectPaperworkState BuildState(
            global::SuspectData data,
            Texture idPhoto,
            IEnumerable<string> activeAnomalyTypeNames,
            int currentDay,
            int suspectIndex,
            int chosenEntryReasonIndex,
            IEnumerable<Texture> idPhotoPool,
            bool localize = false)
        {
            if (data == null)
                return SuspectPaperworkState.Empty;

            // English is the source: no translation pass, byte-identical to the original behaviour.
            localize = localize && Loc.CurrentLanguage != Loc.SourceLanguage;

            var active = new HashSet<string>(activeAnomalyTypeNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            string sourceFullName = $"{data.FirstName} {data.LastName}".Trim();
            string fullName = localize ? $"{Loc.Name(data.FirstName)} {Loc.Name(data.LastName)}".Trim() : sourceFullName;
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
            string entryReason = ResolveEntryReason(data, chosenEntryReasonIndex, useInvalidReasons: documentsVisible && Has(active, nameof(InvalidEntryReasonAnomaly)), localize);

            // Seeds always derive from the English source values, so every language mutates "the same way"
            // (same number of changes, same positions where the lengths allow) and results stay deterministic.
            if (documentsVisible && Has(active, nameof(NameWrongAnomaly)))
                applicationFullName = MutateName(applicationFullName, BuildSeed(data, currentDay, suspectIndex, nameof(NameWrongAnomaly), sourceFullName));

            if (documentsVisible && Has(active, nameof(BirthDateWrongAnomaly)))
                applicationBirthDate = MutateDocumentDate(applicationBirthDate, BuildSeed(data, currentDay, suspectIndex, nameof(BirthDateWrongAnomaly), applicationBirthDate));

            if (documentsVisible && Has(active, nameof(IDNumberWrongAnomaly)))
                applicationIdNumber = MutateDigits(applicationIdNumber, BuildSeed(data, currentDay, suspectIndex, nameof(IDNumberWrongAnomaly), applicationIdNumber), preserve: '\0');

            if (documentsVisible && Has(active, nameof(SexWrongAnomaly)))
                applicationSex = MutateSex(applicationSex);

            if (documentsVisible && Has(active, nameof(ExpirationDateAnomaly)))
                applicationExpirationDate = MutateDocumentDate(applicationExpirationDate, BuildSeed(data, currentDay, suspectIndex, nameof(ExpirationDateAnomaly), applicationExpirationDate));

            // Dates and sex are mutated in the English/invariant form (digits only), then rendered in the local language.
            if (localize)
            {
                birthDate = LocalizeDocumentDate(birthDate);
                applicationBirthDate = LocalizeDocumentDate(applicationBirthDate);
                expirationDate = LocalizeDocumentDate(expirationDate);
                applicationExpirationDate = LocalizeDocumentDate(applicationExpirationDate);
                sex = LocalizeSex(sex);
                applicationSex = LocalizeSex(applicationSex);
            }

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

        private static string ResolveEntryReason(global::SuspectData data, int chosenEntryReasonIndex, bool useInvalidReasons, bool localize)
        {
            string[] reasons = useInvalidReasons
                ? data.invalidEntryReasons
                : data.entryReasons;

            int index = GetReasonIndexAtOrFirst(reasons, chosenEntryReasonIndex);
            if (index >= 0)
            {
                string reason = reasons[index];
                if (!localize) return reason;
                string field = useInvalidReasons ? nameof(global::SuspectData.invalidEntryReasons) : nameof(global::SuspectData.entryReasons);
                return Loc.GetFor(data, $"{field}.Array.data[{index}]", reason);
            }

            if (useInvalidReasons)
                return string.Empty;

            return localize ? Loc.Get("doc.entry_reason.default") : "Entry";
        }

        private static int GetReasonIndexAtOrFirst(string[] reasons, int index)
        {
            if (reasons == null || reasons.Length == 0)
                return -1;

            if (index >= 0 && index < reasons.Length && !string.IsNullOrWhiteSpace(reasons[index]))
                return index;

            return Array.FindIndex(reasons, reason => !string.IsNullOrWhiteSpace(reason));
        }

        private static string MutateName(string value, int seed)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Loc.CurrentLanguage == Loc.SourceLanguage ? "WRONG NAME" : Loc.Get("doc.wrong_name");

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

        private const string LatinLetters = "abcdefghijklmnopqrstuvwxyz";
        private const string CyrillicLetters = "абвгдежзийклмнопрстуфхцчшщъыьэюя";

        /// <summary>
        /// Replaces a letter with another letter from the SAME script (Cyrillic names get Cyrillic typos), preserving
        /// case. The replacement always differs from the original so the misspelling is guaranteed to be visible.
        /// </summary>
        private static char MutateLetterPreservingCase(char original, int seed)
        {
            char lower = char.ToLowerInvariant(original);
            string alphabet = CyrillicLetters.IndexOf(lower) >= 0 || (lower >= '\u0400' && lower <= '\u04FF') ? CyrillicLetters : LatinLetters;
            char replacement = alphabet[PositiveModulo(seed, alphabet.Length)];
            if (replacement == lower)
                replacement = alphabet[PositiveModulo(seed + 1, alphabet.Length)];
            return char.IsUpper(original) ? char.ToUpperInvariant(replacement) : replacement;
        }

        // ── Localized rendering of language-neutral values ───────────────────────────

        /// <summary>
        /// Re-renders an English document date ("19 NOV", "02 March 1990", "02/11/1961") with the local language's
        /// month names (Names/Documents keys doc.month.short.N / doc.month.full.N), keeping the original casing style.
        /// Numeric dates and unparseable text are returned unchanged.
        /// </summary>
        private static string LocalizeDocumentDate(string value)
        {
            if (!TryParseDocumentDate(value, out ParsedDocumentDate parsed) || !parsed.UsesMonthName)
                return value;

            int month = parsed.Date.Month;
            string name = Loc.Get((parsed.UsesFullMonthName ? "doc.month.full." : "doc.month.short.") + month);
            if (parsed.MonthIsUppercase)
                name = name.ToUpper(Loc.Culture);

            string day = parsed.Date.ToString("dd", CultureInfo.InvariantCulture);
            return parsed.HasYear
                ? $"{day} {name} {parsed.Date.ToString("yyyy", CultureInfo.InvariantCulture)}"
                : $"{day} {name}";
        }

        /// <summary>Translates a sex value (doc.sex.*). Unknown values pass through; a trailing "?" is kept.</summary>
        private static string LocalizeSex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string trimmed = value.Trim();
            bool question = trimmed.EndsWith("?", StringComparison.Ordinal);
            string core = question ? trimmed.Substring(0, trimmed.Length - 1) : trimmed;
            string key = "doc.sex." + core.ToLowerInvariant();
            string translated = Loc.Has(key) ? Loc.Get(key) : core;
            return question ? translated + "?" : translated;
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
