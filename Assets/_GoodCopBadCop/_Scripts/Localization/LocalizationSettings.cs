using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[Serializable]
public class LocalizationLanguage
{
    [Tooltip("Language code used as the CSV column header, for example fr or zh-Hans.")]
    public string code = "en";
    public string displayName = "English";

    [Tooltip("Font for labels whose source font is not assigned to any font role. Empty keeps the label's own font.")]
    public TMP_FontAsset font;

    [Tooltip("Font per role (UI, Dialogue, ...). A role with no font here keeps each label's own font.")]
    public List<RoleFont> roleFonts = new List<RoleFont>();

    public TMP_FontAsset FontForRole(string role)
    {
        if (roleFonts == null || string.IsNullOrEmpty(role)) return null;
        foreach (var rf in roleFonts) if (rf != null && rf.role == role) return rf.font;
        return null;
    }

    public void SetRoleFont(string role, TMP_FontAsset f)
    {
        if (roleFonts == null) roleFonts = new List<RoleFont>();
        foreach (var rf in roleFonts) if (rf != null && rf.role == role) { rf.font = f; return; }
        roleFonts.Add(new RoleFont { role = role, font = f });
    }

    [Tooltip("Fallback chain appended to the active font at runtime (Play Mode / builds only).")]
    public List<TMP_FontAsset> fallbackFonts = new List<TMP_FontAsset>();

    [Tooltip("Multiplier applied to the label's font size while this language is active.")]
    public float sizeScale = 1f;
}

/// <summary>A usage group of fonts (UI, Dialogue, ...). Source fonts listed here belong to the role.</summary>
[Serializable]
public class FontRole
{
    public string name;
    [Tooltip("Project fonts authored for this use. Labels using one of these get the role's font per language.")]
    public List<TMP_FontAsset> sourceFonts = new List<TMP_FontAsset>();
}

[Serializable]
public class RoleFont
{
    public string role;
    public TMP_FontAsset font;
}

[Serializable]
public class DialogueFieldRule
{
    [Tooltip("ScriptableObject class name (subclasses included).")]
    public string typeName;
    [Tooltip("Top-level serialized field names to scan. Everything else on this type is ignored.")]
    public List<string> fields = new List<string>();
}

