#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>
    /// Continuous sanity checks evaluated about once per second while the autopilot runs. Each check
    /// has a grace period, so transient states (fades, transitions) don't trigger false positives.
    /// Add new checks in <see cref="RegisterChecks"/>.
    /// </summary>
    public class AutopilotInvariants
    {
        private class Check
        {
            public string Id;
            public string Severity;
            public float GraceSeconds;
            public Func<string> Evaluate; // returns null when healthy, otherwise a description
            public float FailingSince = -1f;
            public bool Reported;
        }

        private readonly AutopilotReport _report;
        private readonly List<Check> _checks = new List<Check>();
        private float _nextEval;
        private int _lastDay = -1;
        private float _dayChangedAt;

        public AutopilotInvariants(AutopilotReport report)
        {
            _report = report;
            RegisterChecks();
        }

        private void RegisterChecks()
        {
            Add("player-missing", "high", 8f, () =>
                AutopilotGame.GameStarted && AutopilotGame.Player == null
                    ? "Game has started but no local PlayerInstance exists." : null);

            Add("active-day-count", "high", 5f, () =>
            {
                if (!AutopilotGame.GameStarted || CampaignManager.Instance == null || InDayTransition) return null;
                int active = UnityEngine.Object.FindObjectsByType<DayBase>(FindObjectsSortMode.None).Length;
                return active == 1 ? null : $"Expected exactly one active DayBase, found {active}.";
            });

            Add("active-day-mismatch", "high", 5f, () =>
            {
                if (!AutopilotGame.GameStarted || CampaignManager.Instance == null || InDayTransition) return null;
                DayBase day = CampaignManager.Instance.ActiveDay;
                if (day == null) return "CampaignManager.ActiveDay is null after game start.";
                if (AutopilotGame.CampaignComplete) return null;
                return day.DayNumber == CampaignManager.Instance.CurrentDay
                    ? null
                    : $"ActiveDay.DayNumber={day.DayNumber} but CurrentDay={CampaignManager.Instance.CurrentDay}.";
            });

            Add("duplicate-singletons", "high", 2f, () =>
            {
                var dupes = new List<string>();
                CountAtMostOne<ShiftManager>(dupes);
                CountAtMostOne<CampaignManager>(dupes);
                CountAtMostOne<SuspectController>(dupes);
                CountAtMostOne<GameManager>(dupes);
                CountAtMostOne<TimecardMachine>(dupes);
                return dupes.Count == 0 ? null : "Duplicate managers: " + string.Join(", ", dupes);
            });

            Add("control-locked", "medium", 25f, () =>
            {
                if (!AutopilotGame.GameStarted || AutopilotGame.Player == null) return null;
                if (AutopilotGame.CanControl || AutopilotGame.AnyModalActive) return null;
                return "Player control has been disabled with no report, dialogue, cutscene, pause or interaction lock active.";
            });

            Add("interaction-locked", "medium", 45f, () =>
            {
                if (!AutopilotGame.GameStarted || AutopilotGame.Interaction == null) return null;
                if (AutopilotGame.CanInteract || AutopilotGame.ReportVisible || AutopilotGame.InDialogue ||
                    AutopilotGame.InCutscene || AutopilotGame.Paused) return null;
                return "PlayerInteractionController.CanInteract has stayed false with no modal state active.";
            });

            Add("fell-out-of-world", "high", 1f, () =>
            {
                PlayerInstance p = AutopilotGame.Player;
                if (p == null) return null;
                Vector3 pos = p.transform.position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) return "Player position is NaN.";
                return pos.y < -100f ? $"Player fell out of the world (y={pos.y:0})." : null;
            });

            Add("timescale-zero", "medium", 15f, () =>
                Time.timeScale == 0f && !AutopilotGame.Paused ? "Time.timeScale is 0 while the game is not paused." : null);

            Add("cursor-in-gameplay", "low", 10f, () =>
            {
                if (!AutopilotGame.GameStarted || !AutopilotGame.CanControl || AutopilotGame.AnyModalActive) return null;
                return Cursor.visible ? "Cursor is visible during free gameplay (player has control, no modal)." : null;
            });

            Add("report-stuck", "high", 120f, () =>
                AutopilotGame.ReportVisible ? "End-of-shift report has been on screen for over 2 minutes." : null);

            Add("verdict-block-leak", "medium", 90f, () =>
                HandOffPoint.BlockVerdict && AutopilotGame.CurrentDay != 1
                    ? "HandOffPoint.BlockVerdict is still true outside Day 1 (deferred verdict never cleared)." : null);

            Add("held-item-on-report", "low", 5f, () =>
                AutopilotGame.ReportVisible && AutopilotGame.Held != null
                    ? $"Player is still holding '{AutopilotGame.Held.name}' while the end-of-shift report is shown." : null);
        }

        private bool InDayTransition => Time.realtimeSinceStartup - _dayChangedAt < 6f;

        private void Add(string id, string severity, float grace, Func<string> evaluate)
        {
            _checks.Add(new Check { Id = id, Severity = severity, GraceSeconds = grace, Evaluate = evaluate });
        }

        private static void CountAtMostOne<T>(List<string> dupes) where T : UnityEngine.Object
        {
            int n = UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None).Length;
            if (n > 1) dupes.Add($"{typeof(T).Name} x{n}");
        }

        public void Tick()
        {
            int day = AutopilotGame.CurrentDay;
            if (day != _lastDay)
            {
                _lastDay = day;
                _dayChangedAt = Time.realtimeSinceStartup;
            }

            if (Time.realtimeSinceStartup < _nextEval) return;
            _nextEval = Time.realtimeSinceStartup + 1f;

            float now = Time.realtimeSinceStartup;
            foreach (Check check in _checks)
            {
                string failure;
                try
                {
                    failure = check.Evaluate();
                }
                catch (Exception e)
                {
                    failure = null;
                    _report.Add(FindingKind.Info, "low", $"Invariant '{check.Id}' threw {e.GetType().Name}", e.Message, screenshot: false);
                }

                if (failure == null)
                {
                    if (check.Reported)
                        _report.Timeline($"Invariant '{check.Id}' recovered.");
                    check.FailingSince = -1f;
                    check.Reported = false;
                    continue;
                }

                if (check.FailingSince < 0f) check.FailingSince = now;
                if (check.Reported || now - check.FailingSince < check.GraceSeconds) continue;

                check.Reported = true;
                _report.Add(FindingKind.Invariant, check.Severity, $"[{check.Id}] {failure}", AutopilotGame.Snapshot());
            }
        }
    }
}
#endif
