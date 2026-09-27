#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>
    /// Automated playtester. Created by the editor launcher (Good Cop Bad Cop ▸ Autopilot) when Play
    /// Mode starts, it plays the campaign day by day and writes a report of every exception, error,
    /// stall (soft-lock), failed step and invariant violation it encounters.
    ///
    /// Structure:
    /// <list type="bullet">
    /// <item><b>Brain</b> — an ordered list of behaviours (report, dialogue, clock in/out, bell, suspect,
    /// tutorial arrows, sleep…). Each tick the first applicable behaviour runs to completion.</item>
    /// <item><b>Watchdog</b> — compares <see cref="AutopilotGame.ProgressSignature"/> every frame; when it
    /// does not change for <see cref="AutopilotConfig.stallSeconds"/> a Stall is reported and the
    /// fallback ladder escalates one level (UI unstick → advance shift → complete tasks → end day → skip day)
    /// so one bug doesn't end the run.</item>
    /// <item><b>Actor</b> — performs interactions directly (Flow) or with a virtual gamepad (Input).</item>
    /// </list>
    /// </summary>
    public class AutopilotRunner : MonoBehaviour
    {
        public const string PendingConfigKey = "GCBC.Autopilot.PendingConfig";
        public const string LastResultKey = "GCBC.Autopilot.LastResult";

        [Serializable]
        public class RunResult
        {
            public string reportPath;
            public bool passed;
            public string reason;
            public bool exitEditor;
        }

        public static AutopilotRunner Active { get; private set; }

        private AutopilotConfig _config;
        private AutopilotReport _report;
        private AutopilotInvariants _invariants;
        private AutopilotInputDriver _input;
        private AutopilotActor _actor;
        private System.Random _rng;

        private int _lastDay;
        private float _runStartedAt;
        private float _dayStartedAt;
        private bool _finished;

        // Watchdog
        private string _lastSignature;
        private float _lastProgressAt;
        private int _stallLevel;
        private bool _fallbackPending;

        // Behaviour bookkeeping
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        private readonly Dictionary<int, int> _suspectAttempts = new Dictionary<int, int>();
        private readonly Dictionary<EntityId, int> _tutorialAttempts = new Dictionary<EntityId, int>();
        private int _arrivedSuspectIndex = -1;
        private float _suspectIndexChangedAt;
        private int _observedSuspectIndex = -2;
        private bool _boothNeedsPlayer;
        private float _reportSeenAt = -1f;
        private bool _reportContinuePressed;
        private float _postShiftSince = -1f;
        private int _taskAssistDay = -1;
        private float _bellNotReadySince = -1f;
        private int _smokeDay = -1;
        private float _smokeShiftStartedAt = -1f;
        private bool _smokeForcedEnd;

        // ── Launch ──────────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LaunchPendingRun()
        {
            string json = UnityEditor.SessionState.GetString(PendingConfigKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            UnityEditor.SessionState.EraseString(PendingConfigKey);
            Launch(AutopilotConfig.FromJson(json));
        }

        public static AutopilotRunner Launch(AutopilotConfig config)
        {
            if (Active != null) return Active;
            var go = new GameObject("[Autopilot]");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<AutopilotRunner>();
            runner._config = config;
            return runner;
        }

        private void Awake()
        {
            Active = this;
        }

        private void Start()
        {
            _config ??= new AutopilotConfig();
            _rng = new System.Random(_config.seed != 0 ? _config.seed : Environment.TickCount);
            _report = new AutopilotReport(_config)
            {
                DayProvider = () => AutopilotGame.CurrentDay,
                PhaseProvider = () => AutopilotGame.Phase,
                CaptureScreenshots = _config.screenshotsOnFindings
            };
            _invariants = new AutopilotInvariants(_report);
            _input = new AutopilotInputDriver();
            _actor = new AutopilotActor(_config, _report, _input);
            _runStartedAt = Time.realtimeSinceStartup;
            _lastProgressAt = Time.realtimeSinceStartup;

            SuspectController.OnSuspectArrived += HandleSuspectArrived;
            SuspectController.OnSuspectWaitingAtBooth += HandleSuspectWaitingAtBooth;
            SuspectController.OnBoothBecameReady += HandleBoothReady;
            SuspectController.OnCurrentSuspectDespawned += HandleSuspectDespawned;

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            SuspectController.OnSuspectArrived -= HandleSuspectArrived;
            SuspectController.OnSuspectWaitingAtBooth -= HandleSuspectWaitingAtBooth;
            SuspectController.OnBoothBecameReady -= HandleBoothReady;
            SuspectController.OnCurrentSuspectDespawned -= HandleSuspectDespawned;

            if (!_finished && _report != null)
                Finish("Play Mode exited before the run finished.", stopPlayMode: false);

            _input?.Dispose();
            if (Active == this) Active = null;
        }

        private void HandleSuspectArrived(int index) => _arrivedSuspectIndex = index;
        private void HandleSuspectWaitingAtBooth() => _boothNeedsPlayer = true;
        private void HandleBoothReady() => _boothNeedsPlayer = false;
        private void HandleSuspectDespawned() => _arrivedSuspectIndex = -1;

        // ── Per-frame ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (_finished) return;

            _report.DrainLogs();
            _invariants.Tick();
            _input.Tick();

            if (_config.timeScale > 0f && !Mathf.Approximately(_config.timeScale, 1f) &&
                Mathf.Approximately(Time.timeScale, 1f))
                Time.timeScale = _config.timeScale;

            int index = AutopilotGame.SuspectIndex;
            if (index != _observedSuspectIndex)
            {
                _observedSuspectIndex = index;
                _suspectIndexChangedAt = Time.realtimeSinceStartup;
            }

            _report.SetDaySuspects(AutopilotGame.SuspectsProcessed);
            Watchdog();

            if (Time.realtimeSinceStartup - _runStartedAt > _config.maxRunMinutes * 60f)
                Finish($"Run exceeded {_config.maxRunMinutes} minutes.");
        }

        private void Watchdog()
        {
            if (!AutopilotGame.GameStarted) return;

            string signature = AutopilotGame.ProgressSignature();
            float now = Time.realtimeSinceStartup;
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                _lastProgressAt = now;
                if (_stallLevel > 0) _report.Timeline($"Progress resumed (fallback level was {_stallLevel}).");
                _stallLevel = 0;
                return;
            }

            float limit = _config.stallSeconds * (AutopilotGame.InCutscene || AutopilotGame.InDialogue ? 2f : 1f);
            if (now - _lastProgressAt < limit || _fallbackPending) return;

            _report.Add(FindingKind.Stall, "high",
                $"No game progress for {limit:0}s (escalation level {_stallLevel})",
                AutopilotGame.Snapshot());
            _lastProgressAt = now;
            _fallbackPending = true;
        }

        // ── Main loop ───────────────────────────────────────────────────────────

        private IEnumerator Run()
        {
            _report.Timeline($"Autopilot started — mode={_config.mode} days={_config.startDay}..{(_config.endDay > 0 ? _config.endDay.ToString() : "last")} verdicts={_config.verdictPolicy}");

            var boot = new ActionResult();
            yield return AutopilotActor.WaitFor(() =>
                    AutopilotGame.GameStarted && AutopilotGame.Player != null &&
                    CampaignManager.Instance != null && CampaignManager.Instance.ActiveDay != null,
                180f, boot, "Game did not reach a playable state within 180s (host start / player spawn / day activation).");
            if (!boot.Success)
            {
                _report.Add(FindingKind.StepFailed, "high", boot.Failure, AutopilotGame.Snapshot());
                Finish("Boot failed.");
                yield break;
            }

            yield return AutopilotActor.WaitSeconds(2f);

            if (_config.startDay > 1 && AutopilotGame.CurrentDay != _config.startDay && DebugConsole.Instance != null)
            {
                _report.Timeline($"Jumping to Day {_config.startDay}.");
                DebugConsole.Instance.SkipToDay(_config.startDay);
                var jump = new ActionResult();
                yield return AutopilotActor.WaitFor(() => AutopilotGame.CurrentDay == _config.startDay, 20f, jump,
                    $"SkipToDay({_config.startDay}) did not activate the day.");
                if (!jump.Success) _report.Add(FindingKind.StepFailed, "high", jump.Failure, AutopilotGame.Snapshot());
                yield return AutopilotActor.WaitSeconds(1f);
            }

            List<int> days = AutopilotGame.AuthoredDays();
            int lastDay = _config.endDay > 0 ? _config.endDay : (days.Count > 0 ? days.Max() : _config.startDay);
            _report.Timeline($"Authored days in scene: [{string.Join(", ", days)}] — playing until Day {lastDay}.");

            if (_config.mode == AutopilotMode.Input)
            {
                _input.Enable();
                _report.Timeline("Virtual gamepad attached.");
            }

            _lastDay = AutopilotGame.CurrentDay;
            _dayStartedAt = Time.realtimeSinceStartup;
            _report.BeginDay(_lastDay);

            while (!_finished)
            {
                // Day bookkeeping. CampaignManager advances the day when the report is broadcast, so a new
                // day only "begins" for the bot once that report has been dismissed.
                int day = AutopilotGame.CurrentDay;
                if (day != _lastDay && !AutopilotGame.ReportVisible)
                {
                    _report.EndDay("completed");
                    ResetDayState();
                    _lastDay = day;

                    if (AutopilotGame.CampaignComplete)
                    {
                        Finish("Campaign complete.");
                        yield break;
                    }

                    if (day > lastDay)
                    {
                        yield return AutopilotActor.WaitSeconds(5f); // observe the transition into the next day
                        Finish($"Reached the configured end (Day {lastDay} completed).");
                        yield break;
                    }

                    _report.BeginDay(day);
                    _dayStartedAt = Time.realtimeSinceStartup;
                }

                if (_fallbackPending)
                {
                    _fallbackPending = false;
                    yield return RunFallback(lastDay);
                    _stallLevel++;
                    continue;
                }

                if (Time.realtimeSinceStartup - _dayStartedAt > _config.maxDayMinutes * 60f)
                {
                    _report.Add(FindingKind.StepFailed, "high", $"Day {day} exceeded {_config.maxDayMinutes} minutes — forcing it to end.",
                        AutopilotGame.Snapshot());
                    _dayStartedAt = Time.realtimeSinceStartup;
                    _stallLevel = Mathf.Max(_stallLevel, 3);
                    yield return RunFallback(lastDay);
                    continue;
                }

                IEnumerator step = NextBehaviour();
                if (step != null) yield return step;
                else yield return AutopilotActor.WaitSeconds(0.25f);
            }
        }

        private void ResetDayState()
        {
            _suspectAttempts.Clear();
            _tutorialAttempts.Clear();
            _postShiftSince = -1f;
            _reportSeenAt = -1f;
            _reportContinuePressed = false;
            _bellNotReadySince = -1f;
            _smokeShiftStartedAt = -1f;
            _smokeForcedEnd = false;
            _stallLevel = 0;
        }

        // ── Brain ───────────────────────────────────────────────────────────────

        private IEnumerator NextBehaviour()
        {
            if (!AutopilotGame.GameStarted || AutopilotGame.Player == null) return null;

            if (AutopilotGame.ReportVisible) return ReportBehaviour();
            _reportSeenAt = -1f;
            _reportContinuePressed = false;

            if (AutopilotGame.Paused) return null;
            if (AutopilotGame.ChoicePanelVisible) return DialogueChoiceBehaviour();

            EndDayPopupUI popup = FindFirstObjectByType<EndDayPopupUI>();
            if (popup != null && popup.isActiveAndEnabled) return EndDayPopupBehaviour(popup);

            if (_config.mode == AutopilotMode.Smoke)
                return SmokeBehaviour();

            if (AutopilotGame.ClockOutArmed && Ready("clockout", 6f)) return ClockOutBehaviour();
            if (TimecardMachine.HasClockedOutThisCycle && Ready("sleep", 15f)) return SleepBehaviour();
            if (AutopilotGame.ClockInArmed && !AutopilotGame.ShiftStarted && Ready("clockin", 6f)) return ClockInBehaviour();
            if (SuspectNeedsProcessing(out SuspectCharacter suspect)) return ProcessSuspectBehaviour(suspect);
            if (BellShouldBePressed() && Ready("bell", 4f)) return BellBehaviour();

            IEnumerator tutorial = TutorialMarkerBehaviour();
            if (tutorial != null) return tutorial;

            return TaskGraceBehaviour();
        }

        private bool Ready(string key, float cooldown)
        {
            float now = Time.realtimeSinceStartup;
            if (_cooldowns.TryGetValue(key, out float until) && now < until) return false;
            _cooldowns[key] = now + cooldown;
            return true;
        }

        private IEnumerator ReportBehaviour()
        {
            if (_reportSeenAt < 0f)
            {
                _reportSeenAt = Time.realtimeSinceStartup;
                _report.MarkCurrentDayOutcome("completed");
                _report.Timeline($"End-of-shift report shown (processed={AutopilotGame.SuspectsProcessed}).");
            }

            if (_reportContinuePressed || Time.realtimeSinceStartup - _reportSeenAt < 4f)
            {
                yield return AutopilotActor.WaitSeconds(0.5f);
                yield break;
            }

            EndOfShiftReportUI reportUI = FindFirstObjectByType<EndOfShiftReportUI>();
            if (reportUI == null)
            {
                _report.Add(FindingKind.StepFailed, "high", "Report is visible but EndOfShiftReportUI could not be found.", AutopilotGame.Snapshot());
                yield return AutopilotActor.WaitSeconds(2f);
                yield break;
            }

            _reportContinuePressed = true;
            _report.Timeline("Pressing Continue on the report.");
            reportUI.OnContinueButtonPressed();

            var closed = new ActionResult();
            yield return AutopilotActor.WaitFor(() => !AutopilotGame.ReportVisible, 25f, closed,
                "End-of-shift report did not close within 25s of pressing Continue.");
            if (!closed.Success)
                _report.Add(FindingKind.StepFailed, "high", closed.Failure, AutopilotGame.Snapshot());
        }

        private IEnumerator DialogueChoiceBehaviour()
        {
            yield return AutopilotActor.WaitSeconds(1.2f);
            if (!AutopilotGame.ChoicePanelVisible) yield break;

            if (_config.mode == AutopilotMode.Input && _input.IsEnabled)
            {
                _input.Press(GamepadButton.South);
                yield return AutopilotActor.WaitSeconds(1f);
                if (!AutopilotGame.ChoicePanelVisible) { _report.Timeline("Dialogue choice confirmed with gamepad."); yield break; }
                _report.Add(FindingKind.StepFailed, "medium", "Gamepad South did not confirm the pre-selected dialogue choice.", AutopilotGame.Snapshot());
            }

            int choice = _config.verdictPolicy == VerdictPolicy.Random ? _rng.Next(0, 2) : 0;
            _report.Timeline($"Choosing dialogue option {choice}.");
            DialogueChoiceSystem.Instance.ChooseDialogueChoice(choice);

            var gone = new ActionResult();
            yield return AutopilotActor.WaitFor(() => !AutopilotGame.ChoicePanelVisible, 6f, gone, "");
            if (!gone.Success && Ready("choice-stuck", 20f))
                _report.Add(FindingKind.Info, "low", "Dialogue choice panel stayed open after choosing (may be waiting for the other speaker).", screenshot: false);
        }

        private IEnumerator EndDayPopupBehaviour(EndDayPopupUI popup)
        {
            yield return AutopilotActor.WaitSeconds(0.8f);
            if (popup == null || !popup.isActiveAndEnabled) yield break;

            if (TimecardMachine.HasClockedOutThisCycle || BunkBedInteractable.ForceAllowSleep)
            {
                _report.Timeline("Confirming 'End the day?'.");
                popup.OnConfirmClicked();
            }
            else
            {
                _report.Timeline("Bed popup is in blocked state — cancelling.");
                popup.OnCancelClicked();
            }
            yield return AutopilotActor.WaitSeconds(1f);
        }

        private IEnumerator ClockInBehaviour()
        {
            TimecardMachine timecard = AutopilotGame.Timecard;
            _report.Timeline("Clocking in.");
            var r = new ActionResult();
            yield return _actor.Interact(timecard, "Clock in", r);
            if (!r.Success) { _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot()); yield break; }

            yield return AutopilotActor.WaitFor(() => AutopilotGame.ShiftStarted || !AutopilotGame.ClockInArmed, 8f, r,
                "Clock-in punch did not start the shift or disarm the machine.");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
        }

        private IEnumerator ClockOutBehaviour()
        {
            TimecardMachine timecard = AutopilotGame.Timecard;
            _report.Timeline("Clocking out.");
            var r = new ActionResult();
            yield return _actor.Interact(timecard, "Clock out", r);
            if (!r.Success) { _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot()); yield break; }

            yield return AutopilotActor.WaitFor(() => TimecardMachine.HasClockedOutThisCycle || !AutopilotGame.ClockOutArmed, 8f, r,
                "Clock-out punch did not register (HasClockedOutThisCycle stayed false).");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
        }

        private IEnumerator SleepBehaviour()
        {
            BunkBedInteractable bed = FindFirstObjectByType<BunkBedInteractable>();
            _report.Timeline("Going to bed.");
            var r = new ActionResult();
            yield return _actor.Interact(bed, "Use bunk bed", r);
            if (!r.Success) { _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot()); yield break; }

            EndDayPopupUI popup = null;
            yield return AutopilotActor.WaitFor(() => (popup = FindFirstObjectByType<EndDayPopupUI>()) != null && popup.isActiveAndEnabled,
                4f, r, "Bed interaction did not open the 'End the day?' popup.");
            if (!r.Success) { _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot()); yield break; }

            yield return AutopilotActor.WaitSeconds(0.8f);
            popup.OnConfirmClicked();

            yield return AutopilotActor.WaitFor(() => AutopilotGame.ReportVisible, 20f, r,
                "Confirming sleep did not show the end-of-shift report within 20s.");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
        }

        private bool BellShouldBePressed()
        {
            if (!AutopilotGame.ShiftStarted || !ShiftManager.NextSuspectReadyForBell) { _bellNotReadySince = -1f; return false; }
            if (SuspectController.Instance == null || SuspectController.Instance.HasEntityAtWindow) return false;

            SwitchButton button = FindFirstObjectByType<SwitchButton>();
            if (button == null) return false;
            if (button.buttonReady) { _bellNotReadySince = -1f; return true; }

            if (_bellNotReadySince < 0f) _bellNotReadySince = Time.realtimeSinceStartup;
            else if (Time.realtimeSinceStartup - _bellNotReadySince > 12f && Ready("bell-desync", 120f))
                _report.Add(FindingKind.Invariant, "medium",
                    "[bell-desync] ShiftManager.NextSuspectReadyForBell is true but SwitchButton.buttonReady stayed false for 12s.",
                    AutopilotGame.Snapshot());
            return false;
        }

        private IEnumerator BellBehaviour()
        {
            SwitchButton button = FindFirstObjectByType<SwitchButton>();
            int before = AutopilotGame.SuspectIndex;
            _report.Timeline("Pressing the switch to call the next suspect.");
            var r = new ActionResult();
            yield return _actor.Interact(button, "Press next-suspect switch", r);
            if (!r.Success) { _report.Add(FindingKind.StepFailed, "medium", r.Failure, AutopilotGame.Snapshot()); yield break; }

            yield return AutopilotActor.WaitFor(() => AutopilotGame.SuspectIndex != before, 8f, r,
                "Pressing the ready switch did not summon the next suspect.");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
            yield return AutopilotActor.WaitSeconds(1.5f);
        }

        private IEnumerator TutorialMarkerBehaviour()
        {
            foreach (Transform target in AutopilotGame.MarkedTutorialTargets())
            {
                Interactable interactable = AutopilotGame.ResolveInteractable(target);
                PlacementBoard board = target.GetComponentInChildren<PlacementBoard>();
                if (interactable == null && board == null) continue;

                EntityId key = (interactable != null ? (UnityEngine.Object)interactable : board).GetEntityId();
                _tutorialAttempts.TryGetValue(key, out int attempts);
                if (attempts >= 3 || !Ready("tutorial-" + key, 8f)) continue;

                if (interactable != null && (interactable == AutopilotGame.Held ||
                                             interactable is TimecardMachine || interactable is BunkBedInteractable))
                    continue;

                _tutorialAttempts[key] = attempts + 1;
                return FollowTutorialTarget(target, interactable, board);
            }
            return null;
        }

        private IEnumerator FollowTutorialTarget(Transform target, Interactable interactable, PlacementBoard board)
        {
            var r = new ActionResult();
            if (AutopilotGame.Held != null && board != null)
            {
                _report.Timeline($"Tutorial arrow → placing held '{AutopilotGame.Held.name}' on '{board.name}'.");
                yield return _actor.PlaceHeld(board.transform.position, Vector3.up, board, $"Tutorial: place on {board.name}", r);
            }
            else if (interactable != null && interactable.IsInteractable)
            {
                _report.Timeline($"Tutorial arrow → interacting with '{interactable.name}'.");
                bool holdingCompatible = AutopilotGame.Held != null && interactable.itemsThatCanInteractWith != null &&
                                         interactable.itemsThatCanInteractWith.Contains(AutopilotGame.Held.ItemData);
                if (holdingCompatible) yield return _actor.UseHeldOn(interactable, $"Tutorial: use held on {interactable.name}", r);
                else yield return _actor.Interact(interactable, $"Tutorial: {interactable.name}", r);
            }
            else yield break;

            if (!r.Success)
                _report.Add(FindingKind.Info, "low", $"Tutorial target '{target.name}' could not be actioned", r.Failure, screenshot: false);
            yield return AutopilotActor.WaitSeconds(1.5f);
        }

        private IEnumerator TaskGraceBehaviour()
        {
            bool postShift = ShiftManager.Instance != null && ShiftManager.Instance.CurrentPhase == ShiftManager.DayPhase.PostShift;
            if (!postShift || AutopilotGame.ClockOutArmed || TimecardMachine.HasClockedOutThisCycle)
            {
                _postShiftSince = -1f;
                return null;
            }

            if (_postShiftSince < 0f) _postShiftSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _postShiftSince < _config.taskGraceSeconds || _taskAssistDay == AutopilotGame.CurrentDay)
                return null;

            _taskAssistDay = AutopilotGame.CurrentDay;
            return ForceTasks();
        }

        private IEnumerator ForceTasks()
        {
            _report.Add(FindingKind.Assist, "low",
                $"Day {AutopilotGame.CurrentDay}: clock-out not armed {_config.taskGraceSeconds:0}s after the last suspect — force-completing daily tasks (bot cannot perform open-world tasks yet).",
                AutopilotGame.Snapshot(), screenshot: false);
            BetweenShiftTaskManager.Instance?.ForceCompleteAllTasks();
            ShiftManager.Instance?.ForceCompleteAllTasksServerRpc();
            ShiftManager.Instance?.RecheckClockOutGate();

            var r = new ActionResult();
            yield return AutopilotActor.WaitFor(() => AutopilotGame.ClockOutArmed, 10f, r, "");
            if (!r.Success)
            {
                _report.Add(FindingKind.Assist, "low", "Clock-out still not armed after force-completing tasks — priming it with DebugEnableClockOut.",
                    AutopilotGame.Snapshot(), screenshot: false);
                ShiftManager.Instance?.DebugEnableClockOut();
            }
        }

        // ── Suspect processing ──────────────────────────────────────────────────

        private bool SuspectNeedsProcessing(out SuspectCharacter suspect)
        {
            suspect = null;
            SuspectController sc = SuspectController.Instance;
            if (sc == null || !AutopilotGame.ShiftStarted || sc.CurrentSuspect == null) return false;

            int index = sc.SuspectIndex;
            bool arrived = _arrivedSuspectIndex == index || Time.realtimeSinceStartup - _suspectIndexChangedAt > 25f;
            if (!arrived) return false;

            _suspectAttempts.TryGetValue(index, out int attempts);
            if (attempts >= 2 || !Ready("suspect", 5f)) return false;

            suspect = sc.CurrentSuspect;
            return true;
        }

        private IEnumerator ProcessSuspectBehaviour(SuspectCharacter suspect)
        {
            SuspectController sc = SuspectController.Instance;
            int index = sc.SuspectIndex;
            _suspectAttempts[index] = (_suspectAttempts.TryGetValue(index, out int a) ? a : 0) + 1;
            int processedBefore = AutopilotGame.SuspectsProcessed;
            StampContainer.StampType verdict = DecideVerdict(suspect);
            string who = $"suspect #{index} '{suspect.name}' (infected={suspect.IsInfected})";
            _report.Timeline($"Processing {who} → {verdict}.");

            if (_boothNeedsPlayer || _config.mode == AutopilotMode.Flow)
                yield return EnsureInBooth();

            HandOffPoint handOff = AutopilotGame.Nearest<HandOffPoint>(suspect.transform.position);
            if (handOff == null)
            {
                _report.Add(FindingKind.StepFailed, "high", $"No HandOffPoint found for {who}.", AutopilotGame.Snapshot());
                yield break;
            }

            var r = new ActionResult();

            // 1. Free hands of anything unrelated.
            if (AutopilotGame.Held != null && !(AutopilotGame.Held is FolderController))
            {
                Vector3 p = DeskPoint(handOff, out Vector3 n);
                yield return _actor.PlaceHeld(p, n, null, $"Put down '{AutopilotGame.Held.name}'", r);
                yield return AutopilotActor.WaitSeconds(0.5f);
            }

            // 2. Get a folder.
            FolderController folder = AutopilotGame.Held as FolderController;
            if (folder == null)
                folder = AutopilotGame.Nearest<FolderController>(handOff.transform.position,
                    f => !f.IsHandedOff && !f.IsHeldByOtherPlayer && (f.transform.position - handOff.transform.position).magnitude < 6f);

            if (folder == null)
            {
                StackOfFolders stack = AutopilotGame.Nearest<StackOfFolders>(handOff.transform.position);
                yield return _actor.Interact(stack, "Take a folder from the stack", r);
                if (r.Success)
                    yield return AutopilotActor.WaitFor(() => AutopilotGame.Held is FolderController, 5f, r,
                        "Taking a folder from the stack did not put a folder in hand.");
                if (!r.Success) { StepFailed(r.Failure, who); yield break; }
                folder = (FolderController)AutopilotGame.Held;
            }

            // 3. Stamp it on the desk.
            if (!folder.IsStamped)
            {
                if (AutopilotGame.Held == folder)
                {
                    Vector3 desk = DeskPoint(handOff, out Vector3 normal);
                    yield return _actor.PlaceHeld(desk, normal, null, "Put the folder on the desk", r);
                    if (!r.Success) { StepFailed(r.Failure, who); yield break; }
                    yield return AutopilotActor.WaitFor(() => !folder.IsHeld, 4f, r, "Folder was still held after placing it on the desk.");
                    if (!r.Success) { StepFailed(r.Failure, who); yield break; }
                    yield return AutopilotActor.WaitSeconds(0.8f);
                }

                InkStamp station = FindStamp(verdict, out bool exact);
                if (station == null)
                {
                    StepFailed("No ink stamp is available in its slot and interactable (all stamps locked or missing).", who);
                    yield break;
                }
                if (!exact)
                    _report.Add(FindingKind.Info, "low", $"Wanted {verdict} stamp but it was unavailable; used {station.StampType}.", screenshot: false);

                yield return _actor.Interact(station, $"Pick up {station.StampType} stamp", r);
                if (r.Success)
                    yield return AutopilotActor.WaitFor(() => AutopilotGame.Held is InkStampPickup, 5f, r,
                        $"Interacting with the {station.StampType} stamp holder did not put the stamp in hand.");
                if (!r.Success) { StepFailed(r.Failure, who); yield break; }

                yield return _actor.UseHeldOn(folder, "Stamp the folder", r);
                if (r.Success)
                    yield return AutopilotActor.WaitFor(() => folder != null && folder.IsStamped, 6f, r,
                        "Using the stamp on the folder did not stamp it (face-down, open, out of ink, or quarantine full?).");
                if (!r.Success)
                {
                    StepFailed(r.Failure, who);
                    yield return ReturnStamp(station);
                    yield break;
                }

                yield return AutopilotActor.WaitFor(() => folder == null || folder.IsInteractable || folder.IsHandedOff, 8f, r,
                    "Folder stayed non-interactable 8s after stamping (stamp sequence did not finish).");
                if (!r.Success) StepFailed(r.Failure, who);
                yield return AutopilotActor.WaitSeconds(0.5f);

                yield return ReturnStamp(station);
            }

            // 4. Hand it off at the window.
            if (folder != null && !folder.IsHandedOff)
            {
                if (AutopilotGame.Held != folder)
                {
                    yield return _actor.Interact(folder, "Pick up the stamped folder", r);
                    if (r.Success)
                        yield return AutopilotActor.WaitFor(() => AutopilotGame.Held == folder, 5f, r,
                            "Picking up the stamped folder failed.");
                    if (!r.Success) { StepFailed(r.Failure, who); yield break; }
                }

                yield return _actor.PlaceHeld(handOff.transform.position, Vector3.up, handOff, "Hand the folder off at the window", r);
                if (r.Success)
                    yield return AutopilotActor.WaitFor(() => folder == null || folder.IsHandedOff || HandOffPoint.PendingVerdictFolder == folder,
                        6f, r, "Placing the stamped folder on the window did not hand it off.");
                if (!r.Success) { StepFailed(r.Failure, who); yield break; }
            }

            // 5. Suspect should leave.
            float timeout = HandOffPoint.BlockVerdict ? 60f : 30f;
            yield return AutopilotActor.WaitFor(() =>
                    sc.SuspectIndex != index || sc.CurrentSuspect != suspect || AutopilotGame.SuspectsProcessed > processedBefore,
                timeout, r, $"Verdict was handed off but the suspect was not processed within {timeout:0}s.");
            if (!r.Success) { StepFailed(r.Failure, who); yield break; }

            _report.Timeline($"Processed {who}.");
        }

        private IEnumerator ReturnStamp(InkStamp station)
        {
            if (!(AutopilotGame.Held is InkStampPickup)) yield break;
            var r = new ActionResult();
            yield return _actor.UseHeldOn(station, "Return the stamp to its holder", r);
            if (r.Success)
                yield return AutopilotActor.WaitFor(() => !(AutopilotGame.Held is InkStampPickup), 5f, r,
                    "Returning the stamp to its holder did not free the player's hands.");
            if (r.Success) yield break;

            StepFailed(r.Failure, "stamp return");
            if (AutopilotGame.Held is InkStampPickup)
            {
                Vector3 p = DeskPoint(AutopilotGame.Nearest<HandOffPoint>(station.transform.position), out Vector3 n);
                yield return _actor.PlaceHeld(p, n, null, "Put the stamp down on the desk", r);
            }
        }

        private IEnumerator EnsureInBooth()
        {
            if (PlayerSpawner.Instance == null || AutopilotGame.Player == null) yield break;
            Transform booth = PlayerSpawner.Instance.GetBoothSpawnPoint(AutopilotGame.Player.OwnerClientId);
            if (booth == null) yield break;
            if ((AutopilotGame.Player.transform.position - booth.position).magnitude < 3f) yield break;

            if (_config.mode == AutopilotMode.Input)
                _report.Add(FindingKind.Assist, "low", "Teleported the player into the booth (a suspect was waiting and the player was not inside).",
                    AutopilotGame.Snapshot(), screenshot: false);
            else
                _report.Timeline("Positioning player in the booth.");

            CharacterController cc = AutopilotGame.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            AutopilotGame.Player.SetPosition(booth);
            yield return null;
            if (cc != null) cc.enabled = true;
            yield return AutopilotActor.WaitSeconds(1f);
        }

        private void StepFailed(string failure, string context)
        {
            _report.Add(FindingKind.StepFailed, "high", failure, $"While handling {context}\n\n{AutopilotGame.Snapshot()}");
        }

        private StampContainer.StampType DecideVerdict(SuspectCharacter suspect)
        {
            switch (_config.verdictPolicy)
            {
                case VerdictPolicy.AlwaysPass: return StampContainer.StampType.Pass;
                case VerdictPolicy.AlwaysKill: return StampContainer.StampType.Kill;
                case VerdictPolicy.Random:
                    return (StampContainer.StampType)_rng.Next(0, 3);
                default:
                    return suspect != null && suspect.IsInfected ? StampContainer.StampType.Kill : StampContainer.StampType.Pass;
            }
        }

        private static InkStamp FindStamp(StampContainer.StampType wanted, out bool exact)
        {
            InkStamp[] all = FindObjectsByType<InkStamp>(FindObjectsSortMode.None);
            InkStamp match = all.FirstOrDefault(s => s.StampType == wanted && s.IsStampInSlot && s.IsInteractable);
            exact = match != null;
            if (match != null) return match;
            return all.FirstOrDefault(s => s.IsStampInSlot && s.IsInteractable && s.StampType != StampContainer.StampType.Quarantine)
                   ?? all.FirstOrDefault(s => s.IsStampInSlot && s.IsInteractable);
        }

        /// <summary>Finds a free, flat desk surface next to the stamp holders, facing the window.</summary>
        private static Vector3 DeskPoint(HandOffPoint handOff, out Vector3 normal)
        {
            normal = Vector3.up;
            InkStamp anchor = handOff != null ? AutopilotGame.Nearest<InkStamp>(handOff.transform.position) : FindFirstObjectByType<InkStamp>();
            if (anchor != null)
            {
                Vector3 origin = anchor.transform.position;
                Vector3 toward = handOff != null ? handOff.transform.position - origin : anchor.transform.forward;
                toward.y = 0f;
                toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, toward);

                float[] forwardOffsets = { 0.35f, 0.5f, 0.25f, 0.65f };
                float[] sideOffsets = { 0f, 0.25f, -0.25f };
                foreach (float f in forwardOffsets)
                foreach (float s in sideOffsets)
                {
                    Vector3 start = origin + toward * f + side * s + Vector3.up * 0.5f;
                    if (!Physics.Raycast(start, Vector3.down, out RaycastHit hit, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.8f || hit.collider.GetComponentInParent<Interactable>() != null) continue;
                    normal = hit.normal;
                    return hit.point;
                }
            }

            return handOff != null ? handOff.transform.position : (AutopilotGame.Player != null ? AutopilotGame.Player.transform.position : Vector3.zero);
        }

        // ── Smoke mode ──────────────────────────────────────────────────────────

        private IEnumerator SmokeBehaviour()
        {
            int day = AutopilotGame.CurrentDay;
            if (_smokeDay != day)
            {
                _smokeDay = day;
                _smokeShiftStartedAt = -1f;
                _smokeForcedEnd = false;
            }

            if (_smokeForcedEnd) return null;

            if (!AutopilotGame.ShiftStarted && _smokeShiftStartedAt < 0f)
                return SmokeStartShift();

            if (_smokeShiftStartedAt < 0f) _smokeShiftStartedAt = Time.realtimeSinceStartup;

            if (BellShouldBePressed() && Ready("bell", 4f)) return BellBehaviour();

            if (Time.realtimeSinceStartup - _smokeShiftStartedAt >= _config.smokeObserveSeconds)
                return SmokeForceEndDay();

            return null;
        }

        private IEnumerator SmokeStartShift()
        {
            yield return AutopilotActor.WaitSeconds(3f);
            if (AutopilotGame.ShiftStarted) yield break;

            TimecardMachine timecard = AutopilotGame.Timecard;
            var r = new ActionResult();
            if (timecard != null && timecard.IsClockInArmed)
            {
                _report.Timeline("Smoke: clocking in.");
                yield return _actor.Interact(timecard, "Clock in", r);
            }
            else
            {
                _report.Timeline("Smoke: starting shift via ShiftManager.TryStartShift().");
                ShiftManager.Instance?.TryStartShift();
            }

            yield return AutopilotActor.WaitFor(() => AutopilotGame.ShiftStarted, 10f, r, "Shift did not start in smoke mode.");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
            _smokeShiftStartedAt = Time.realtimeSinceStartup;
        }

        private IEnumerator SmokeForceEndDay()
        {
            _smokeForcedEnd = true;
            _report.Timeline($"Smoke: forcing end of Day {AutopilotGame.CurrentDay}.");
            ShiftManager sm = ShiftManager.Instance;
            sm?.DebugEnableClockOut();
            BetweenShiftTaskManager.Instance?.ForceCompleteAllTasks();
            sm?.EndShift();
            BunkBedInteractable.ForceAllowSleep = true;
            yield return AutopilotActor.WaitSeconds(1f);
            sm?.TriggerEndOfShiftReportServerRpc();

            var r = new ActionResult();
            yield return AutopilotActor.WaitFor(() => AutopilotGame.ReportVisible, 20f, r,
                "Smoke: end-of-shift report did not appear after TriggerEndOfShiftReport.");
            if (!r.Success) _report.Add(FindingKind.StepFailed, "high", r.Failure, AutopilotGame.Snapshot());
            BunkBedInteractable.ForceAllowSleep = false;
        }

        // ── Fallback ladder ─────────────────────────────────────────────────────

        private IEnumerator RunFallback(int lastDay)
        {
            int level = _stallLevel;
            ShiftManager sm = ShiftManager.Instance;

            switch (level)
            {
                case 0:
                    Assist("L0 unstick UI: close report/choices/popups, end intro cutscene if active");
                    if (AutopilotGame.ChoicePanelVisible) DialogueChoiceSystem.Instance.ChooseDialogueChoice(0);
                    if (AutopilotGame.ReportVisible)
                    {
                        EndOfShiftReportUI ui = FindFirstObjectByType<EndOfShiftReportUI>();
                        if (ui != null && !_reportContinuePressed) ui.OnContinueButtonPressed();
                        else UIController.Instance?.ForceDismissEndOfShiftReport();
                    }
                    EndDayPopupUI popup = FindFirstObjectByType<EndDayPopupUI>();
                    if (popup != null) popup.OnCancelClicked();
                    if (GameManager.Instance != null && (GameManager.Instance.IsIntroCutsceneEntering || AutopilotGame.InCutscene))
                        sm?.EndIntroCutscene();
                    break;

                case 1:
                    if (!AutopilotGame.ShiftStarted)
                    {
                        Assist("L1 advance shift: ShiftManager.TryStartShift()");
                        sm?.TryStartShift();
                    }
                    else if (ShiftManager.NextSuspectReadyForBell && SuspectController.Instance != null && !SuspectController.Instance.HasEntityAtWindow)
                    {
                        Assist("L1 advance shift: SuspectController.NextSuspect()");
                        SuspectController.Instance.NextSuspect();
                    }
                    else
                    {
                        Assist("L1 advance shift: nothing applicable, clearing per-day retry limits");
                        _suspectAttempts.Clear();
                        _tutorialAttempts.Clear();
                    }
                    break;

                case 2:
                    Assist("L2 complete tasks: ForceCompleteAllTasks + DebugEnableClockOut");
                    BetweenShiftTaskManager.Instance?.ForceCompleteAllTasks();
                    sm?.ForceCompleteAllTasksServerRpc();
                    sm?.DebugEnableClockOut();
                    break;

                case 3:
                    Assist("L3 end day: clock out, EndShift, TriggerEndOfShiftReport");
                    TimecardMachine timecard = AutopilotGame.Timecard;
                    if (timecard != null && timecard.IsClockOutArmed && AutopilotGame.Interaction != null)
                        timecard.Interact(AutopilotGame.Interaction);
                    BunkBedInteractable.ForceAllowSleep = true;
                    sm?.EndShift();
                    yield return AutopilotActor.WaitSeconds(1f);
                    sm?.TriggerEndOfShiftReportServerRpc();
                    yield return AutopilotActor.WaitSeconds(2f);
                    BunkBedInteractable.ForceAllowSleep = false;
                    break;

                case 4:
                    int next = AutopilotGame.CurrentDay + 1;
                    if (next > lastDay || DebugConsole.Instance == null)
                    {
                        Finish("Unrecoverable stall (fallback ladder exhausted).");
                        yield break;
                    }
                    Assist($"L4 skip day: DebugConsole.SkipToDay({next})");
                    _report.MarkCurrentDayOutcome("skipped (stalled)");
                    DebugConsole.Instance.SkipToDay(next);
                    break;

                default:
                    Finish("Unrecoverable stall (fallback ladder exhausted).");
                    yield break;
            }

            yield return AutopilotActor.WaitSeconds(2f);
        }

        private void Assist(string what)
        {
            _report.Add(FindingKind.Assist, "low", "Fallback " + what, AutopilotGame.Snapshot(), screenshot: false);
        }
        [Serializable]
        private class LiveStatus
        {
            public bool running;
            public string mode;
            public int day;
            public string phase;
            public float elapsedSeconds;
            public int stallLevel;
            public int exceptions, errors, stalls, stepFailures, invariants, assists;
            public string snapshot;
            public string reportDirectory;
            public List<string> recentTimeline;
        }

        /// <summary>Live progress as JSON (used by the GetAutopilotStatus Bezi action).</summary>
        public string StatusJson()
        {
            AutopilotReportData d = _report?.Data;
            var status = new LiveStatus
            {
                running = !_finished,
                mode = _config?.mode.ToString(),
                day = AutopilotGame.CurrentDay,
                phase = AutopilotGame.Phase,
                elapsedSeconds = _report != null ? _report.Elapsed : 0f,
                stallLevel = _stallLevel,
                exceptions = d?.exceptionCount ?? 0,
                errors = d?.errorCount ?? 0,
                stalls = d?.stallCount ?? 0,
                stepFailures = d?.stepFailedCount ?? 0,
                invariants = d?.invariantCount ?? 0,
                assists = d?.assistCount ?? 0,
                snapshot = AutopilotGame.Snapshot(),
                reportDirectory = _report?.Directory,
                recentTimeline = d != null ? d.timeline.Skip(Math.Max(0, d.timeline.Count - 12)).ToList() : new List<string>()
            };
            return JsonUtility.ToJson(status);
        }



        // ── Finish ──────────────────────────────────────────────────────────────

        public void Finish(string reason, bool stopPlayMode = true)
        {
            if (_finished) return;
            _finished = true;
            StopAllCoroutines();

            _input?.ReleaseAll();
            _input?.Tick();
            _input?.Dispose();
            if (!Mathf.Approximately(_config.timeScale, 1f)) Time.timeScale = 1f;

            string path = _report.Finish(reason);
            bool passed = _report.Passed;
            AutopilotReportData d = _report.Data;
            Debug.Log($"{AutopilotReport.LogPrefix} Finished: {(passed ? "PASS" : "FAIL")} — {reason}\n" +
                      $"exceptions={d.exceptionCount} errors={d.errorCount} stalls={d.stallCount} stepFailures={d.stepFailedCount} " +
                      $"invariants={d.invariantCount} assists={d.assistCount}\nReport: {path}");

            var result = new RunResult { reportPath = path, passed = passed, reason = reason, exitEditor = _config.exitEditorWhenDone };
            UnityEditor.SessionState.SetString(LastResultKey, JsonUtility.ToJson(result));

            if (stopPlayMode && UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
