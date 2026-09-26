using System;
using System.IO;
using Bezi;
using GoodCopBadCop.Autopilot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor entry points for the Autopilot playtester: menu items, a settings window, command-line
/// (CI) entry, and Bezi actions. Starting a run:
/// <list type="number">
/// <item>opens Main.unity if needed,</item>
/// <item>backs up persistentDataPath/savedata.json (restored when Play Mode exits),</item>
/// <item>selects a Game Start Point so DebugConsole bootstraps a host session and skips the menu/lobby
/// (the previous selection is restored afterwards),</item>
/// <item>hands the config to <see cref="AutopilotRunner"/> through SessionState and enters Play Mode.</item>
/// </list>
/// </summary>
[InitializeOnLoad]
public static class AutopilotLauncher
{
    private const string MainScenePath = "Assets/_GoodCopBadCop/_Scenes/Main.unity";
    private const string RunningKey = "GCBC.Autopilot.Running";
    private const string SaveBackupStateKey = "GCBC.Autopilot.SaveBackupState"; // "backup" | "none"
    private const string PrevStartPointKey = "GCBC.Autopilot.PrevStartPoint";
    private const string WindowConfigPrefKey = "GCBC.Autopilot.WindowConfig";
    private const string SaveFileName = "savedata.json";
    private const string BackupFileName = "savedata.autopilot-backup.json";

