using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GoodCopBadCop.Infrastructure.Persistence;
using UnityEngine;

/// <summary>Deterministic key derivation for ScriptableObject text, shared by the scanner and runtime lookups.</summary>
public static class LocKeys
{
    public static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool lastUnderscore = false;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '.')
            {
                sb.Append(char.ToLowerInvariant(c));
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }
        return sb.ToString().Trim('_');
    }

    /// <summary>"nodes.Array.data[2].choices.Array.data[0].npcResponse" becomes "nodes.2.choices.0.npcresponse".</summary>
    public static string PathToKeyPart(string propertyPath)
    {
        string p = (propertyPath ?? "").Replace(".Array.data[", ".").Replace("]", "");
        return Sanitize(p);
    }

    public static string PrefixFor(UnityEngine.Object owner)
    {
        if (owner is SuspectData || owner is ScriptedDialogue) return "dialogue";
        return Sanitize(owner.GetType().Name);
    }

    /// <summary>
    /// Key for a string field on a ScriptableObject (dialogue.nona.nodes.2.choices.0.npcresponse) or on a component
    /// of a suspect object (dialogue.nona.suspectworlddialogue.greetingline). "(Clone)" is stripped from object names.
    /// </summary>
    public static string ForAsset(UnityEngine.Object owner, string propertyPath)
    {
        if (owner is Component comp)
        {
            string goName = comp.gameObject.name.Replace("(Clone)", "").Trim();
            return "dialogue." + Sanitize(goName) + "." + Sanitize(comp.GetType().Name) + "." + PathToKeyPart(propertyPath);
        }
        return PrefixFor(owner) + "." + Sanitize(owner.name) + "." + PathToKeyPart(propertyPath);
    }

    /// <summary>
    /// Key for an English literal wrapped in <see cref="Loc.T(string)"/>: "code." + readable slug + stable hash of the
    /// full text, e.g. "code.go_to_the_booth_to_start_your_shift.3f1a9c2e". Editing the literal yields a new key.
    /// </summary>
    public static string ForCode(string english)
    {
        string slug = Sanitize(english ?? "").Replace('.', '_');
        if (slug.Length > 40) slug = slug.Substring(0, 40).TrimEnd('_');
        uint h = 2166136261;
        foreach (char c in english ?? "") { h ^= c; h *= 16777619; }
        return "code." + (slug.Length > 0 ? slug + "." : "") + h.ToString("x8");
    }
}

/// <summary>Static localization entry point. Fallback chain: current language, English source, the key itself.</summary>
public static class Loc
{
    public const string LanguageKey = "settings.language";
    public const string SourceLanguage = LocalizationTable.SourceLanguage;
    public const string ResourcePath = "Localization/strings";

    public static event Action LanguageChanged;

    /// <summary>
    /// Zero-width space (TMP renders it with no width) prefixed to pseudolocalized and verbatim strings. Render-time
    /// localization (<see cref="LocalizeRendered"/>) leaves any text containing it alone, so nothing is processed twice.
    /// </summary>
    public const string Marker = "\u200B";

