using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private const string QuickStartPref = "Loc.QuickStart.Open";
    private int _quickStartOpen = -1;
    private Vector2 _quickStartScroll;

    private bool QuickStartOpen
    {
        get
        {
            if (_quickStartOpen < 0) _quickStartOpen = EditorPrefs.GetBool(QuickStartPref, true) ? 1 : 0;
            return _quickStartOpen == 1;
        }
        set
        {
            _quickStartOpen = value ? 1 : 0;
            EditorPrefs.SetBool(QuickStartPref, value);
        }
    }

    private static readonly (string tab, string text)[] QuickStartSteps =
    {
        ("1  Scan",       "Find every piece of player-facing text in scenes, prefabs, ScriptableObjects and suspect components. Apply the results to create string keys."),
        ("2  Strings",    "Your master list. Each row is one English source line with a column per language. Edit translations here, or filter by sheet, source type or what is untranslated."),
        ("3  Preview",    "Switch the editor to any language, including pseudolocales, to see how text fits before real translations exist."),
        ("4  Import / Export", "Send the CSV sheets to translators and bring finished work back in."),
        ("5  Findings",   "Validation: missing translations, broken {tokens}, forbidden characters and lines that are too long."),
        ("6  Freeze / Delta", "Snapshot the strings you shipped to translators. Later you see exactly what was added or changed since."),
        ("7  Fonts",      "Pick fonts and fallbacks for each language, download open fonts, and check glyph coverage so no characters show as boxes."),
        ("8  Stats",      "Word and string counts, translation progress and optional cost estimates."),
    };

    private void DrawQuickStart()
    {
        bool open = QuickStartOpen;
        using (LocUI.Card(LocUI.Accent))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool nv = EditorGUILayout.Foldout(open, "Quick start: what is this tool for?", true, EditorStyles.foldoutHeader);
                if (nv != open) QuickStartOpen = nv;
                GUILayout.FlexibleSpace();
                if (open && LocUI.Button("Hide", false, GUILayout.Width(50))) QuickStartOpen = false;
                if (LocUI.Button("Full guide", false, GUILayout.Width(80))) LocalizationGuideWindow.Open();
            }

            if (!open) return;

            GUILayout.Space(4);
            LocUI.Muted_("This window turns the game's text into translatable data. It scans the project for English text, stores each line "
                + "under a stable key in CSV sheets, and at runtime swaps in the player's chosen language. The original text stays in your "
                + "assets as the English source, so nothing breaks if a translation is missing.");
            GUILayout.Space(6);

            GUILayout.Label("Typical workflow", EditorStyles.boldLabel);
            foreach (var step in QuickStartSteps)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(step.tab, EditorStyles.boldLabel, GUILayout.Width(130));
                    GUILayout.Label(step.text, LocUI.MutedStyle);
                }
            }

            GUILayout.Space(6);
            LocUI.Muted_("Header buttons: Save writes the CSV sheets. Build runtime table generates the file the game loads, so run it before "
                + "testing a build. Reload re-reads the sheets from disk. In play mode, pick a language from the F12 cheat menu.");
        }
    }
}
