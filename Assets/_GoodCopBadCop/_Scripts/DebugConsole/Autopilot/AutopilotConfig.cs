#if UNITY_EDITOR
using System;
using UnityEngine;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>How the autopilot drives the game.</summary>
    public enum AutopilotMode
    {
        /// <summary>Tier 1 — skips through each day with debug hooks, only observing errors, stalls and invariants.</summary>
        Smoke,
        /// <summary>Tier 2 — plays each day through the real interaction entry points (logic level, no aiming/walking).</summary>
        Flow,
        /// <summary>Tier 3 — same brain as Flow, but every action is performed with a virtual gamepad (walk, aim, press).</summary>
        Input
    }

    /// <summary>Which stamp the bot applies to each suspect.</summary>
    public enum VerdictPolicy
    {
        /// <summary>Kill infected suspects, pass clean ones.</summary>
        Correct,
        /// <summary>Uniformly random Pass/Kill/Quarantine (seeded).</summary>
        Random,
        AlwaysPass,
        AlwaysKill
    }

    /// <summary>
    /// Serializable run configuration. Written by the editor launcher into SessionState and read by
    /// <see cref="AutopilotRunner"/> when Play Mode starts.
    /// </summary>
    [Serializable]
    public class AutopilotConfig
    {
        public AutopilotMode mode = AutopilotMode.Flow;

        [Tooltip("Campaign day to start on.")]
        public int startDay = 1;

        [Tooltip("Last day to play (inclusive). 0 = every configured day in the scene.")]
        public int endDay = 0;

        [Tooltip("Day 1 only: play the full tutorial (true) or the free-play Day 1 without tutorial gates (false).")]
        public bool day1Tutorial = true;

        public VerdictPolicy verdictPolicy = VerdictPolicy.Correct;

        [Tooltip("Time.timeScale while the bot runs. Values > 1 speed up runs but can hide timing bugs.")]
        public float timeScale = 1f;

        [Tooltip("Seconds without any game-progress change before a stall is reported and the fallback ladder escalates.")]
        public float stallSeconds = 75f;

        [Tooltip("Hard cap per day (minutes) before the day is force-ended.")]
        public float maxDayMinutes = 20f;

        [Tooltip("Hard cap for the whole run (minutes).")]
        public float maxRunMinutes = 120f;

        [Tooltip("Smoke mode: seconds to observe each day after the shift starts before force-ending it.")]
        public float smokeObserveSeconds = 25f;

        [Tooltip("Flow/Input: seconds to wait for clock-out to arm after the last suspect before force-completing tasks.")]
        public float taskGraceSeconds = 30f;

        [Tooltip("Capture a Game View screenshot for stalls, step failures and invariant violations.")]
        public bool screenshotsOnFindings = true;

        [Tooltip("Exit the editor with a pass/fail exit code when the run ends (command-line / CI usage).")]
        public bool exitEditorWhenDone = false;

        [Tooltip("Random seed for verdicts and dialogue choices. 0 = time-based.")]
        public int seed = 0;

        [Tooltip("Folder (relative to the project root) where reports are written.")]
        public string reportRoot = "AutopilotReports";

        [Tooltip("Free-form label copied into the report.")]
        public string label = "";

        [Tooltip("Console messages containing any of these substrings are ignored (known noise).")]
        public string[] ignoreLogSubstrings =
        {
            "GameAnalytics: REMEMBER THE SDK NEEDS TO BE MANUALLY INITIALIZED",
            "Animator is not playing an AnimatorController",
            "CapturePipelineManager: Detected a frame skip"
        };

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static AutopilotConfig FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AutopilotConfig();
            var config = new AutopilotConfig();
            JsonUtility.FromJsonOverwrite(json, config);
            return config;
        }
    }
}
#endif
