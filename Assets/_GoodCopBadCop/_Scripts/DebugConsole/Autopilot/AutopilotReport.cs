#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GoodCopBadCop.Autopilot
{
    public enum FindingKind
    {
        Exception,
        Error,
        Warning,
        Stall,
        Invariant,
        StepFailed,
        Assist,
        Info
    }

    [Serializable]
    public class AutopilotFinding
    {
        public string kind;
        public string severity;
        public int day;
        public string phase;
        public float firstSeenAt;
        public float lastSeenAt;
        public int count = 1;
        public string title;
        public string detail;
        public string stack;
        public string screenshot;
    }

    [Serializable]
    public class AutopilotDaySummary
    {
        public int day;
        public float startedAt;
        public float endedAt;
        public int suspectsProcessed;
        public int errors;
        public int stalls;
        public int stepFailures;
        public int assists;
        public string outcome = "incomplete";
    }

    [Serializable]
    public class AutopilotReportData
    {
        public string label;
        public string mode;
        public string startedAt;
        public string finishedAt;
        public float durationSeconds;
        public string result;
        public string resultReason;
        public string unityVersion;
        public string configJson;
        public int exceptionCount;
        public int errorCount;
        public int warningCount;
        public int stallCount;
        public int invariantCount;
        public int stepFailedCount;
        public int assistCount;
        public int ignoredLogCount;
        public List<AutopilotDaySummary> days = new List<AutopilotDaySummary>();
        public List<AutopilotFinding> findings = new List<AutopilotFinding>();
        public List<string> timeline = new List<string>();
    }

    /// <summary>
    /// Collects console output, findings and per-day summaries for one autopilot run and writes
    /// report.json / report.md (plus screenshots) to &lt;project&gt;/AutopilotReports/&lt;timestamp&gt;/.
    /// </summary>
    public class AutopilotReport
    {
        public const string LogPrefix = "[Autopilot]";
        private const int MaxTimelineEntries = 600;
        private const int MaxWarningFindings = 60;

        private struct RawLog
        {
            public string Condition;
            public string Stack;
            public LogType Type;
        }

        private readonly ConcurrentQueue<RawLog> _pendingLogs = new ConcurrentQueue<RawLog>();
        private readonly Dictionary<string, AutopilotFinding> _byKey = new Dictionary<string, AutopilotFinding>();
        private readonly AutopilotReportData _data = new AutopilotReportData();
        private readonly float _startTime;
        private AutopilotDaySummary _currentDay;
        private int _screenshotIndex;
        private readonly string[] _ignore;

        private bool IsIgnored(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return false;
            foreach (string s in _ignore)
                if (!string.IsNullOrEmpty(s) && condition.IndexOf(s, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }

        public string Directory { get; }
        public AutopilotReportData Data => _data;
        public Func<int> DayProvider { get; set; } = () => -1;
        public Func<string> PhaseProvider { get; set; } = () => "";
        public bool CaptureScreenshots { get; set; } = true;

        public AutopilotReport(AutopilotConfig config)
        {
            _startTime = Time.realtimeSinceStartup;
            _data.label = config.label;
            _data.mode = config.mode.ToString();
            _data.startedAt = DateTime.Now.ToString("s");
            _data.unityVersion = Application.unityVersion;
            _data.configJson = config.ToJson();
            _ignore = config.ignoreLogSubstrings ?? Array.Empty<string>();

            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string root = Path.Combine(projectRoot, string.IsNullOrWhiteSpace(config.reportRoot) ? "AutopilotReports" : config.reportRoot);
            Directory = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + config.mode);
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(root, "latest.txt"), Directory);

            Application.logMessageReceivedThreaded += OnLogThreaded;
        }

        public float Elapsed => Time.realtimeSinceStartup - _startTime;

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= OnLogThreaded;
        }

        // ── Console capture ─────────────────────────────────────────────────────

        private void OnLogThreaded(string condition, string stackTrace, LogType type)
        {
            _pendingLogs.Enqueue(new RawLog { Condition = condition, Stack = stackTrace, Type = type });
        }

        /// <summary>Converts queued console messages into findings. Main thread only.</summary>
        public void DrainLogs()
        {
            while (_pendingLogs.TryDequeue(out RawLog log))
            {
                if (log.Condition != null && log.Condition.StartsWith(LogPrefix, StringComparison.Ordinal))
                    continue;
                if (IsIgnored(log.Condition))
                {
                    _data.ignoredLogCount++;
                    continue;
                }

                switch (log.Type)
                {
                    case LogType.Exception:
                        Add(FindingKind.Exception, "high", Trim(log.Condition, 300), null, log.Stack, screenshot: false);
                        break;
                    case LogType.Error:
                    case LogType.Assert:
                        Add(FindingKind.Error, "high", Trim(log.Condition, 300), null, log.Stack, screenshot: false);
                        break;
                    case LogType.Warning:
                        if (_data.findings.Count(f => f.kind == nameof(FindingKind.Warning)) < MaxWarningFindings
                            || _byKey.ContainsKey(MakeKey(FindingKind.Warning, Trim(log.Condition, 300), log.Stack)))
                            Add(FindingKind.Warning, "low", Trim(log.Condition, 300), null, log.Stack, screenshot: false);
                        else
                            _data.warningCount++;
                        break;
                }
            }
        }

        // ── Findings ────────────────────────────────────────────────────────────

        public AutopilotFinding Add(FindingKind kind, string severity, string title, string detail = null,
            string stack = null, bool screenshot = true)
        {
            string key = MakeKey(kind, title, stack);
            float now = Elapsed;

            if (_byKey.TryGetValue(key, out AutopilotFinding existing))
            {
                existing.count++;
                existing.lastSeenAt = now;
                Count(kind);
                return existing;
            }

            var finding = new AutopilotFinding
            {
                kind = kind.ToString(),
                severity = severity,
                day = SafeDay(),
                phase = SafePhase(),
                firstSeenAt = now,
                lastSeenAt = now,
                title = title,
                detail = detail,
                stack = Trim(stack, 2000)
            };

            if (screenshot && CaptureScreenshots)
                finding.screenshot = CaptureScreenshot(kind.ToString());

            _byKey[key] = finding;
            _data.findings.Add(finding);
            Count(kind);

            if (kind != FindingKind.Warning && kind != FindingKind.Info)
                Timeline($"{kind}: {title}");

            if (kind == FindingKind.Stall || kind == FindingKind.StepFailed || kind == FindingKind.Invariant)
                Debug.LogWarning($"{LogPrefix} {kind}: {title}");

            return finding;
        }

        private void Count(FindingKind kind)
        {
            switch (kind)
            {
                case FindingKind.Exception: _data.exceptionCount++; if (_currentDay != null) _currentDay.errors++; break;
                case FindingKind.Error: _data.errorCount++; if (_currentDay != null) _currentDay.errors++; break;
                case FindingKind.Warning: _data.warningCount++; break;
                case FindingKind.Stall: _data.stallCount++; if (_currentDay != null) _currentDay.stalls++; break;
                case FindingKind.Invariant: _data.invariantCount++; break;
                case FindingKind.StepFailed: _data.stepFailedCount++; if (_currentDay != null) _currentDay.stepFailures++; break;
                case FindingKind.Assist: _data.assistCount++; if (_currentDay != null) _currentDay.assists++; break;
            }
        }

        public void Timeline(string message)
        {
            string line = $"[{Elapsed,7:0.0}s] D{SafeDay()} {SafePhase()} | {message}";
            _data.timeline.Add(line);
            if (_data.timeline.Count > MaxTimelineEntries)
                _data.timeline.RemoveAt(0);
            Debug.Log($"{LogPrefix} {message}");
        }

        // ── Days ────────────────────────────────────────────────────────────────

        public void BeginDay(int day)
        {
            if (_currentDay != null && _currentDay.day == day) return;
            EndDay("advanced");
            _currentDay = new AutopilotDaySummary { day = day, startedAt = Elapsed };
            _data.days.Add(_currentDay);
            Timeline($"=== Day {day} started ===");
        }

        public void SetDaySuspects(int processed)
        {
            if (_currentDay != null) _currentDay.suspectsProcessed = Mathf.Max(_currentDay.suspectsProcessed, processed);
        }

        public void EndDay(string outcome)
        {
            if (_currentDay == null) return;
            _currentDay.endedAt = Elapsed;
            if (_currentDay.outcome == "incomplete") _currentDay.outcome = outcome;
            _currentDay = null;
        }

        public void MarkCurrentDayOutcome(string outcome)
        {
            if (_currentDay != null) _currentDay.outcome = outcome;
        }

        // ── Output ──────────────────────────────────────────────────────────────

        public bool Passed => _data.exceptionCount == 0 && _data.errorCount == 0 && _data.stallCount == 0
                              && _data.stepFailedCount == 0 && _data.invariantCount == 0;

        public string Finish(string reason)
        {
            DrainLogs();
            EndDay("run ended");
            _data.finishedAt = DateTime.Now.ToString("s");
            _data.durationSeconds = Elapsed;
            _data.result = Passed ? "PASS" : "FAIL";
            _data.resultReason = reason;

            string jsonPath = Path.Combine(Directory, "report.json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(_data, true));
            File.WriteAllText(Path.Combine(Directory, "report.md"), BuildMarkdown());
            Dispose();
            return jsonPath;
        }

        private string BuildMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Autopilot report — {_data.result}");
            sb.AppendLine();
            sb.AppendLine($"- Mode: `{_data.mode}`  Label: `{_data.label}`");
            sb.AppendLine($"- Started: {_data.startedAt}  Duration: {_data.durationSeconds:0}s");
            sb.AppendLine($"- End reason: {_data.resultReason}");
            sb.AppendLine();
            sb.AppendLine("| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            sb.AppendLine($"| {_data.exceptionCount} | {_data.errorCount} | {_data.stallCount} | {_data.stepFailedCount} | {_data.invariantCount} | {_data.assistCount} | {_data.warningCount} |");
            sb.AppendLine();
            sb.AppendLine($"Ignored known-noise log lines: {_data.ignoredLogCount}");
            sb.AppendLine();
            sb.AppendLine("## Days");
            sb.AppendLine();
            sb.AppendLine("| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (AutopilotDaySummary d in _data.days)
                sb.AppendLine($"| {d.day} | {d.outcome} | {d.endedAt - d.startedAt:0}s | {d.suspectsProcessed} | {d.errors} | {d.stalls} | {d.stepFailures} | {d.assists} |");
            sb.AppendLine();

            string[] order = { "Exception", "Error", "Stall", "StepFailed", "Invariant", "Assist", "Warning", "Info" };
            foreach (string kind in order)
            {
                List<AutopilotFinding> group = _data.findings.Where(f => f.kind == kind).ToList();
                if (group.Count == 0) continue;
                sb.AppendLine($"## {kind} ({group.Count} unique)");
                sb.AppendLine();

                if (kind == nameof(FindingKind.Warning) || kind == nameof(FindingKind.Info))
                {
                    foreach (AutopilotFinding f in group.OrderByDescending(f => f.count).Take(40))
                        sb.AppendLine($"- ({f.count}x, Day {f.day}) {f.title.Replace('\n', ' ')}");
                    sb.AppendLine();
                    continue;
                }
                foreach (AutopilotFinding f in group)
                {
                    sb.AppendLine($"### [{f.severity}] {f.title}");
                    sb.AppendLine($"Day {f.day}, {f.phase}, first at {f.firstSeenAt:0.0}s, seen {f.count}x");
                    if (!string.IsNullOrEmpty(f.detail)) { sb.AppendLine(); sb.AppendLine("```"); sb.AppendLine(f.detail); sb.AppendLine("```"); }
                    if (!string.IsNullOrEmpty(f.stack)) { sb.AppendLine(); sb.AppendLine("```"); sb.AppendLine(Trim(f.stack, 1200)); sb.AppendLine("```"); }
                    if (!string.IsNullOrEmpty(f.screenshot)) sb.AppendLine($"Screenshot: `{Path.GetFileName(f.screenshot)}`");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (string line in _data.timeline) sb.AppendLine(line);
            sb.AppendLine("```");
            return sb.ToString();
        }

        private string CaptureScreenshot(string tag)
        {
            try
            {
                string file = Path.Combine(Directory, $"{++_screenshotIndex:000}_{tag}.png");
                ScreenCapture.CaptureScreenshot(file);
                return file;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private int SafeDay()
        {
            try { return DayProvider(); } catch { return -1; }
        }

        private string SafePhase()
        {
            try { return PhaseProvider(); } catch { return "?"; }
        }

        private static string MakeKey(FindingKind kind, string title, string stack)
        {
            string firstFrame = string.Empty;
            if (!string.IsNullOrEmpty(stack))
            {
                int nl = stack.IndexOf('\n');
                firstFrame = nl > 0 ? stack.Substring(0, nl) : stack;
            }
            return kind + "|" + title + "|" + firstFrame;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
#endif
