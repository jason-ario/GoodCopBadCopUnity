using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public enum PseudoSegmentKind { Text, LineBreak, Escape, Tag, Placeholder, Technical, Input, Numeric }

public struct PseudoSegment
{
    public PseudoSegmentKind kind;
    public string text;

    public bool IsProtected { get { return kind != PseudoSegmentKind.Text; } }

    /// <summary>Visible character count: text, input identifiers, numerals and technical literals count; syntax does not.</summary>
    public int Visible
    {
        get
        {
            switch (kind)
            {
                case PseudoSegmentKind.Text:
                case PseudoSegmentKind.Input:
                case PseudoSegmentKind.Numeric:
                case PseudoSegmentKind.Technical:
                    return PseudoTokenizer.CountVisible(text);
                default:
                    return 0;
            }
        }
    }
}

/// <summary>Shared tokenizer: splits a string into protected segments and localizable text. Uncertain means preserve.</summary>
public static class PseudoTokenizer
{
    private static readonly Regex Pattern = new Regex(
        @"(?<br>\r\n|\n|\r)" +
        @"|(?<esc>\\[nrt\\])" +
        @"|(?<tag>\[\[|\]\]|</?[A-Za-z][^<>\r\n]*>|<#[0-9A-Fa-f]{3,8}>|\[/?[A-Za-z][A-Za-z0-9_]*(?:=[^\]\r\n]*)?\])" +
        @"|(?<ph>\{\w+\s*,\s*(?:plural|select|selectordinal)\s*,(?:[^{}]|\{[^{}]*\})*\}|\$\{[^}\r\n]*\}|\{\{[^}\r\n]*\}\}|\{[A-Za-z0-9_.:,#\- ]*\}|%%|%(?:\d+\$)?[-+0#]*\d*(?:\.\d+)?[sdfiuxXcg])" +
        @"|(?<tech>https?://\S+|www\.\S+|(?<![\w/])/(?:[\w.\-]+/)+[\w.\-]*|\b[\w\-]+\.(?:png|jpg|json|txt|csv|cs|prefab|asset|unity|wav|mp3|ogg)\b)" +
        @"|(?<inp>\b(?:Esc|Ctrl|Alt|Shift|Tab|Enter|Backspace|LMB|RMB|MMB|F\d{1,2})\b|\b[ABXY](?:/[ABXY])+\b|(?<=\b(?:Press|press|Hold|hold|PRESS|HOLD)\s)[A-Z]\b)" +
        @"|(?<num>\d+(?:[.,:/]\d+)*)");

    public static List<PseudoSegment> Tokenize(string s)
    {
        var list = new List<PseudoSegment>();
        if (string.IsNullOrEmpty(s)) return list;

        int pos = 0;
        foreach (Match m in Pattern.Matches(s))
        {
            if (m.Length == 0) continue;
            if (m.Index > pos)
                list.Add(new PseudoSegment { kind = PseudoSegmentKind.Text, text = s.Substring(pos, m.Index - pos) });
            list.Add(new PseudoSegment { kind = Classify(m), text = m.Value });
            pos = m.Index + m.Length;
        }
        if (pos < s.Length)
            list.Add(new PseudoSegment { kind = PseudoSegmentKind.Text, text = s.Substring(pos) });
        return list;
    }

    private static PseudoSegmentKind Classify(Match m)
    {
        if (m.Groups["br"].Success) return PseudoSegmentKind.LineBreak;
        if (m.Groups["esc"].Success) return PseudoSegmentKind.Escape;
        if (m.Groups["tag"].Success) return PseudoSegmentKind.Tag;
        if (m.Groups["ph"].Success) return PseudoSegmentKind.Placeholder;
        if (m.Groups["tech"].Success) return PseudoSegmentKind.Technical;
        if (m.Groups["inp"].Success) return PseudoSegmentKind.Input;
        return PseudoSegmentKind.Numeric;
    }

    public static int CountVisible(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int n = 0;
        foreach (char c in s)
        {
            if (c == '\r' || c == '\n' || char.IsLowSurrogate(c)) continue;
            n++;
        }
        return n;
    }

    public static int VisibleLength(List<PseudoSegment> segs)
    {
        int n = 0;
        foreach (var s in segs) n += s.Visible;
        return n;
    }

