#if UNITY_EDITOR
using System;
using UnityEditor;
#endif

/// <summary>
/// Named points in the game that the "Game Start Point" editor window (and
/// <see cref="DebugConsole"/> at runtime) can jump straight into on Play,
/// skipping the main menu and lobby flow entirely.
/// Seeded from the F12 cheat console entries in <see cref="CheatConsoleUI"/> —
/// keep the two lists in sync when adding new skip points.
/// </summary>
public enum DebugStartPoint
{
    None,
    Day1BoothStart,
    FreePlayDay1NoTutorial,
    Day1SoldierAlexeiCutscene,
    Day1MutantBreach,
    EndOfDay1,
    Day2StartInsideBunker,
    Day2VladOutBackCutscene,
    Day2OchoBoothEncounter,
    Day3StartInsideBunker,
    Day3InBoothAllSuspectsProcessed,
    EndOfDay4BeforeMutantBreach,
    FreePlayDay4Booth,
    FreePlayDay5Booth,
    EndOfDemo,
}

#if UNITY_EDITOR
/// <summary>
/// Persists the currently selected <see cref="DebugStartPoint"/> in EditorPrefs so the
/// selection survives domain reloads and is readable both by the editor window
/// (<c>DebugStartPointWindow</c>) and by <see cref="DebugConsole"/> at Play-mode Start().
/// </summary>
public static class DebugStartPointPrefs
{
    public const string PrefKey = "GCBC_DebugStartPoint";

    public static DebugStartPoint Selected
    {
        get
        {
            var raw = EditorPrefs.GetString(PrefKey, DebugStartPoint.None.ToString());
            return Enum.TryParse(raw, out DebugStartPoint result) ? result : DebugStartPoint.None;
        }
        set => EditorPrefs.SetString(PrefKey, value.ToString());
    }
}
#endif
