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

    public static List<LocIssue> Validate(string source, string translation, LocalizationSettings settings, TMP_FontAsset font = null)
    {
        var issues = new List<LocIssue>();
        if (string.IsNullOrEmpty(translation)) return issues;
        if (source == null) source = "";

        var srcSegs = PseudoTokenizer.Tokenize(source);
        var trSegs = PseudoTokenizer.Tokenize(translation);

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