    public static int VisibleLength(string s) { return VisibleLength(Tokenize(s)); }

    /// <summary>True when the localizable text contains at least one letter. Otherwise the string is returned unchanged.</summary>
    public static bool HasLocalizableLetters(List<PseudoSegment> segs)
    {
        foreach (var s in segs)
        {
            if (s.kind != PseudoSegmentKind.Text) continue;
            foreach (char c in s.text) if (char.IsLetter(c)) return true;
        }
        return false;
    }
}

public interface IPseudoLocale
{
    string Code { get; }
    string DisplayName { get; }
    string Generate(string source, string key, float expansion);
    /// <summary>Re-verifies the manager's QC list; returns human readable violations (empty when clean).</summary>
    List<string> Verify(string source, string output, string key, float expansion);
    /// <summary>Every character this locale may emit, for the restricted-character probe.</summary>
    string ApprovedCharacters { get; }
}

/// <summary>Registry of pluggable pseudolocales. Register new ones (for example Georgian) with <see cref="Register"/>.</summary>
public static class PseudoLocalizer
{
    private static readonly Dictionary<string, IPseudoLocale> Locales = new Dictionary<string, IPseudoLocale>();

    static PseudoLocalizer()
    {
        Register(new LatinCyrillicPseudoLocale());
        Register(new CjkPseudoLocale());
    }

    public static void Register(IPseudoLocale locale) { Locales[locale.Code] = locale; }
    public static bool IsPseudo(string code) { return code != null && Locales.ContainsKey(code); }
    public static IEnumerable<IPseudoLocale> All { get { return Locales.Values; } }
    public static IEnumerable<string> Codes { get { return Locales.Keys; } }

    public static IPseudoLocale Get(string code)
    {
        Locales.TryGetValue(code ?? "", out var l);
        return l;
    }

    public static string Generate(string code, string source, string key, float expansion)
    {
        var l = Get(code);
        return l == null ? source : l.Generate(source, key, expansion);
    }

    /// <summary>ceil(visible x expansion), rounded to avoid float noise (5 x 0.4 must be exactly 2).</summary>
    public static int Extra(int visible, float expansion)
    {
        return (int)Math.Ceiling(Math.Round(visible * (double)expansion, 6));
    }
}

public sealed class LatinCyrillicPseudoLocale : IPseudoLocale
{
    public string Code { get { return "pseudo-latincyr"; } }
    public string DisplayName { get { return "Pseudo Latin Extended / Cyrillic"; } }

    private const char Open = '\u27E6';
    private const char Close = '\u27E7';

    private static readonly Dictionary<char, string> Latin = new Dictionary<char, string>
    {
        {'a', "\u00E5\u00E1\u00E4\u00E6"}, {'c', "\u00E7"}, {'e', "\u00EB\u00E9"}, {'i', "\u00EF\u00ED"},
        {'n', "\u00F1"}, {'o', "\u00F6\u00F3\u00F8"}, {'s', "\u0161"}, {'t', "\u0163"},
        {'u', "\u00FC\u00FA"}, {'y', "\u00FF"}, {'z', "\u017E"},
        {'A', "\u00C5\u00C1\u00C4\u00C6"}, {'C', "\u00C7"}, {'E', "\u00CB\u00C9"}, {'I', "\u00CF\u00CD"},
        {'N', "\u00D1"}, {'O', "\u00D6\u00D3\u00D8"}, {'S', "\u0160"}, {'T', "\u0162"},
        {'U', "\u00DC\u00DA"}, {'Y', "\u0178"}, {'Z', "\u017D"}
    };

    // Approved homoglyphs only: A B C E H K M O P T X Y and a c e o p x y.
    private static readonly Dictionary<char, char> Cyrillic = new Dictionary<char, char>
    {
        {'A', '\u0410'}, {'B', '\u0412'}, {'C', '\u0421'}, {'E', '\u0415'}, {'H', '\u041D'}, {'K', '\u041A'},
        {'M', '\u041C'}, {'O', '\u041E'}, {'P', '\u0420'}, {'T', '\u0422'}, {'X', '\u0425'}, {'Y', '\u0423'},
        {'a', '\u0430'}, {'c', '\u0441'}, {'e', '\u0435'}, {'o', '\u043E'}, {'p', '\u0440'}, {'x', '\u0445'}, {'y', '\u0443'}
    };

