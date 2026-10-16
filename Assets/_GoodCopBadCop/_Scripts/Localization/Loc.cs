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
}

/// <summary>Static localization entry point. Fallback chain: current language, English source, the key itself.</summary>
public static class Loc
{
    public const string LanguageKey = "settings.language";
    public const string SourceLanguage = LocalizationTable.SourceLanguage;
    public const string ResourcePath = "Localization/strings";

    public static event Action LanguageChanged;

    private static LocalizationTable _table;
    private static string _language;
    private static bool _initialized;
    private static readonly HashSet<string> Warned = new HashSet<string>();
    private static readonly Dictionary<string, string> PseudoCache = new Dictionary<string, string>();
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
        LocalizationSettings.ClearCache();
        LanguageChanged = null;
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

    public static CultureInfo Culture
    {
        get
        {
            Init();
            if (PseudoLocalizer.IsPseudo(_language)) return CultureInfo.InvariantCulture;
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
        if (PseudoAvailable)
            foreach (var code in PseudoLocalizer.Codes) if (!list.Contains(code)) list.Add(code);
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
        if (PseudoLocalizer.IsPseudo(code)) return PseudoAvailable;
        return _table.languages.Contains(code);
    }

    private static void LoadTable()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        _table = asset != null ? LocalizationTable.FromRuntimeCsv(asset.text) : new LocalizationTable();
        _sourceIndex = null;
        PseudoCache.Clear();
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
            WarnOnce("missing:" + key, "[Loc] Missing key '" + key + "'.");
            return false;
        }
        text = Resolve(entry, true);
        return true;
    }

    private static string Resolve(LocEntry entry, bool warn)
    {
        if (_language == SourceLanguage) return entry.source;

        if (PseudoLocalizer.IsPseudo(_language))
        {
            string cacheKey = _language + "\u0001" + entry.key;
            if (!PseudoCache.TryGetValue(cacheKey, out string pseudo))
            {
                var settings = LocalizationSettings.Instance;
                float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
                pseudo = PseudoLocalizer.Generate(_language, entry.source, entry.key, expansion);
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
        return PseudoLocalizer.IsPseudo(_language) && PseudoAvailable ? PseudoOf(english) : english;
    }

    /// <summary>
    /// Looks up an English source string (used by <c>Subtitles.SetText</c>, before markup is applied).
    /// If several keys share the same source the first one wins. Unknown text is returned unchanged.
    /// </summary>
    public static string TranslateSource(string english)
    {
        Init();
        if (string.IsNullOrEmpty(english) || _language == SourceLanguage) return english;

        if (_sourceIndex == null)
        {
            _sourceIndex = new Dictionary<string, List<LocEntry>>();
            foreach (var e in _table.entries)
            {
                if (string.IsNullOrEmpty(e.source)) continue;
                if (!_sourceIndex.TryGetValue(e.source, out var list)) _sourceIndex[e.source] = list = new List<LocEntry>();
                list.Add(e);
            }
        }

        if (_sourceIndex.TryGetValue(english, out var matches) && matches.Count > 0)
            return Resolve(matches[0], false);

        return PseudoLocalizer.IsPseudo(_language) && PseudoAvailable ? PseudoOf(english) : english;
    }

    private static string PseudoOf(string english)
    {
        var settings = LocalizationSettings.Instance;
        float expansion = settings != null ? settings.pseudoExpansion : 0.4f;
        return PseudoLocalizer.Generate(_language, english, english, expansion);
    }

    private static void WarnOnce(string id, string message)
    {
        if (Warned.Add(id)) Debug.LogWarning(message);
    }
}
