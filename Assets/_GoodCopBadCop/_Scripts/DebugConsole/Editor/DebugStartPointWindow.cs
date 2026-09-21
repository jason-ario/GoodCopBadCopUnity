using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window that lets you pick a specific point in the game to start at.
/// Each entry is a toggle button; selecting one deselects the others (radio behavior).
/// The selection is stored in EditorPrefs via <see cref="DebugStartPointPrefs"/> and is
/// read by <see cref="DebugConsole"/> on Play — it bootstraps a host session and jumps
/// straight to the chosen point, skipping the main menu, lobby screen, and lobby start.
/// Seeded from the F12 in-game cheat console (<see cref="CheatConsoleUI"/>); keep both
/// lists in sync when adding new skip points.
/// </summary>
public class DebugStartPointWindow : EditorWindow
{
    private static readonly (DebugStartPoint Point, string Label)[] Entries =
    {
        (DebugStartPoint.Day1BoothStart, "Skip to Day 1 — Booth Start"),
        (DebugStartPoint.FreePlayDay1NoTutorial, "Free Play — Day 1 Booth (No Tutorial)"),
        (DebugStartPoint.Day1SoldierAlexeiCutscene, "Skip to Day 1 — Soldier / Alexei Cutscene"),
        (DebugStartPoint.Day1MutantBreach, "Skip to Day 1 — Mutant Breach"),
        (DebugStartPoint.EndOfDay1, "Skip to End of Day 1"),
        (DebugStartPoint.Day2StartInsideBunker, "Skip to Day 2 — Start (Inside Bunker)"),
        (DebugStartPoint.Day2VladOutBackCutscene, "Skip to Day 2 — Vlad Out Back Cutscene"),
        (DebugStartPoint.Day2OchoBoothEncounter, "Skip to Day 2 — Ocho Booth Encounter"),
        (DebugStartPoint.Day3StartInsideBunker, "Skip to Day 3 — Start (Inside Bunker)"),
        (DebugStartPoint.Day3InBoothAllSuspectsProcessed, "Skip to Day 3 — In Booth, All Suspects Processed"),
        (DebugStartPoint.EndOfDay4BeforeMutantBreach, "Skip to End of Day 4 — Before Mutant Breach"),
        (DebugStartPoint.FreePlayDay4Booth, "Free Play — Day 4 Booth"),
        (DebugStartPoint.FreePlayDay5Booth, "Free Play — Day 5 Booth"),
        (DebugStartPoint.EndOfDemo, "Skip to End of Demo (Thanks For Playing)"),
    };

    private static readonly Color SelectedColor = new Color(0.25f, 0.55f, 0.95f, 1f);

    private Vector2 _scroll;

    [MenuItem("Good Cop Bad Cop/Game Start Point")]
    public static void ShowWindow()
    {
        var window = GetWindow<DebugStartPointWindow>("Start Point");
        window.minSize = new Vector2(380f, 300f);
    }

    private void OnGUI()
    {
        var selected = DebugStartPointPrefs.Selected;

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Game Start Point", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick a point in the game to jump straight into on Play — the main menu, " +
            "lobby screen, and lobby start are skipped automatically. " +
            "Selection persists until changed; click the active entry again to return to a normal start.",
            MessageType.Info);
        EditorGUILayout.Space(6f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var (point, label) in Entries)
        {
            bool isSelected = selected == point;

            var prevColor = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = SelectedColor;

            bool pressed = GUILayout.Toggle(isSelected, label, "Button", GUILayout.Height(28f));

            GUI.backgroundColor = prevColor;

            if (pressed != isSelected)
            {
                DebugStartPointPrefs.Selected = pressed ? point : DebugStartPoint.None;
                Repaint();
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(selected == DebugStartPoint.None))
        {
            if (GUILayout.Button("Clear Selection (Normal Start)", GUILayout.Height(24f)))
            {
                DebugStartPointPrefs.Selected = DebugStartPoint.None;
                Repaint();
            }
        }

        EditorGUILayout.Space(4f);
        string statusText = selected == DebugStartPoint.None
            ? "Normal start — main menu and lobby flow will play out as usual."
            : $"Play will start at: {GetLabel(selected)}";
        EditorGUILayout.LabelField(statusText, EditorStyles.wordWrappedMiniLabel);
    }

    private static string GetLabel(DebugStartPoint point)
    {
        foreach (var (p, label) in Entries)
        {
            if (p == point)
                return label;
        }

        return point.ToString();
    }
}
