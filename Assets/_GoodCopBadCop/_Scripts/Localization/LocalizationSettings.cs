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

    [Tooltip("Optional TMP font override while this language is active. Empty keeps the label's own font.")]
    public TMP_FontAsset font;

    [Tooltip("Fallback chain appended to the active font at runtime (Play Mode / builds only).")]
    public List<TMP_FontAsset> fallbackFonts = new List<TMP_FontAsset>();

    [Tooltip("Multiplier applied to the label's font size while this language is active.")]
    public float sizeScale = 1f;
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

    [Tooltip("Serialized string field names the scanner never treats as copy (identifiers, triggers, ids).")]
    public List<string> fieldDenyList = new List<string>
    {
        "m_Name", "animationTrigger", "cameraTrigger", "IDNumber", "idNumber", "id", "ID", "guid", "tag",
        "layer", "eventName", "trigger", "path", "address", "scene", "sceneName", "parameterName", "stateName"
    };

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