    private static LocalizationTable _table;
    private static string _language;
    private static bool _initialized;
    private static readonly HashSet<string> Warned = new HashSet<string>();
    private static readonly Dictionary<string, string> PseudoCache = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> RenderCache = new Dictionary<string, string>();
    private const int RenderCacheLimit = 4096;
    private static Dictionary<string, List<LocEntry>> _sourceIndex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _table = null;
        _language = null;
        _initialized = false;
        _sourceIndex = null;
        Warned.Clear();
        PseudoCache.Clear();
        RenderCache.Clear();
        LocalizationSettings.ClearCache();
        LanguageChanged = null;
    }

    /// <summary>
    /// Marks an English literal for the Localization window's code scan without translating it (like gettext's N_).
    /// Use for static definitions that are translated later at display time with <see cref="T(string)"/>.
    /// </summary>
    public static string N(string english) { return english; }

    /// <summary>Text that must be shown exactly as given (device names, language names); render-time localization skips it.</summary>
    public static string Verbatim(string text)
    {
        if (string.IsNullOrEmpty(text) || text.StartsWith(Marker, StringComparison.Ordinal)) return text;
        return Marker + text;
    }

    /// <summary>
    /// Render-time localization applied to every TMP text by <c>LocalizationTextRuntime</c> (an ITextPreprocessor), so
    /// text set from code or left untouched by a missing key still follows the language. English and font-only previews
    /// return the text unchanged, pseudolocales pseudolocalize it, and real languages reverse-look-up the English source.
    /// Text containing <see cref="Marker"/> (already localized or verbatim) is returned unchanged.
    /// </summary>
    public static string LocalizeRendered(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        Init();
        if (_language == SourceLanguage || text.IndexOf(Marker, StringComparison.Ordinal) >= 0) return text;

        bool pseudo = IsPseudoAvailable(_language);
        if (!pseudo && !_table.languages.Contains(_language)) return text; // font-only preview

        if (RenderCache.TryGetValue(text, out string cached)) return cached;
        string result = pseudo ? PseudoOf(text) : TranslateSource(text);
        if (RenderCache.Count >= RenderCacheLimit) RenderCache.Clear();
        RenderCache[text] = result;
        return result;
    }

    public static LocalizationTable Table { get { Init(); return _table; } }

    public static string CurrentLanguage { get { Init(); return _language; } }

    public static bool PseudoAvailable
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Whether a pseudolocale may be selected. The Settings-menu pseudolocale (<see cref="PseudoLocalizer.SettingsMenuCode"/>)
    /// follows <c>GameSettings.PseudoLocalizationEnabled</c> in every build; the other pseudolocales are Editor/dev only.
    /// </summary>
    public static bool IsPseudoAvailable(string code)
    {
        if (!PseudoLocalizer.IsPseudo(code)) return false;
        if (code == PseudoLocalizer.SettingsMenuCode) return GameSettings.Instance.PseudoLocalizationEnabled;
        return PseudoAvailable;
    }

    /// <summary>Falls back to the default language when the current one stopped being selectable (pseudolocalization disabled).</summary>
    public static void RevalidateLanguage()
    {
        Init();
        if (IsValid(_language)) return;
        var settings = LocalizationSettings.Instance;
        string fallback = settings != null && !string.IsNullOrEmpty(settings.defaultLanguage) ? settings.defaultLanguage : SourceLanguage;
        SetLanguage(IsValid(fallback) ? fallback : SourceLanguage);
    }

    public static CultureInfo Culture
    {
        get
        {
            Init();
            if (PseudoLocalizer.IsPseudo(_language) || IsFontPreview(_language)) return CultureInfo.InvariantCulture;
            try { return new CultureInfo(_language); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }

    /// <summary>All selectable language codes: source, imported languages, configured languages and (dev only) pseudolocales.</summary>
    public static List<string> AvailableLanguages()
    {
        Init();
        var list = new List<string> { SourceLanguage };
        foreach (var l in _table.languages) if (!list.Contains(l) && !PseudoLocalizer.IsPseudo(l)) list.Add(l);
        var settings = LocalizationSettings.Instance;
        if (settings != null)
            foreach (var l in settings.languages) if (l != null && !list.Contains(l.code)) list.Add(l.code);
        foreach (var code in PseudoLocalizer.Codes) if (IsPseudoAvailable(code) && !list.Contains(code)) list.Add(code);
        return list;
    }

    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        LoadTable();

        var settings = LocalizationSettings.Instance;
        string fallback = settings != null && !string.IsNullOrEmpty(settings.defaultLanguage) ? settings.defaultLanguage : SourceLanguage;
        string saved = fallback;
        try { saved = PlayerPrefsPersistentPropertyStorage.Shared.Load(LanguageKey, fallback); }
        catch (Exception) { saved = fallback; }

        _language = IsValid(saved) ? saved : fallback;
    }

    private static bool IsValid(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (code == SourceLanguage) return true;
        if (PseudoLocalizer.IsPseudo(code)) return IsPseudoAvailable(code);
        if (_table.languages.Contains(code)) return true;
        var settings = LocalizationSettings.Instance;
        return settings != null && settings.Find(code) != null;
    }

    /// <summary>
    /// True for a language configured in <see cref="LocalizationSettings"/> that has no translation column yet.
    /// It shows English text with that language's fonts (a font-only pseudolocale), without missing-text warnings.
    /// </summary>
    public static bool IsFontPreview(string code)
    {
        Init();
        return !string.IsNullOrEmpty(code) && code != SourceLanguage && !PseudoLocalizer.IsPseudo(code)
               && !_table.languages.Contains(code);
    }

    /// <summary>Display name from LocalizationSettings (for example "Русский"), the pseudolocale's name, or the code itself.</summary>
    public static string DisplayName(string code)
    {
        var pseudo = PseudoLocalizer.Get(code);
        if (pseudo != null) return pseudo.DisplayName;
        var lang = LocalizationSettings.Instance != null ? LocalizationSettings.Instance.Find(code) : null;
        return lang != null && !string.IsNullOrEmpty(lang.displayName) ? lang.displayName : code;
    }

    private static void LoadTable()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        _table = asset != null ? LocalizationTable.FromRuntimeCsv(asset.text) : new LocalizationTable();
        if (asset == null)
            WarnOnce("no-table", "[Loc] No runtime table at Resources/" + ResourcePath + ".txt (Tools/Localization > Build runtime table). " +
                                 "LocalizedText keys can't resolve, so labels keep their authored text; pseudolocalization and " +
                                 "language fonts still apply at render time.");
        _sourceIndex = null;
        PseudoCache.Clear();
        RenderCache.Clear();
    }

    /// <summary>Reloads the generated table (used by the editor tool after a build).</summary>
    public static void Reload()
    {
        _initialized = false;
        Warned.Clear();
        LocalizationSettings.ClearCache();
        Init();
        LanguageChanged?.Invoke();
    }

    public static void SetLanguage(string code)
    {
        Init();
        if (!IsValid(code))
        {
            Debug.LogWarning("[Loc] Unknown or unavailable language '" + code + "'.");
            return;
        }
        if (code == _language) return;

        _language = code;
        RenderCache.Clear();
        try
        {
            PlayerPrefsPersistentPropertyStorage.Shared.Save(LanguageKey, code);
            PlayerPrefsPersistentPropertyStorage.Shared.Flush();
        }
        catch (Exception e) { Debug.LogWarning("[Loc] Could not persist language: " + e.Message); }
        LanguageChanged?.Invoke();
    }

    public static bool Has(string key)
    {
        Init();
        return !string.IsNullOrEmpty(key) && _table.TryGet(key, out _);
    }

    public static string Get(string key)
    {
        return TryGet(key, out string text) ? text : key;
    }

    public static string Get(string key, params object[] args)
    {
        string text = Get(key);
        if (args == null || args.Length == 0) return text;
        try { return string.Format(Culture, text, args); }
        catch (FormatException)
        {
            WarnOnce("fmt:" + key, "[Loc] Bad format string for key '" + key + "' in '" + _language + "'.");
            return text;
        }
    }

    /// <summary>Resolves a key. Returns false when the key does not exist at all (text is then null).</summary>
    public static bool TryGet(string key, out string text)
    {
        Init();
        text = null;
        if (string.IsNullOrEmpty(key)) return false;
        if (!_table.TryGet(key, out var entry))
        {
            // With no runtime table at all, LoadTable already logged one warning; don't add one per label.
            if (_table.entries.Count > 0) WarnOnce("missing:" + key, "[Loc] Missing key '" + key + "'.");
            return false;
        }
        text = Resolve(entry, true);
        return true;
    }

    private static string Resolve(LocEntry entry, bool warn)
    {
        if (_language == SourceLanguage) return entry.source;

        // Font-only pseudolocale: no column for this language yet, so keep English and let the font swap show.
        if (!_table.languages.Contains(_language) && !PseudoLocalizer.IsPseudo(_language)) return entry.source;

        if (PseudoLocalizer.IsPseudo(_language))
        {
            string cacheKey = _language + "\u0001" + entry.key;
            if (!PseudoCache.TryGetValue(cacheKey, out string pseudo))
            {
                var settings = LocalizationSettings.Instance;
                float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
                pseudo = Marker + PseudoLocalizer.Generate(_language, entry.source, entry.key, expansion);
                PseudoCache[cacheKey] = pseudo;
            }
            return pseudo;
        }

        string value = entry.GetValue(_language);
        if (!string.IsNullOrEmpty(value)) return value;

        if (warn) WarnOnce("untranslated:" + _language + ":" + entry.key, "[Loc] '" + entry.key + "' has no '" + _language + "' text; using English.");
        return entry.source;
    }

    /// <summary>
    /// Translates a ScriptableObject string field. English (or a missing key) returns the field's own text,
    /// so the asset stays the source of truth.
    /// </summary>
    public static string GetFor(UnityEngine.Object owner, string propertyPath, string english)
    {
        Init();
        if (owner == null || string.IsNullOrEmpty(english) || _language == SourceLanguage) return english;
        if (_table.TryGet(LocKeys.ForAsset(owner, propertyPath), out var entry))
            return Resolve(entry, false);
        return IsPseudoAvailable(_language) ? PseudoOf(english) : english;
    }

    private static readonly Dictionary<string, string> CodeKeyCache = new Dictionary<string, string>();

    /// <summary>
    /// Translates player-facing text set from code. Pass the English as a plain string literal so the
    /// Localization window's "Code strings" scan can find it: <c>Loc.T("Go to the booth.")</c>.
    /// English (or a string not yet in the sheets) returns the literal itself.
    /// </summary>
    public static string T(string english)
    {
        Init();
        if (string.IsNullOrEmpty(english) || _language == SourceLanguage) return english;
        if (!CodeKeyCache.TryGetValue(english, out string key)) CodeKeyCache[english] = key = LocKeys.ForCode(english);
        if (_table.TryGet(key, out var entry)) return Resolve(entry, false);
        return IsPseudoAvailable(_language) ? PseudoOf(english) : english;
    }

    /// <summary>Like <see cref="T(string)"/>, then string.Format with the current culture: <c>Loc.T("Day {0}", day)</c>.</summary>
    public static string T(string english, params object[] args)
    {
        string text = T(english);
        if (args == null || args.Length == 0) return text;
        try { return string.Format(Culture, text, args); }
        catch (FormatException)
        {
            WarnOnce("fmt:" + english, "[Loc] Bad format string for code text '" + english + "' in '" + _language + "'.");
            return string.Format(CultureInfo.InvariantCulture, english, args);
        }
    }

    /// <summary>Sheet holding character names (keys <c>name.&lt;lowercase&gt;</c>). Excluded from source-text lookup.</summary>
    public const string NamesSheet = "Names";

    /// <summary>Key for a character name or name word: <c>name.</c> + lowercase, spaces as underscores.</summary>
    public static string NameKey(string englishName)
    {
        return "name." + englishName.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>
    /// Translates a character / speaker name for display (name tag, dialogue history, interact prompt). Tries the
    /// whole name first ("The Gambler"), then word by word ("Vladislav Syrnikov"). Returns the input unchanged unless
    /// every word is a known name, so ordinary text never gets partially translated. The Names sheet is deliberately
    /// excluded from render-time lookup: documents, paperwork mismatches and the PC terminal keep their source names.
    /// </summary>
    public static string Name(string english)
    {
        Init();
        if (string.IsNullOrWhiteSpace(english) || _language == SourceLanguage) return english;
        if (english.IndexOf(Marker, StringComparison.Ordinal) >= 0) return english;

        if (_table.TryGet(NameKey(english), out var whole)) return Resolve(whole, false);

        string[] words = english.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return english;
        var parts = new string[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            if (!_table.TryGet(NameKey(words[i]), out var entry)) return english;
            parts[i] = Resolve(entry, false);
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Search text for a character name: the English name plus its spelling in every table language (whole-name
    /// entry, else word by word). Independent of the current language, so replicated searches give identical
    /// results on every client.
    /// </summary>
    public static string NameSearchText(string english)
    {
        Init();
        if (string.IsNullOrWhiteSpace(english)) return english ?? string.Empty;
        var sb = new System.Text.StringBuilder(english);

        if (_table.TryGet(NameKey(english), out var whole))
        {
            AppendAllLanguages(sb, whole);
            return sb.ToString();
        }

        foreach (string word in english.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (_table.TryGet(NameKey(word), out var entry))
                AppendAllLanguages(sb, entry);
        return sb.ToString();
    }

    /// <summary>
    /// Search text for display copy shown through render-time localization (e.g. news headers): the English source
    /// plus every table language's translation of it. Language-independent, like <see cref="NameSearchText"/>.
    /// </summary>
    public static string SourceSearchText(string english)
    {
        Init();
        if (string.IsNullOrWhiteSpace(english)) return english ?? string.Empty;
        EnsureSourceIndex();
        var sb = new System.Text.StringBuilder(english);
        if (_sourceIndex.TryGetValue(english, out var matches) || _sourceIndex.TryGetValue(english.Trim(), out matches))
            if (matches.Count > 0) AppendAllLanguages(sb, matches[0]);
        return sb.ToString();
    }

    private static void AppendAllLanguages(System.Text.StringBuilder sb, LocEntry entry)
    {
        foreach (var value in entry.values.Values)
            if (!string.IsNullOrEmpty(value)) sb.Append(' ').Append(value);
    }

    private static void EnsureSourceIndex()
    {
        if (_sourceIndex != null) return;
        _sourceIndex = new Dictionary<string, List<LocEntry>>();
        foreach (var e in _table.entries)
        {
            if (string.IsNullOrEmpty(e.source) || e.sheet == NamesSheet) continue;
            if (!_sourceIndex.TryGetValue(e.source, out var list)) _sourceIndex[e.source] = list = new List<LocEntry>();
            list.Add(e);
        }
    }

    /// <summary>
    /// Looks up an English source string (used by <c>Subtitles.SetText</c>, before markup is applied).
    /// If several keys share the same source the first one wins. Unknown text is returned unchanged.
    /// </summary>
    public static string TranslateSource(string english)
    {
        Init();
        if (string.IsNullOrEmpty(english) || _language == SourceLanguage) return english;
        if (english.IndexOf(Marker, StringComparison.Ordinal) >= 0) return english; // already localized / verbatim

        EnsureSourceIndex();

        if (_sourceIndex.TryGetValue(english, out var matches) && matches.Count > 0)
            return Resolve(matches[0], false);

        // Authored labels often carry stray leading/trailing whitespace ("Ready Up ", "Case #02\n\n"): match the
        // trimmed text and keep the original padding around the translation.
        string trimmed = english.Trim();
        if (trimmed.Length > 0 && trimmed.Length != english.Length &&
            _sourceIndex.TryGetValue(trimmed, out matches) && matches.Count > 0)
        {
            int start = english.IndexOf(trimmed, StringComparison.Ordinal);
            return english.Substring(0, start) + Resolve(matches[0], false) + english.Substring(start + trimmed.Length);
        }

        return IsPseudoAvailable(_language) ? PseudoOf(english) : english;
    }

    private static string PseudoOf(string english)
    {
        var settings = LocalizationSettings.Instance;
        float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
        return Marker + PseudoLocalizer.Generate(_language, english, english, expansion);
    }

    private static void WarnOnce(string id, string message)
    {
        if (Warned.Add(id)) Debug.LogWarning(message);
    }
}