    public string ApprovedCharacters
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Open).Append(Close).Append('_');
            foreach (var kv in Latin) sb.Append(kv.Value);
            foreach (var kv in Cyrillic) sb.Append(kv.Value);
            return sb.ToString();
        }
    }

    public string Generate(string source, string key, float expansion)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var segs = PseudoTokenizer.Tokenize(source);
        if (!PseudoTokenizer.HasLocalizableLetters(segs)) return source;

        int visible = PseudoTokenizer.VisibleLength(segs);
        var sb = new StringBuilder(source.Length + 8);
        sb.Append(Open);

        int dualCounter = 0;
        bool cyrPossible = false, cyrProduced = false;
        int lastDualPos = -1;
        char lastDualChar = '\0';
        var variantCount = new Dictionary<char, int>();

        foreach (var seg in segs)
        {
            if (seg.IsProtected) { sb.Append(seg.text); continue; }
            foreach (char c in seg.text)
            {
                bool hasL = Latin.TryGetValue(c, out var variants);
                bool hasC = Cyrillic.TryGetValue(c, out var cy);
                if (hasL && hasC)
                {
                    cyrPossible = true;
                    if (dualCounter % 2 == 0) sb.Append(PickVariant(variantCount, c, variants));
                    else { sb.Append(cy); cyrProduced = true; }
                    dualCounter++;
                    lastDualPos = sb.Length - 1;
                    lastDualChar = c;
                }
                else if (hasC) { cyrPossible = true; cyrProduced = true; sb.Append(cy); }
                else if (hasL) sb.Append(PickVariant(variantCount, c, variants));
                else sb.Append(c);
            }
        }

        if (cyrPossible && !cyrProduced && lastDualPos >= 0)
            sb[lastDualPos] = Cyrillic[lastDualChar];

        sb.Append('_', PseudoLocalizer.Extra(visible, expansion));
        sb.Append(Close);
        return sb.ToString();
    }

    private static char PickVariant(Dictionary<char, int> counts, char c, string variants)
    {
        counts.TryGetValue(c, out int n);
        counts[c] = n + 1;
        return variants[n % variants.Length];
    }

    public List<string> Verify(string source, string output, string key, float expansion)
    {
        var issues = new List<string>();
        if (string.IsNullOrEmpty(source)) return issues;

        var srcSegs = PseudoTokenizer.Tokenize(source);
        if (!PseudoTokenizer.HasLocalizableLetters(srcSegs))
        {
            if (output != source) issues.Add("Protected-only string was modified.");
            return issues;
        }

        if (output.Length < 2 || output[0] != Open || output[output.Length - 1] != Close)
        {
            issues.Add("Missing wrapper characters.");
            return issues;
        }

        string inner = output.Substring(1, output.Length - 2);
        int visible = PseudoTokenizer.VisibleLength(srcSegs);
        int extra = PseudoLocalizer.Extra(visible, expansion);
        if (inner.Length < extra || inner.Substring(inner.Length - extra) != new string('_', extra))
        {
            issues.Add("Padding is not exactly " + extra + " underscores.");
            return issues;
        }
        inner = inner.Substring(0, inner.Length - extra);

        // Substitution is 1:1 per character, so walk the output using the source segment lengths.
        // (Re-tokenizing the output is not reliable: context-sensitive rules like "Press E" depend on the original letters.)
        int total = 0;
        foreach (var s in srcSegs) total += s.text.Length;
        if (total != inner.Length)
        {
            issues.Add("Length before padding changed (" + inner.Length + " vs " + total + ").");
            return issues;
        }

        bool cyrEligible = false, cyrSeen = false;
        int pos = 0;
        for (int i = 0; i < srcSegs.Count; i++)
        {
            var a = srcSegs[i];
            var b = new PseudoSegment { kind = a.kind, text = inner.Substring(pos, a.text.Length) };
            pos += a.text.Length;
            if (a.IsProtected)
            {
                if (a.text != b.text) issues.Add("Protected content altered: '" + a.text + "'.");
                continue;
            }
            for (int c = 0; c < a.text.Length; c++)
            {
                char o = a.text[c], p = b.text[c];
                if (Cyrillic.ContainsKey(o)) cyrEligible = true;
                if (Cyrillic.TryGetValue(o, out var cy) && p == cy) cyrSeen = true;
                bool ok = p == o
                    || (Latin.TryGetValue(o, out var lv) && lv.IndexOf(p) >= 0)
                    || (Cyrillic.TryGetValue(o, out var cc) && cc == p);
                if (!ok) { issues.Add("Unapproved substitution '" + o + "' -> '" + p + "'."); break; }
            }
        }
        if (cyrEligible && !cyrSeen) issues.Add("Cyrillic was possible but none was produced.");
        return issues;
    }
}