    static AutopilotLauncher()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += RestoreIfRunEnded;
    }

    // ── Menu ────────────────────────────────────────────────────────────────

    [MenuItem("Good Cop Bad Cop/Autopilot/Run Smoke Test (All Days)", priority = 200)]
    private static void MenuSmoke() => StartRun(new AutopilotConfig { mode = AutopilotMode.Smoke, label = "menu-smoke" });

    [MenuItem("Good Cop Bad Cop/Autopilot/Run Flow Playtest (All Days)", priority = 201)]
    private static void MenuFlow() => StartRun(new AutopilotConfig { mode = AutopilotMode.Flow, label = "menu-flow" });

    [MenuItem("Good Cop Bad Cop/Autopilot/Run Input Playtest (All Days)", priority = 202)]
    private static void MenuInput() => StartRun(new AutopilotConfig { mode = AutopilotMode.Input, label = "menu-input" });

    [MenuItem("Good Cop Bad Cop/Autopilot/Settings and Custom Run...", priority = 220)]
    private static void MenuWindow() => AutopilotWindow.Open();

    [MenuItem("Good Cop Bad Cop/Autopilot/Open Latest Report", priority = 240)]
    private static void MenuOpenLatest()
    {
        string dir = LatestReportDirectory();
        if (dir == null) { EditorUtility.DisplayDialog("Autopilot", "No autopilot report found yet.", "OK"); return; }
        string md = Path.Combine(dir, "report.md");
        EditorUtility.OpenWithDefaultApp(File.Exists(md) ? md : dir);
    }

    [MenuItem("Good Cop Bad Cop/Autopilot/Reveal Reports Folder", priority = 241)]
    private static void MenuReveal()
    {
        string dir = LatestReportDirectory();
        EditorUtility.RevealInFinder(dir ?? ReportsRoot());
    }

    [MenuItem("Good Cop Bad Cop/Autopilot/Stop Current Run", priority = 260)]
    private static void MenuStop() => StopRun("Stopped from the menu.");

    [MenuItem("Good Cop Bad Cop/Autopilot/Stop Current Run", true)]
    private static bool MenuStopValidate() => AutopilotRunner.Active != null;

    // ── Run control ─────────────────────────────────────────────────────────

    public static string StartRun(AutopilotConfig config)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "Cannot start: the editor is already in (or entering) Play Mode.";
        if (EditorApplication.isCompiling)
            return "Cannot start: scripts are compiling.";

        if (!EnsureMainSceneOpen())
            return "Cannot start: Main.unity is not open (open cancelled).";

        BackupSave();

        SessionState.SetString(PrevStartPointKey, DebugStartPointPrefs.Selected.ToString());
        DebugStartPointPrefs.Selected = StartPointFor(config);

        SessionState.SetString(AutopilotRunner.PendingConfigKey, config.ToJson());
        SessionState.EraseString(AutopilotRunner.LastResultKey);
        SessionState.SetBool(RunningKey, true);

        Debug.Log($"{AutopilotReport.LogPrefix} Starting {config.mode} run (days {config.startDay}..{(config.endDay > 0 ? config.endDay.ToString() : "last")}).");
        EditorApplication.isPlaying = true;
        return $"Autopilot {config.mode} run starting (start point {DebugStartPointPrefs.Selected}). Reports: {ReportsRoot()}";
    }

    public static void StopRun(string reason)
    {
        if (AutopilotRunner.Active != null) AutopilotRunner.Active.Finish(reason);
        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    private static DebugStartPoint StartPointFor(AutopilotConfig config)
    {
        switch (config.startDay)
        {
            case 1: return config.day1Tutorial ? DebugStartPoint.Day1BoothStart : DebugStartPoint.FreePlayDay1NoTutorial;
            case 2: return DebugStartPoint.Day2StartInsideBunker;
            case 3: return DebugStartPoint.Day3StartInsideBunker;
            default: return DebugStartPoint.Day1BoothStart; // runner jumps with DebugConsole.SkipToDay(startDay)
        }
    }

    private static bool EnsureMainSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == MainScenePath) return true;

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        return true;
    }

    // ── Save protection ─────────────────────────────────────────────────────

    private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);
    private static string BackupPath => Path.Combine(Application.persistentDataPath, BackupFileName);

    private static void BackupSave()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                File.Copy(SavePath, BackupPath, overwrite: true);
                SessionState.SetString(SaveBackupStateKey, "backup");
            }
            else
            {
                SessionState.SetString(SaveBackupStateKey, "none");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{AutopilotReport.LogPrefix} Could not back up save data: {e.Message}");
            SessionState.SetString(SaveBackupStateKey, "");
        }
    }

    private static void RestoreSave()
    {
        string state = SessionState.GetString(SaveBackupStateKey, "");
        try
        {
            if (state == "backup" && File.Exists(BackupPath))
            {
                File.Copy(BackupPath, SavePath, overwrite: true);
                File.Delete(BackupPath);
            }
            else if (state == "none" && File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{AutopilotReport.LogPrefix} Could not restore save data (backup kept at {BackupPath}): {e.Message}");
        }
        SessionState.EraseString(SaveBackupStateKey);
    }

    // ── Play Mode lifecycle ─────────────────────────────────────────────────

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode)
            RestoreIfRunEnded();
    }

    private static void RestoreIfRunEnded()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        SessionState.SetBool(RunningKey, false);
        SessionState.EraseString(AutopilotRunner.PendingConfigKey);
        RestoreSave();

        string prev = SessionState.GetString(PrevStartPointKey, DebugStartPoint.None.ToString());
        DebugStartPointPrefs.Selected = Enum.TryParse(prev, out DebugStartPoint p) ? p : DebugStartPoint.None;
        SessionState.EraseString(PrevStartPointKey);

        string json = SessionState.GetString(AutopilotRunner.LastResultKey, "");
        var result = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<AutopilotRunner.RunResult>(json);
        if (result == null)
        {
            Debug.LogWarning($"{AutopilotReport.LogPrefix} Run ended without a result (Play Mode stopped before the runner started?).");
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }

        string summary = $"{AutopilotReport.LogPrefix} Run {(result.passed ? "PASSED" : "FAILED")} — {result.reason}\nReport: {result.reportPath}";
        if (result.passed) Debug.Log(summary); else Debug.LogWarning(summary);

        if (result.exitEditor || Application.isBatchMode)
            EditorApplication.Exit(result.passed ? 0 : 1);
    }

    // ── Command line ────────────────────────────────────────────────────────

    /// <summary>
    /// CI entry point. Example (do NOT pass -quit; the editor exits itself with 0 = pass, 1 = fail, 2 = no result):
    /// <c>Unity -batchmode -projectPath . -executeMethod AutopilotLauncher.RunFromCommandLine
    /// -autopilotMode Smoke -autopilotStartDay 1 -autopilotEndDay 3 -autopilotTimeScale 2</c>
    /// Optional: <c>-autopilotConfig path/to/config.json</c> (AutopilotConfig JSON; flags override it).
    /// Run without -nographics so cameras, UI and URP behave as in a normal session.
    /// </summary>
    public static void RunFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        var config = new AutopilotConfig();

        string configPath = Arg(args, "-autopilotConfig");
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            config = AutopilotConfig.FromJson(File.ReadAllText(configPath));

        if (Enum.TryParse(Arg(args, "-autopilotMode"), true, out AutopilotMode mode)) config.mode = mode;
        if (int.TryParse(Arg(args, "-autopilotStartDay"), out int start)) config.startDay = start;
        if (int.TryParse(Arg(args, "-autopilotEndDay"), out int end)) config.endDay = end;
        if (float.TryParse(Arg(args, "-autopilotTimeScale"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float ts)) config.timeScale = ts;
        if (Enum.TryParse(Arg(args, "-autopilotVerdicts"), true, out VerdictPolicy policy)) config.verdictPolicy = policy;
        if (bool.TryParse(Arg(args, "-autopilotDay1Tutorial"), out bool tutorial)) config.day1Tutorial = tutorial;

        config.exitEditorWhenDone = true;
        config.screenshotsOnFindings = !Application.isBatchMode || !Environment.CommandLine.Contains("-nographics");
        config.label = string.IsNullOrEmpty(config.label) ? "command-line" : config.label;

        string status = StartRun(config);
        Debug.Log($"{AutopilotReport.LogPrefix} {status}");
        if (!status.StartsWith("Autopilot", StringComparison.Ordinal))
            EditorApplication.Exit(2);
    }

    private static string Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    // ── Reports ─────────────────────────────────────────────────────────────

    public static string ReportsRoot() =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AutopilotReports");

    public static string LatestReportDirectory()
    {
        string pointer = Path.Combine(ReportsRoot(), "latest.txt");
        if (!File.Exists(pointer)) return null;
        string dir = File.ReadAllText(pointer).Trim();
        return Directory.Exists(dir) ? dir : null;
    }

    // ── Bezi actions ────────────────────────────────────────────────────────

    [BeziAction("Autopilot: start an automated playtest run in Play Mode. mode = Smoke (skip through days with debug hooks, " +
                "observe errors/stalls), Flow (play each day via real interaction entry points) or Input (walk/aim/press with a " +
                "virtual gamepad). startDay/endDay select the day range (endDay 0 = all days). verdictPolicy = Correct, Random, " +
                "AlwaysPass or AlwaysKill. The save file is backed up and restored automatically. Returns a status string; poll " +
                "GetAutopilotStatus and read GetLatestAutopilotReport when finished.")]
    public static string StartAutopilotRun(string mode, int startDay, int endDay, bool day1Tutorial, float timeScale, string verdictPolicy)
    {
        var config = new AutopilotConfig
        {
            startDay = Mathf.Max(1, startDay),
            endDay = Mathf.Max(0, endDay),
            day1Tutorial = day1Tutorial,
            timeScale = timeScale > 0f ? timeScale : 1f,
            label = "bezi"
        };
        if (Enum.TryParse(mode, true, out AutopilotMode m)) config.mode = m;
        if (Enum.TryParse(verdictPolicy, true, out VerdictPolicy v)) config.verdictPolicy = v;
        return StartRun(config);
    }

    [BeziAction("Autopilot: get the status of the current or most recent automated playtest run (running flag, day, phase, " +
                "finding counts, or the last result and report path).", IsReadOnly = true)]
    public static string GetAutopilotStatus()
    {
        AutopilotRunner active = AutopilotRunner.Active;
        if (active != null && EditorApplication.isPlaying)
            return active.StatusJson();

        string json = SessionState.GetString(AutopilotRunner.LastResultKey, "");
        bool pending = SessionState.GetBool(RunningKey, false);
        return string.IsNullOrEmpty(json)
            ? $"{{\"running\":{(pending ? "true" : "false")},\"lastResult\":null,\"latestReportDir\":\"{Escape(LatestReportDirectory())}\"}}"
            : $"{{\"running\":{(pending ? "true" : "false")},\"lastResult\":{json}}}";
    }

    [BeziAction("Autopilot: read the most recent autopilot report. markdown=true returns report.md (human summary, findings, " +
                "timeline); false returns report.json. Output is truncated to maxChars (0 = 60000).", IsReadOnly = true)]
    public static string GetLatestAutopilotReport(bool markdown, int maxChars)
    {
        string dir = LatestReportDirectory();
        if (dir == null) return "No autopilot report found.";
        string file = Path.Combine(dir, markdown ? "report.md" : "report.json");
        if (!File.Exists(file)) return $"Report file not written yet: {file}";
        string text = File.ReadAllText(file);
        int limit = maxChars > 0 ? maxChars : 60000;
        return text.Length <= limit ? text : text.Substring(0, limit) + "\n…(truncated)";
    }

    [BeziAction("Autopilot: stop the current automated playtest run, write its report and exit Play Mode.")]
    public static string StopAutopilotRun()
    {
        if (AutopilotRunner.Active == null) return "No autopilot run is active.";
        StopRun("Stopped by request.");
        return "Autopilot run stopped; report written.";
    }

    private static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Settings window ─────────────────────────────────────────────────────

    public class AutopilotWindow : EditorWindow
    {
        private AutopilotConfig _config;
        private Vector2 _scroll;

        public static void Open()
        {
            var w = GetWindow<AutopilotWindow>("Autopilot");
            w.minSize = new Vector2(360f, 420f);
        }

        private void OnEnable()
        {
            _config = AutopilotConfig.FromJson(EditorPrefs.GetString(WindowConfigPrefKey, ""));
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Automated Playtester", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Smoke: skips through days with debug hooks and records errors/stalls.\n" +
                "Flow: plays days through the real interaction entry points (no aiming).\n" +
                "Input: walks, aims and presses with a virtual gamepad.\n" +
                "Save data is backed up and restored; the Game Start Point selection is restored afterwards.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _config.mode = (AutopilotMode)EditorGUILayout.EnumPopup("Mode", _config.mode);
            _config.startDay = Mathf.Max(1, EditorGUILayout.IntField("Start Day", _config.startDay));
            _config.endDay = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("End Day", "0 = all days"), _config.endDay));
            using (new EditorGUI.DisabledScope(_config.startDay != 1))
                _config.day1Tutorial = EditorGUILayout.Toggle("Day 1 Tutorial", _config.day1Tutorial);
            _config.verdictPolicy = (VerdictPolicy)EditorGUILayout.EnumPopup("Verdicts", _config.verdictPolicy);
            _config.timeScale = EditorGUILayout.Slider("Time Scale", _config.timeScale, 0.5f, 4f);
            _config.seed = EditorGUILayout.IntField(new GUIContent("Seed", "0 = random"), _config.seed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Timeouts", EditorStyles.boldLabel);
            _config.stallSeconds = EditorGUILayout.FloatField("Stall (s)", _config.stallSeconds);
            _config.maxDayMinutes = EditorGUILayout.FloatField("Max per Day (min)", _config.maxDayMinutes);
            _config.maxRunMinutes = EditorGUILayout.FloatField("Max Run (min)", _config.maxRunMinutes);
            _config.smokeObserveSeconds = EditorGUILayout.FloatField("Smoke Observe (s)", _config.smokeObserveSeconds);
            _config.taskGraceSeconds = EditorGUILayout.FloatField("Task Grace (s)", _config.taskGraceSeconds);
            _config.screenshotsOnFindings = EditorGUILayout.Toggle("Screenshots", _config.screenshotsOnFindings);
            _config.label = EditorGUILayout.TextField("Label", _config.label);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(WindowConfigPrefKey, _config.ToJson());

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Run", GUILayout.Height(32f)))
                {
                    AutopilotConfig copy = AutopilotConfig.FromJson(_config.ToJson());
                    copy.exitEditorWhenDone = false;
                    Debug.Log($"{AutopilotReport.LogPrefix} {StartRun(copy)}");
                }
            }

            using (new EditorGUI.DisabledScope(AutopilotRunner.Active == null))
            {
                if (GUILayout.Button("Stop Current Run")) StopRun("Stopped from the Autopilot window.");
            }

            if (GUILayout.Button("Open Latest Report")) MenuOpenLatest();

            if (AutopilotRunner.Active != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Running — Day {AutopilotGame.CurrentDay}, {AutopilotGame.Phase}, processed {AutopilotGame.SuspectsProcessed}");
                Repaint();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
