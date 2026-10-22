using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Short in-editor guide for the localization tool. Opened from the "Guide" button in the Localization window.</summary>
public class LocalizationGuideWindow : EditorWindow
{
    private struct Section
    {
        public string title;
        public string body;
    }

    private static readonly List<Section> Sections = new List<Section>
    {
        new Section
        {
            title = "1. Quick start",
            body =
                "1. Open <b>Tools > Localization</b>.\n" +
                "2. <b>Scan</b> tab: press Scan, review the list, press Apply selected. This creates the sheet CSVs, binds each key to its origin and attaches LocalizedText to TMP labels.\n" +
                "3. Press <b>Build runtime table</b> (toolbar). This writes Resources/Localization/strings.txt. Never edit that file by hand.\n" +
                "4. Enter Play Mode and switch language with the Preview tab or the F12 cheat menu."
        },
        new Section
        {
            title = "2. Where text comes from (source groups)",
            body =
                "The Scan and Strings tabs have a <b>Source</b> filter so dialogue can be handled separately:\n" +
                "- <b>Suspect objects</b>: dialogue coded in components on suspect character objects (SuspectWorldDialogue, DrunkBehaviour, OchoBoothEncounter and so on). Found on any object under a SuspectCharacter, in prefabs and loaded scenes.\n" +
                "- <b>Suspect data</b>: SuspectData assets (names, descriptions, entry reasons, question answers, barks, exit lines).\n" +
                "- <b>Conversation data</b>: ScriptedDialogue assets (npcLine, playerChoiceText, npcResponse).\n" +
                "- <b>Dialogue (all three)</b> and <b>Non-dialogue</b> combine them.\n" +
                "Each suspect or conversation gets its own sheet named Dialogue_<name>. Use the Sheet popup next to the Source filter to focus on one character.\n" +
                "Note: prefab instances placed in a scene are skipped by the scan; their source prefab is scanned instead."
        },
        new Section
        {
            title = "3. Scan tab",
            body =
                "Tick what to scan (loaded scenes, prefabs, ScriptableObjects, suspect objects). Scenes must be open to be scanned.\n" +
                "Status: NEW = not localized yet, CHANGED = text differs from the sheet, OK = already localized (hidden unless 'Show already localized').\n" +
                "Identical text in different places gets separate keys. If text AND context match, you are offered 'reuse that key'; it is never automatic.\n" +
                "Field names such as animationTrigger, cameraTrigger and IDs are skipped (see the deny list in the Fonts tab)."
        },
        new Section
        {
            title = "4. Strings tab",
            body =
                "Edit notes, English source and every language inline. Source edits are written back to the origin asset when you leave the field; translations are saved to the CSV.\n" +
                "Filters: sheet, source group, search, untranslated, new/changed, frozen, pseudo overflow, language. <b>Ping source</b> selects the origin object. <b>Preview</b> jumps to the Preview tab. Add a language with the code box and Add language.\n" +
                "<b>Clear translations</b> empties one language (or all) across every sheet or only the filtered strings. Keys, English source and freeze hashes stay; run Build runtime table afterwards."
        },
        new Section
        {
            title = "5. Preview tab",
            body =
                "Shows the string in the original element's box size with its real TMP font. A yellow outline is the box. Warnings cover overflow, missing glyphs (tofu) and broken placeholders or tags.\n" +
                "Pick pseudo-latincyr or pseudo-cjk to stress-test with +40% length. <b>Capture reference</b> saves a PNG to Localization/Refs and adds it to the notes for translators.\n" +
                "In Play Mode you can set the language live or cycle through all locales."
        },
        new Section
        {
            title = "6. Import / Export and Freeze",
            body =
                "Export writes one CSV per sheet for the translators (optionally with pseudo columns). The <b>Translations</b> option exports current translations, empty language columns (best for a fresh handoff: translators fill them and Import maps them back), or English source only; project sheets are never changed by export. Import reads translated CSVs, shows a diff and validates placeholders, tags, forbidden characters and length before you apply.\n" +
                "<b>Freeze handoff</b> after sending a table. Later edits to frozen text are flagged 'changed' and new strings are 'new'; <b>Export delta</b> sends only those."
        },
        new Section
        {
            title = "7. Findings tab (report for the localization manager)",
            body =
                "Run the sub-tabs in order: Max length, Placeholders, Restricted chars, Player address, Numbers / dates, Pseudo self-check, Checklist. Then press Build and save findings summary to create Localization/Findings/findings_summary.md."
        },
        new Section
        {
            title = "8. Stats tab",
            body =
                "Total word count of the English source, plus strings and characters, broken down by source group (dialogue vs other) and by sheet (one per suspect or conversation). " +
                "Also shows words that are new or changed since the last freeze, translation progress per language and a per-word cost estimate. Only strings already in the sheets are counted; the last Scan's not-yet-applied text is reported separately. Export stats saves Localization/Findings/stats.md."
        },
        new Section
        {
            title = "9. Using it from code",
            body =
                "<b>Loc.Get(\"key\")</b> or <b>Loc.Get(\"key\", args)</b> for keyed text. <b>Loc.GetFor(this, \"propertyPath\", english)</b> for a string field on a ScriptableObject or component. " +
                "Subtitles are translated automatically by matching the English line, so dialogue shown through Subtitles needs no code change. " +
                "Dialogue shown elsewhere (for example choice buttons) should call Loc.GetFor or use a LocalizedText key.\n" +
                "Language is stored in the settings.language PlayerPrefs key. Network messages must carry ids, never translated text."
        },
        new Section
        {
            title = "10. Good to know",
            body =
                "- No machine translation is included, by publisher request.\n" +
                "- Pseudolocales are generated by code from the manager's specs and only exist in the Editor and development builds.\n" +
                "- Fonts tab: <b>Library</b> downloads OFL fonts (or any direct URL) into _Fonts/Fallback, previews them with sample text per script, and creates Dynamic TMP font assets in one click. <b>Languages</b> sets each language's main font, size scale and fallback chain (quick swap, reorder, live preview). <b>Coverage</b> lists characters with no glyph."
        }
    };

    private Vector2 _scroll;
    private GUIStyle _body;
    private GUIStyle _title;

    [MenuItem("Tools/Localization Guide")]
    public static void Open()
    {
        var w = GetWindow<LocalizationGuideWindow>("Localization Guide");
        w.minSize = new Vector2(420, 360);
        w.Show();
    }

    private void OnGUI()
    {
        if (_body == null)
        {
            _body = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, padding = new RectOffset(8, 8, 2, 8) };
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, padding = new RectOffset(4, 4, 8, 2) };
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        GUILayout.Label("Localization Guide", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
        foreach (var s in Sections)
        {
            GUILayout.Label(s.title, _title);
            GUILayout.Label(s.body, _body);
        }
        EditorGUILayout.EndScrollView();
    }
}