/// <summary>
/// Project-wide localization settings. Lives in a Resources folder so the runtime can load it:
/// Assets/_GoodCopBadCop/Resources/Localization/LocalizationSettings.asset
/// </summary>
[CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Localization/Localization Settings")]
public class LocalizationSettings : ScriptableObject
{
    public const string ResourcePath = "Localization/LocalizationSettings";

    public string defaultLanguage = "en";

    public List<LocalizationLanguage> languages = new List<LocalizationLanguage>
    {
        new LocalizationLanguage { code = "en", displayName = "English" }
    };

    [Range(0f, 1.5f)]
    [Tooltip("Pseudolocale expansion. 0.40 = +40 percent visible length.")]
    public float pseudoExpansion = 0.4f;

    [Tooltip("Characters flagged by import validation. Fill from the pseudolocale character probe results, not guesses.")]
    public string forbiddenCharacters = "";

    [Tooltip("Import warns when a translation is longer than source x this ratio.")]
    public float maxLengthRatio = 1.4f;

    [Header("Text Fit presets (min size as a fraction of the design size)")]
    [Range(0.3f, 1f)] public float fitLabelMin = 0.7f;
    [Range(0.3f, 1f)] public float fitHeaderMin = 0.6f;
    [Range(0.3f, 1f)] public float fitBodyMin = 0.75f;

    [Tooltip("ScriptableObject types the scanner reads text from (class name, subclasses included). Presets, configs and item data are ignored.")]
    public List<string> dialogueAssetTypes = new List<string> { "ScriptedDialogue", "SuspectData" };

    [Tooltip("Only scan dialogue assets the game can actually load: referenced (directly or indirectly) by an enabled " +
             "build scene, or living in a Resources folder. Orphaned drafts are skipped.")]
    public bool onlyUsedDialogueAssets = true;

    [Tooltip("Optional per-type field allowlist. When a dialogue asset type has a rule, only these top-level fields are scanned " +
             "(nested strings inside them included). Types without a rule scan every text field.")]
    public List<DialogueFieldRule> dialogueFieldRules = new List<DialogueFieldRule>
    {
        new DialogueFieldRule
        {
            typeName = "SuspectData",
            fields = new List<string>
            {
                "entryReasons", "invalidEntryReasons",
                "entryDialogues", "uncannyEntryDialogues",
                "exitDialoguesPassed", "exitDialoguesQuarantined", "exitDialoguesKilled",
                "quarantineExitLines", "killExitLines",
                "questionResponses", "idleBarks", "uncannyIdleBarks"
            }
        }
    };

    [Tooltip("Serialized string field names the scanner never treats as copy (identifiers, triggers, ids).")]
    public List<string> fieldDenyList = new List<string>
    {
        "m_Name", "animationTrigger", "cameraTrigger", "IDNumber", "idNumber", "id", "ID", "guid", "tag",
        "layer", "eventName", "trigger", "path", "address", "scene", "sceneName", "parameterName", "stateName"
    };

    [Tooltip("Font roles. Each project font is assigned to one role; each language then picks one font per role.")]
    public List<FontRole> fontRoles = new List<FontRole>
    {
        new FontRole { name = "UI" },
        new FontRole { name = "Dialogue" }
    };

    /// <summary>Role whose source fonts explicitly include <paramref name="sourceFont"/> (Fonts tab), or null when unassigned.</summary>
    public string RoleOf(TMP_FontAsset sourceFont)
    {
        if (sourceFont == null || fontRoles == null) return null;
        foreach (var r in fontRoles)
            if (r != null && r.sourceFonts != null && r.sourceFonts.Contains(sourceFont)) return r.name;
        return null;
    }

    /// <summary>
    /// Runtime role lookup: the explicit <see cref="RoleOf"/>, else the role that font fills for the default language
    /// (for example SovietStyle SDF is the English "UI" font). Null when the font maps to no role.
    /// </summary>
    public string EffectiveRoleOf(TMP_FontAsset sourceFont)
    {
        string explicitRole = RoleOf(sourceFont);
        if (explicitRole != null || sourceFont == null) return explicitRole;

        var source = Find(defaultLanguage) ?? Find(LocalizationTable.SourceLanguage);
        if (source?.roleFonts != null)
            foreach (var rf in source.roleFonts)
                if (rf != null && rf.font == sourceFont && HasRole(rf.role)) return rf.role;
        return null;
    }

    /// <summary>
    /// Font for a label with no <see cref="LocalizedText"/>: its role's font for <paramref name="lang"/> when the font
    /// maps to a role, otherwise the label's own font (the global TMP fallbacks still cover missing glyphs).
    /// </summary>
    public TMP_FontAsset ResolveUntaggedFont(LocalizationLanguage lang, TMP_FontAsset sourceFont)
    {
        if (lang == null || sourceFont == null) return sourceFont;
        string role = EffectiveRoleOf(sourceFont);
        var rf = role != null ? lang.FontForRole(role) : null;
        return rf != null ? rf : sourceFont;
    }

    public bool HasRole(string role)
    {
        if (string.IsNullOrEmpty(role) || fontRoles == null) return false;
        foreach (var r in fontRoles) if (r != null && r.name == role) return true;
        return false;
    }

    /// <summary>
    /// Font to show for a label authored in <paramref name="sourceFont"/> while <paramref name="lang"/> is active.
    /// Role = <paramref name="roleOverride"/> when set, otherwise the role the source font is assigned to.
    /// Roles use the language's role font (or keep the source font if none is set); unassigned fonts use
    /// the language's unassigned font (or keep the source font).
    /// </summary>
    public TMP_FontAsset ResolveFont(LocalizationLanguage lang, TMP_FontAsset sourceFont, string roleOverride = null)
    {
        if (lang == null) return sourceFont;
        string role = !string.IsNullOrEmpty(roleOverride) ? roleOverride : EffectiveRoleOf(sourceFont);
        if (!string.IsNullOrEmpty(role))
        {
            var rf = lang.FontForRole(role);
            return rf != null ? rf : sourceFont;
        }
        return lang.font != null ? lang.font : sourceFont;
    }

    /// <summary>Resolves a font for the current language. Safe to call when no settings asset exists.</summary>
    public static TMP_FontAsset Localize(TMP_FontAsset sourceFont, string roleOverride = null)
    {
        var s = Instance;
        if (s == null) return sourceFont;
        return s.ResolveFont(s.FontLanguage(Loc.CurrentLanguage), sourceFont, roleOverride);
    }

    /// <summary>
    /// Language whose fonts are shown for <paramref name="code"/>. Pseudolocales have no entry of their own and use the
    /// default language's fonts, so pseudolocalized text looks exactly like the source build apart from the characters.
    /// </summary>
    public LocalizationLanguage FontLanguage(string code)
    {
        var lang = Find(code);
        if (lang == null && PseudoLocalizer.IsPseudo(code)) lang = Find(defaultLanguage) ?? Find(LocalizationTable.SourceLanguage);
        return lang;
    }

    private static LocalizationSettings _instance;

    public static LocalizationSettings Instance
    {
        get
        {
            if (_instance == null) _instance = Resources.Load<LocalizationSettings>(ResourcePath);
            return _instance;
        }
    }

    public static void ClearCache() { _instance = null; }

    public LocalizationLanguage Find(string code)
    {
        if (languages == null) return null;
        foreach (var l in languages) if (l != null && l.code == code) return l;
        return null;
    }
}
