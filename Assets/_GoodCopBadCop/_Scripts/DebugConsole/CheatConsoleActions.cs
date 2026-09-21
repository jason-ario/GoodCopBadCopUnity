using System;
using System.Collections.Generic;

/// <summary>
/// Single source of truth for the list of cheat actions shown both by the in-game
/// F12 overlay (<see cref="CheatConsoleUI"/>) and the in-editor "Cheat Console"
/// window (<c>CheatConsoleWindow</c>, Editor-only). Add new cheats here once and
/// both surfaces pick them up automatically.
/// </summary>
public static class CheatConsoleActions
{
    /// <summary>
    /// Builds the ordered list of (label, callback) cheat entries. Callbacks only
    /// touch runtime singletons when invoked, so building this list is always safe,
    /// even outside Play mode.
    /// </summary>
    public static List<(string Label, Action Callback)> BuildCheats()
    {
        var cheats = new List<(string Label, Action Callback)>();

        cheats.Add(("Skip to Day 1 — Booth Start", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToDay(1))));

        cheats.Add(("Free Play — Day 1 Booth (No Tutorial)", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.StartFreePlayDay1())));

        cheats.Add(("Skip to Day 1 — Soldier / Alexei Cutscene", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToSoldierSlot())));

        cheats.Add(("Skip to Day 1 — Mutant Breach", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToMutantBreach())));

        cheats.Add(("Skip to End of Day 1", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDay1())));

        cheats.Add(("Skip to Day 2 — Start (Inside Bunker)", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToStartOfDay2())));

        cheats.Add(("Skip to Day 2 — Vlad Out Back Cutscene", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDay2())));

        cheats.Add(("Skip to Day 2 — Ocho Booth Encounter", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToOchoBoothEncounter())));

        cheats.Add(("Skip to Day 3 — Start (Inside Bunker)", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToStartOfDay3())));

        cheats.Add(("Skip to Day 3 — In Booth, All Suspects Processed", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToDay3PostShiftBooth())));

        cheats.Add(("Skip to End of Day 4 — Before Mutant Breach", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDay4())));

        cheats.Add(("Free Play — Day 4 Booth", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.StartFreePlay())));

        cheats.Add(("Free Play — Day 5 Booth", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.StartFreePlayDay5())));

        cheats.Add(("Skip to End of Demo (Thanks For Playing)", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDemo())));

        cheats.Add(("Teleport to Power Station", () =>
            DebugConsole.Instance.TeleportToPowerStation()));

        cheats.Add(("Unlock All Anomalies + Guidebook Pages", () =>
            DebugConsole.Instance.EnsureGameStartedThen(() =>
            {
                AnomalyUnlockManager.Instance?.UnlockAllAnomalies();
            })));

        cheats.Add(("Smash Booth Glass", () =>
            DebugConsole.Instance.SmashGlass()));

        cheats.Add(("Trigger End of Shift Report", () =>
            ShiftManager.Instance?.DebugShowEndOfShiftReport()));

        cheats.Add(("Trigger Mail Delivery Task", () =>
        {
            // SortMailTask adds/updates its own "Sort the mail" tutorial objective row
            // automatically as soon as the delivery triggers — no extra call needed here.
            SortMailTask.Instance?.TriggerDailyTask();
        }));

        cheats.Add(("Trigger Day 3 Power Outage (Fuse Box)", () =>
            DebugConsole.Instance.TriggerDay3PowerOutage()));

        cheats.Add(("Force Next Civilian Suspect — Full Mutant (M)", () =>
            DebugConsole.Instance.EnsureGameStartedThen(DebugConsole.Instance.ForceNextSuspectFullMutant)));

        cheats.Add(("Trigger Mutant Breach", () =>
            DebugConsole.Instance.DebugForceMutantBreach()));

        return cheats;
    }
}
