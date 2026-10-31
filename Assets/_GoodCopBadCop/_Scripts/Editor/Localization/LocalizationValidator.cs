using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;

public enum IssueSeverity { Warning, Error }

public struct LocIssue
{
    public IssueSeverity severity;
    public string code;
    public string message;
    public override string ToString() { return severity + " [" + code + "] " + message; }
}

/// <summary>Shared checks used by Import, Preview and Findings.</summary>
public static class LocalizationValidator
{
    private static readonly Regex TagName = new Regex(@"^<\s*(/?)\s*([A-Za-z]+)|^\[\s*(/?)\s*([A-Za-z0-9_]+)");

    public static List<LocIssue> Validate(string source, string translation, LocalizationSettings settings, TMP_FontAsset font = null, int maxChars = 0)
    {
        var issues = new List<LocIssue>();
        if (string.IsNullOrEmpty(translation)) return issues;
        if (source == null) source = "";

        var srcSegs = PseudoTokenizer.Tokenize(source);
        var trSegs = PseudoTokenizer.Tokenize(translation);

        // Per-key character limit (sheet column max_chars): the text can't shrink any further and stay readable.
        if (maxChars > 0)
        {
            var over = MaxCharsIssue(trSegs, maxChars);
            if (over.HasValue) issues.Add(over.Value);
        }

        // Placeholders: multiset equality
        CompareMultiset(srcSegs, trSegs, PseudoSegmentKind.Placeholder, "placeholder", issues);
        // Escape sequences and line breaks
        CompareMultiset(srcSegs, trSegs, PseudoSegmentKind.Escape, "escape", issues);
        CompareCount(srcSegs, trSegs, PseudoSegmentKind.LineBreak, "linebreak", "Line break count differs", issues, IssueSeverity.Warning);
        // Rich text tag parity
        CompareTags(srcSegs, trSegs, issues);

        if (settings != null)
        {
            if (!string.IsNullOrEmpty(settings.forbiddenCharacters))
                foreach (char c in translation.Distinct())
                    if (settings.forbiddenCharacters.IndexOf(c) >= 0)
                        issues.Add(new LocIssue { severity = IssueSeverity.Error, code = "forbidden", message = "Forbidden character U+" + ((int)c).ToString("X4") + " '" + c + "'." });

            int sv = PseudoTokenizer.VisibleLength(srcSegs);
            int tv = PseudoTokenizer.VisibleLength(trSegs);
            if (sv > 0 && tv > sv * settings.maxLengthRatio)
                issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = "length", message = "Length " + tv + " is " + (100f * tv / sv).ToString("0") + "% of source (limit " + (100f * settings.maxLengthRatio).ToString("0") + "%)." });
        }

        if (font != null)
        {
            var missing = MissingGlyphs(font, translation);
            if (missing.Length > 0)
                issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = "tofu", message = "No glyph for: " + missing });
        }
        return issues;
    }

    /// <summary>Visible characters of <paramref name="text"/>, counted the same way as the max_chars check (tags and placeholders excluded).</summary>
    public static int VisibleLength(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : PseudoTokenizer.VisibleLength(PseudoTokenizer.Tokenize(text));
    }

    /// <summary>Error when the text has more visible characters than the key's max_chars; null when within the limit or no limit is set.</summary>
    public static LocIssue? MaxCharsIssue(string text, int maxChars)
    {
        if (maxChars <= 0 || string.IsNullOrEmpty(text)) return null;
        return MaxCharsIssue(PseudoTokenizer.Tokenize(text), maxChars);
    }

    private static LocIssue? MaxCharsIssue(List<PseudoSegment> segs, int maxChars)
    {
        int len = PseudoTokenizer.VisibleLength(segs);
        if (len <= maxChars) return null;
        return new LocIssue { severity = IssueSeverity.Error, code = "maxchars", message = len + " characters, limit is " + maxChars + " (over by " + (len - maxChars) + ")." };
    }

    public static string MissingGlyphs(TMP_FontAsset font, string text)
    {
        var missing = new List<char>();
        foreach (char c in text.Distinct())
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
            if (!font.HasCharacter(c, true)) missing.Add(c);
        }
        return new string(missing.ToArray());
    }

    private static void CompareMultiset(List<PseudoSegment> a, List<PseudoSegment> b, PseudoSegmentKind kind, string code, List<LocIssue> issues)
    {
        var ca = a.Where(s => s.kind == kind).GroupBy(s => s.text).ToDictionary(g => g.Key, g => g.Count());
        var cb = b.Where(s => s.kind == kind).GroupBy(s => s.text).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in ca)
        {
            cb.TryGetValue(kv.Key, out int n);
            if (n != kv.Value)
                issues.Add(new LocIssue { severity = IssueSeverity.Error, code = code, message = "'" + kv.Key + "' expected x" + kv.Value + ", found x" + n + "." });
        }
        foreach (var kv in cb)
            if (!ca.ContainsKey(kv.Key))
                issues.Add(new LocIssue { severity = IssueSeverity.Error, code = code, message = "Unexpected '" + kv.Key + "'." });
    }

    private static void CompareCount(List<PseudoSegment> a, List<PseudoSegment> b, PseudoSegmentKind kind, string code, string msg, List<LocIssue> issues, IssueSeverity sev)
    {
        int na = a.Count(s => s.kind == kind), nb = b.Count(s => s.kind == kind);
        if (na != nb) issues.Add(new LocIssue { severity = sev, code = code, message = msg + " (" + na + " vs " + nb + ")." });
    }

    private static Dictionary<string, int> TagCounts(List<PseudoSegment> segs)
    {
        var d = new Dictionary<string, int>();
        foreach (var s in segs.Where(x => x.kind == PseudoSegmentKind.Tag))
        {
            if (s.text == "[[" || s.text == "]]") { d[s.text] = d.TryGetValue(s.text, out int q) ? q + 1 : 1; continue; }
            var m = TagName.Match(s.text);
            if (!m.Success) continue;
            bool close = m.Groups[1].Value == "/" || m.Groups[3].Value == "/";
            string name = (m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[4].Value).ToLowerInvariant();
            string id = (close ? "/" : "") + name;
            d[id] = d.TryGetValue(id, out int n) ? n + 1 : 1;
        }
        return d;
    }

    private static void CompareTags(List<PseudoSegment> a, List<PseudoSegment> b, List<LocIssue> issues)
    {
        var ta = TagCounts(a);
        var tb = TagCounts(b);
        foreach (var kv in ta)
        {
            tb.TryGetValue(kv.Key, out int n);
            if (n != kv.Value)
                issues.Add(new LocIssue { severity = IssueSeverity.Error, code = "tags", message = "Tag '" + kv.Key + "' expected x" + kv.Value + ", found x" + n + "." });
        }
        foreach (var kv in tb)
            if (!ta.ContainsKey(kv.Key))
                issues.Add(new LocIssue { severity = IssueSeverity.Error, code = "tags", message = "Unexpected tag '" + kv.Key + "'." });
    }

    // ---------------------------------------------------------------- source_en lint (style guide, see the Localization Source Text Fixes plan D1-D14)

    private class LintRule
    {
        public string code, message;
        public Regex match;
        public Func<string, string, bool> allow; // (sheet, source) -> accepted exception
    }

    private static readonly Regex Typographic = new Regex("[\u2026\u2018\u2019\u201C\u201D]");
    private static bool IsLuka(string sheet) { return sheet != null && sheet.StartsWith("Dialogue_Luka", StringComparison.Ordinal); }

    private static readonly LintRule[] LintRules =
    {
        // Confirmed terminology (D1-D6, D11 and the issue map).
        new LintRule { code = "term", message = "Verdict is KILL / Kill, not ELIMINATE (D1).", match = new Regex(@"\bELIMINATE\b") },
        new LintRule { code = "term", message = "Verdict wording is 'killed', not 'eliminated' (D1).", match = new Regex(@"\b[Ee]liminat(e|ed|ion)\b"),
                       allow = (sheet, s) => s.Contains("no longer human must be eliminated") },
        new LintRule { code = "term", message = "Exam name is 'Behavior Exam' (D3).", match = new Regex(@"\bMental Exam\b|\bBehavior Checklist\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Exam name is 'Physical Exam' (D3).", match = new Regex(@"\bMutations? Exam\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Chart name is 'Disposition Chart' (D4).", match = new Regex(@"\bFINAL DISPOSITION\b|\bscoring chart\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Use 'postbox' (D5).", match = new Regex(@"\bmailbox\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Item is 'Mutant Sample' (D6).", match = new Regex(@"\bMutant Bit\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Spelling: 'Canister'.", match = new Regex(@"\bCannister\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Use 'UV mode', not 'UV light mode'.", match = new Regex(@"\bUV light mode\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "'flashlight' is lowercase mid-sentence.", match = new Regex(@"[a-z,;:] Flashlight\b") },
        new LintRule { code = "term", message = "Use 'Geiger counter'.", match = new Regex(@"\bGeiger scanner\b|\bRadiation Scanner\b", RegexOptions.IgnoreCase) },
        new LintRule { code = "term", message = "Tab label is SUPERNATURAL, not OCCULT.", match = new Regex(@"\bOCCULT\b") },
        new LintRule { code = "term", message = "Name is Mariam / Vladislav.", match = new Regex(@"\bMiriam\b|\bVladimir\b") },
        new LintRule { code = "term", message = "Form of address is capitalized: ', Officer' (D7).", match = new Regex(@"(,|^|[.!?]\s)\s*officer(?=\s*[.!?,\u2026\u2014-]|\.\.\.)") },
        new LintRule { code = "term", message = "Temperature is written '~15 \u00B0C' (D14).", match = new Regex(@"\d\u00B0") },

        // US English (D8). Luka's "Mum" is deliberate.
        new LintRule { code = "spelling", message = "UK spelling, use US English (D8).", match = new Regex(
            @"\b(colour|favour|honour|neighbour|behaviour|flavour|labour|humour|rumour|harbour|armour|savour|odour|vapour)\w*|" +
            @"\b(centre|metre|litre|theatre|fibre|sombre)s?\b|" +
            @"\b(realis|organis|apologis|recognis|criticis|memoris|sympathis|prioritis|authoris|minimis|maximis|utilis|analys|paralys)(e|es|ed|ing)\b|" +
            @"\b(travell|cancell|labell|modell)(ed|er|ers|ing)\b|" +
            @"\b(grey|whilst|amongst|learnt|spelt|cheque|programme|defence|offence|licence|practise|tyres?|aluminium|mould|pyjamas|jewellery|plough|sceptic\w*|fulfil|enrol|skilful|arse)\b",
            RegexOptions.IgnoreCase) },
        new LintRule { code = "spelling", message = "UK 'Mum', use 'Mom' (D8; only Luka keeps 'Mum').", match = new Regex(@"\bmum(my|s)?\b", RegexOptions.IgnoreCase),
                       allow = (sheet, s) => IsLuka(sheet) },
    };

    /// <summary>
    /// Style lint for English source text: stray whitespace, typographic ellipsis / curly quotes (D9), confirmed terminology and US spelling.
    /// Warnings only; they flag regressions before the sheets go to translators.
    /// </summary>
    public static List<LocIssue> LintSource(string sheet, string source)
    {
        var issues = new List<LocIssue>();
        if (string.IsNullOrEmpty(source)) return issues;

        if (source != source.Trim())
            issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = "whitespace", message = "Leading or trailing whitespace / line break." });
        if (source.Contains("  "))
            issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = "whitespace", message = "Double space." });
        var typo = Typographic.Match(source);
        if (typo.Success)
            issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = "typography", message = "Use '...' and straight quotes, found '" + typo.Value + "' (D9)." });

        foreach (var rule in LintRules)
        {
            var m = rule.match.Match(source);
            if (!m.Success || (rule.allow != null && rule.allow(sheet, source))) continue;
            issues.Add(new LocIssue { severity = IssueSeverity.Warning, code = rule.code, message = rule.message + " Found '" + m.Value.Trim() + "'." });
        }
        return issues;
    }

    /// <summary>Lints every entry; returns "sheet | key | [code] message | source" lines.</summary>
    public static List<string> LintAll(IEnumerable<LocEntry> entries)
    {
        var lines = new List<string>();
        foreach (var e in entries)
            foreach (var i in LintSource(e.sheet, e.source))
                lines.Add(e.sheet + " | " + e.key + " | [" + i.code + "] " + i.message + " | " + (e.source ?? "").Replace("\r", "\\r").Replace("\n", "\\n"));
        return lines;
    }

    /// <summary>Runs the self-check of every registered pseudolocale across all sources; returns violations keyed by locale.</summary>
    public static List<string> SelfCheckPseudo(IEnumerable<LocEntry> entries, float expansion)
    {
        var report = new List<string>();
        foreach (var locale in PseudoLocalizer.All)
            foreach (var e in entries)
            {
                string output = locale.Generate(e.source, e.key, expansion);
                foreach (var v in locale.Verify(e.source, output, e.key, expansion))
                    report.Add(locale.Code + " | " + e.key + " | " + v);
            }
        return report;
    }
}
