#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>
    /// Read-only probes over the live game state. Every accessor is null-safe so it can be used
    /// during bootstrap, transitions, and teardown.
    /// </summary>
    public static class AutopilotGame
    {
        public static PlayerInstance Player => PlayerInstance.Instance;
        public static PlayerInteractionController Interaction => Player != null ? Player.PlayerInteractionController : null;
        public static PlayerPickupController Pickup => Player != null ? Player.PlayerPickupController : null;
        public static PickableObject Held => Pickup != null ? Pickup.HeldObject : null;

        public static bool GameStarted => GameManager.Instance != null && GameManager.Instance.HasGameStarted;
        public static int CurrentDay => CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
        public static bool CampaignComplete => CampaignManager.Instance != null && CampaignManager.Instance.IsCampaignComplete;

        public static bool ShiftStarted => ShiftManager.Instance != null && ShiftManager.Instance.shiftStarted.Value;
        public static int SuspectsProcessed => ShiftManager.Instance != null ? ShiftManager.Instance.suspectsProcessed : 0;
        public static int SuspectIndex => SuspectController.Instance != null ? SuspectController.Instance.SuspectIndex : -1;

        public static bool ReportVisible => UIController.Instance != null && UIController.Instance.IsEndOfShiftReportVisible;
        public static bool Paused => UIController.Instance != null && UIController.Instance.IsPaused;
        public static bool ChoicePanelVisible => DialogueChoiceSystem.Instance != null && DialogueChoiceSystem.Instance.IsChoicePanelVisible;
        public static bool InDialogue => DialogueChoiceSystem.IsInDialogueMode || ScriptedDialogueRunner.IsScriptedModeActive;
        public static bool InCutscene => Player != null && Player.IsInCutscene;
        public static bool CanControl => Player != null && Player.CanControl;
        public static bool CanInteract => Interaction != null && Interaction.CanInteract;

        public static TimecardMachine Timecard => Object.FindFirstObjectByType<TimecardMachine>();
        public static bool ClockInArmed { get { var t = Timecard; return t != null && t.IsClockInArmed; } }
        public static bool ClockOutArmed { get { var t = Timecard; return t != null && t.IsClockOutArmed; } }

        public static string Phase
        {
            get
            {
                if (!GameStarted) return "Boot";
                if (ReportVisible) return "Report";
                string phase = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentPhase.ToString() : "?";
                if (InCutscene) phase += "+Cutscene";
                else if (InDialogue) phase += "+Dialogue";
                return phase;
            }
        }

        /// <summary>True while a legitimately modal state owns the player (no control expected).</summary>
        public static bool AnyModalActive =>
            ReportVisible || Paused || InDialogue || InCutscene || ChoicePanelVisible ||
            (Interaction != null && !Interaction.CanInteract) ||
            (GameManager.Instance != null && (GameManager.Instance.IsIntroCutsceneEntering || GameManager.Instance.IsTransitioningToLobby));

        /// <summary>
        /// Compact fingerprint of game progress. The watchdog reports a stall when it stays
        /// unchanged for too long. Deliberately excludes player position and the shift clock.
        /// </summary>
        public static string ProgressSignature()
        {
            var sb = new StringBuilder(128);
            sb.Append(CurrentDay).Append('|')
              .Append(Phase).Append('|')
              .Append(ShiftStarted).Append('|')
              .Append(SuspectIndex).Append('|')
              .Append(SuspectsProcessed).Append('|')
              .Append(SuspectController.Instance != null && SuspectController.Instance.HasEntityAtWindow).Append('|')
              .Append(ShiftManager.NextSuspectReadyForBell).Append('|')
              .Append(ClockInArmed).Append(ClockOutArmed).Append(TimecardMachine.HasClockedOutThisCycle).Append('|')
              .Append(ChoicePanelVisible).Append('|')
              .Append(Held != null ? Held.name : "-").Append('|')
              .Append(CampaignComplete).Append('|');

            foreach (Transform t in MarkedTutorialTargets())
                sb.Append(t.name).Append('#').Append(t.GetHashCode()).Append(',');

            return sb.ToString();
        }

        /// <summary>Human-readable multi-line state dump attached to stalls and failures.</summary>
        public static string Snapshot()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine($"day={CurrentDay} phase={Phase} campaignComplete={CampaignComplete}");
            sb.AppendLine($"shiftStarted={ShiftStarted} suspectIndex={SuspectIndex} processed={SuspectsProcessed} " +
                          $"entityAtWindow={(SuspectController.Instance != null && SuspectController.Instance.HasEntityAtWindow)} " +
                          $"currentSuspect={(SuspectController.Instance != null && SuspectController.Instance.CurrentSuspect != null ? SuspectController.Instance.CurrentSuspect.name : "-")} " +
                          $"bellReady={ShiftManager.NextSuspectReadyForBell}");
            sb.AppendLine($"clockInArmed={ClockInArmed} clockOutArmed={ClockOutArmed} clockedOut={TimecardMachine.HasClockedOutThisCycle} " +
                          $"blockVerdict={HandOffPoint.BlockVerdict}");
            sb.AppendLine($"report={ReportVisible} paused={Paused} dialogueMode={DialogueChoiceSystem.IsInDialogueMode} " +
                          $"scripted={ScriptedDialogueRunner.IsScriptedModeActive} choices={ChoicePanelVisible} cutscene={InCutscene}");
            sb.AppendLine($"canControl={CanControl} canInteract={CanInteract} held={(Held != null ? Held.name : "-")} " +
                          $"pos={(Player != null ? Player.transform.position.ToString("F1") : "-")} timeScale={Time.timeScale:0.##} " +
                          $"cursorVisible={Cursor.visible}");

            List<Transform> marked = MarkedTutorialTargets().ToList();
            if (marked.Count > 0)
                sb.AppendLine("tutorialTargets=" + string.Join(", ", marked.Select(t => t.name)));
            if (Interaction != null && Interaction.onlyAllowedInteractable != null)
                sb.AppendLine("onlyAllowedInteractable=" + Interaction.onlyAllowedInteractable.name);
            return sb.ToString();
        }

        /// <summary>Tutorial arrow targets from pooled (manager) and pre-placed markers.</summary>
        public static IEnumerable<Transform> MarkedTutorialTargets()
        {
            var seen = new HashSet<Transform>();
            if (TutorialMarkerManager.Instance != null)
            {
                foreach (Transform t in TutorialMarkerManager.Instance.GetMarkedTargets())
                    if (t != null && seen.Add(t)) yield return t;
            }

            foreach (TutorialMarker marker in TutorialMarker.ActiveInstances)
            {
                if (marker == null || !marker.isActiveAndEnabled) continue;
                if (seen.Add(marker.transform)) yield return marker.transform;
            }
        }

        /// <summary>Resolves the interactable a tutorial target refers to (on, above, below, or near it).</summary>
        public static Interactable ResolveInteractable(Transform target)
        {
            if (target == null) return null;
            Interactable i = target.GetComponentInParent<Interactable>();
            if (i == null) i = target.GetComponentInChildren<Interactable>();
            if (i != null) return i;

            TutorialMarker marker = target.GetComponent<TutorialMarker>();
            Vector3 pos = marker != null ? marker.StableAnchorPosition : target.position;
            Interactable best = null;
            float bestDist = 1.5f;
            foreach (Collider c in Physics.OverlapSphere(pos, 1.5f, ~0, QueryTriggerInteraction.Collide))
            {
                Interactable candidate = c.GetComponentInParent<Interactable>();
                if (candidate == null || !candidate.IsInteractable) continue;
                float d = Vector3.Distance(pos, c.ClosestPoint(pos));
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best;
        }

        /// <summary>Campaign day numbers authored in the scene (excluding the debug test day).</summary>
        public static List<int> AuthoredDays()
        {
            return Object.FindObjectsByType<DayBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(d => d.DayNumber)
                .Where(n => n > 0 && n != Day_Test.TestDayNumber)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        public static T Nearest<T>(Vector3 to, System.Func<T, bool> filter = null) where T : Component
        {
            T best = null;
            float bestDist = float.MaxValue;
            foreach (T c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c == null || (filter != null && !filter(c))) continue;
                float d = (c.transform.position - to).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }
    }
}
#endif