public sealed class CjkPseudoLocale : IPseudoLocale
{
    public string Code { get { return "pseudo-cjk"; } }
    public string DisplayName { get { return "Pseudo CJK"; } }

    private const string SharedHan = "\u4EBA\u5927\u4E2D\u65E5\u6708\u6C34\u706B\u5C71\u6728\u571F\u91D1\u5929\u5730\u4E0A\u4E0B\u5FC3\u624B\u53E3\u76EE\u7530\u529B\u767D\u77F3\u82B1";
    private const string SimplifiedHan = "\u56FD\u5B66\u95E8\u8F66\u9A6C\u9E1F\u9C7C\u89C1\u4E66\u52A8\u673A\u7535\u8BDD\u65F6\u95F4\u540E\u53D1\u5BF9\u8BF4\u8FD9";
    private const string TraditionalHan = "\u570B\u5B78\u9580\u8ECA\u99AC\u9CE5\u9B5A\u898B\u66F8\u52D5\u6A5F\u96FB\u8A71\u6642\u9593\u5F8C\u767C\u5C0D\u8AAA\u9019";
    private const string RareHan = "\u9F98\u7228\u9B31\u9A6B\u9EA4\u9F49\u7065\u7AC8\u8B1F\u7E9B";
    private const string Hiragana = "\u3042\u3044\u3046\u3048\u304A\u304B\u304D\u304F\u3051\u3053\u3055\u3057\u3059\u305B\u305D";
    private const string Katakana = "\u30A2\u30A4\u30A6\u30A8\u30AA\u30AB\u30AD\u30AF\u30B1\u30B3\u30B5\u30B7\u30B9\u30BB\u30BD";
    private const string Hangul = "\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14\uC0AC\uC544\uC790\uCC28\uCE74\uD0C0\uD30C\uD558";
    private const string Ascii = "ABCXYZabcxyz";
    private const string FullWidth = "\uFF21\uFF22\uFF23\uFF38\uFF39\uFF3A\uFF41\uFF42\uFF43\uFF58\uFF59\uFF5A\uFF10\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17\uFF18\uFF19";
    private const string Punct = "\u3001\u3002\uFF0C\uFF1A\uFF1B\uFF01\uFF1F\u300C\u300D\u300E\u300F\uFF08\uFF09\u301C\u30FB";

    public string ApprovedCharacters
    {
        get { return SharedHan + SimplifiedHan + TraditionalHan + RareHan + Hiragana + Katakana + Hangul + Ascii + FullWidth + Punct; }
    }

    private enum Cat { Shared, Simplified, Traditional, Rare, Kana, Hangul, Ascii, FullWidth, Punct }

    private sealed class Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 2463534242u : seed; }
        public int Next(int max)
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (int)(_s % (uint)max);
        }
    }

    private static uint SeedOf(string s)
    {
        uint h = 2166136261u;
        if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h;
    }

    private static bool IsHan(Cat c)
    {
        return c == Cat.Shared || c == Cat.Simplified || c == Cat.Traditional || c == Cat.Rare;
    }

    private static char Pick(Cat cat, Rng rng)
    {
        string pool;
        switch (cat)
        {
            case Cat.Shared: pool = SharedHan; break;
            case Cat.Simplified: pool = SimplifiedHan; break;
            case Cat.Traditional: pool = TraditionalHan; break;
            case Cat.Rare: pool = RareHan; break;
            case Cat.Kana: pool = rng.Next(2) == 0 ? Hiragana : Katakana; break;
            case Cat.Hangul: pool = Hangul; break;
            case Cat.Ascii: pool = Ascii; break;
            case Cat.FullWidth: pool = FullWidth; break;
            default: pool = Punct; break;
        }
        return pool[rng.Next(pool.Length)];
    }

    private static Cat HanMix(Rng rng)
    {
        int r = rng.Next(3);
        return r == 0 ? Cat.Shared : r == 1 ? Cat.Simplified : Cat.Traditional;
    }

    /// <summary>Builds exactly <paramref name="n"/> characters; composition depends on the source's visible length.</summary>
    private static string BuildContent(int n, int sourceLength, string seedKey)
    {
        var rng = new Rng(SeedOf(seedKey));
        var cats = new List<Cat>(n);

        if (sourceLength <= 4)
        {
            for (int i = 0; i < n; i++) cats.Add(HanMix(rng));
        }
        else if (sourceLength <= 12)
        {
            var others = new List<Cat> { Cat.Kana, Cat.Hangul, Cat.Ascii, Cat.FullWidth };
            int hanCount = Math.Max(1, (int)Math.Round(n * 0.6));
            for (int i = 0; i < hanCount && cats.Count < n; i++) cats.Add(HanMix(rng));
            int offset = rng.Next(others.Count);
            int k = 0;
            while (cats.Count < n) cats.Add(others[(offset + k++) % others.Count]);
            if (sourceLength >= 8 && n >= 4) cats[cats.Count - 1] = Cat.Punct;
        }
        else
        {
            var required = new[] { Cat.Shared, Cat.Simplified, Cat.Traditional, Cat.Rare, Cat.Kana, Cat.Hangul, Cat.Ascii, Cat.FullWidth, Cat.Punct };
            foreach (var c in required) if (cats.Count < n) cats.Add(c);
            var extras = new List<Cat> { Cat.Kana, Cat.Hangul, Cat.Ascii, Cat.FullWidth, Cat.Rare };
            int k = 0;
            while (cats.Count < n)
                cats.Add(rng.Next(10) < 6 ? HanMix(rng) : extras[k++ % extras.Count]);
        }

        for (int i = cats.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var t = cats[i]; cats[i] = cats[j]; cats[j] = t;
        }
        if (cats.Count > 0 && !IsHan(cats[0]))
        {
            int idx = cats.FindIndex(IsHan);
            if (idx > 0) { var t = cats[0]; cats[0] = cats[idx]; cats[idx] = t; }
        }

        var sb = new StringBuilder(n);
        foreach (var c in cats) sb.Append(Pick(c, rng));
        return sb.ToString();
    }

    private struct Piece
    {
        public bool isCore;
        public string text; // protected text, whitespace, or (for cores) the original core
    }

    private static List<Piece> Plan(List<PseudoSegment> segs, out int fixedVisible, out int coreTotal)
    {
        var pieces = new List<Piece>();
        fixedVisible = 0;
        coreTotal = 0;
        foreach (var s in segs)
        {
            if (s.IsProtected)
            {
                pieces.Add(new Piece { isCore = false, text = s.text });
                fixedVisible += s.Visible;
                continue;
            }
            string t = s.text;
            int a = 0, b = t.Length;
            while (a < b && char.IsWhiteSpace(t[a])) a++;
            while (b > a && char.IsWhiteSpace(t[b - 1])) b--;
            if (a > 0) { pieces.Add(new Piece { text = t.Substring(0, a) }); fixedVisible += a; }
            if (b > a)
            {
                string core = t.Substring(a, b - a);
                pieces.Add(new Piece { isCore = true, text = core });
                coreTotal += PseudoTokenizer.CountVisible(core);
            }
            if (b < t.Length) { pieces.Add(new Piece { text = t.Substring(b) }); fixedVisible += t.Length - b; }
        }
        return pieces;
    }

    public string Generate(string source, string key, float expansion)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var segs = PseudoTokenizer.Tokenize(source);
        if (!PseudoTokenizer.HasLocalizableLetters(segs)) return source;

        int visible = PseudoTokenizer.VisibleLength(segs);
        int target = visible + PseudoLocalizer.Extra(visible, expansion);

        var pieces = Plan(segs, out int fixedVisible, out int coreTotal);
        int gen = target - fixedVisible;
        var coreIdx = new List<int>();
        for (int i = 0; i < pieces.Count; i++) if (pieces[i].isCore) coreIdx.Add(i);
        if (coreIdx.Count == 0 || gen < coreIdx.Count) return source;

        // Proportional allocation with largest remainder; every core gets at least one character.
        var shares = new int[coreIdx.Count];
        var rema = new double[coreIdx.Count];
        int assigned = 0;
        for (int i = 0; i < coreIdx.Count; i++)
        {
            double exact = gen * (double)PseudoTokenizer.CountVisible(pieces[coreIdx[i]].text) / Math.Max(1, coreTotal);
            shares[i] = Math.Max(1, (int)Math.Floor(exact));
            rema[i] = exact - Math.Floor(exact);
            assigned += shares[i];
        }
        while (assigned < gen)
        {
            int best = 0;
            for (int i = 1; i < rema.Length; i++) if (rema[i] > rema[best]) best = i;
            shares[best]++; rema[best] -= 1; assigned++;
        }
        while (assigned > gen)
        {
            int best = 0;
            for (int i = 1; i < shares.Length; i++) if (shares[i] > shares[best]) best = i;
            if (shares[best] <= 1) break;
            shares[best]--; assigned--;
        }

        string content = BuildContent(gen, visible, string.IsNullOrEmpty(key) ? source : key);
        var sb = new StringBuilder(target + 8);
        int cursor = 0, ci = 0;
        foreach (var p in pieces)
        {
            if (!p.isCore) { sb.Append(p.text); continue; }
            sb.Append(content, cursor, shares[ci]);
            cursor += shares[ci];
            ci++;
        }
        return sb.ToString();
    }

    public List<string> Verify(string source, string output, string key, float expansion)
    {
        var issues = new List<string>();
        if (string.IsNullOrEmpty(source)) return issues;
        var srcSegs = PseudoTokenizer.Tokenize(source);
        if (!PseudoTokenizer.HasLocalizableLetters(srcSegs))
        {
            if (output != source) issues.Add("Protected-only string was modified.");
            return issues;
        }

        int visible = PseudoTokenizer.VisibleLength(srcSegs);
        int target = visible + PseudoLocalizer.Extra(visible, expansion);
        var outSegs = PseudoTokenizer.Tokenize(output);
        int got = PseudoTokenizer.VisibleLength(outSegs);
        if (got != target) issues.Add("Visible length " + got + " != expected " + target + ".");

        if (output.IndexOf('\u27E6') >= 0 || output.IndexOf('\u27E7') >= 0) issues.Add("Wrapper characters present.");

        int srcBreaks = 0, outBreaks = 0;
        foreach (char c in source) if (c == '\n') srcBreaks++;
        foreach (char c in output) if (c == '\n') outBreaks++;
        if (srcBreaks != outBreaks) issues.Add("Line break count changed.");

        // Remove source protected strings in order; what remains must be approved characters and fixed whitespace only.
        string remainder = output;
        int fixedWhitespace = 0;
        var pieces = Plan(srcSegs, out int _, out int __);
        foreach (var p in pieces)
        {
            if (p.isCore) continue;
            if (p.text.Length > 0 && p.text.Trim().Length == 0 && !p.text.Contains("\n") && !p.text.Contains("\r")) { fixedWhitespace += p.text.Length; continue; }
            int idx = remainder.IndexOf(p.text, StringComparison.Ordinal);
            if (idx < 0) { issues.Add("Protected content missing or altered: '" + p.text + "'."); continue; }
            remainder = remainder.Remove(idx, p.text.Length);
        }

        string approved = ApprovedCharacters;
        int ws = 0;
        foreach (char c in remainder)
        {
            if (char.IsWhiteSpace(c)) { ws++; continue; }
            if (c >= '\uE000' && c <= '\uF8FF') { issues.Add("Private Use Area character U+" + ((int)c).ToString("X4") + "."); break; }
            if (approved.IndexOf(c) < 0) { issues.Add("Unapproved character U+" + ((int)c).ToString("X4") + " '" + c + "'."); break; }
        }
        if (ws != fixedWhitespace) issues.Add("Generated content contains spaces.");
        return issues;
    }
}
