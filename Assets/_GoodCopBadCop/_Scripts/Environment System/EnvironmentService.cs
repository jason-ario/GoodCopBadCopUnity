using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    public interface IEnvironmentService
    {
        void ApplyDay(int day);
        void ApplyPreset(EnvironmentPreset preset);
        void ApplyNightPreset(EnvironmentPreset preset);
        void ApplyNext();
        void ApplyPrevious();

        /// <summary>
        /// Reports how far the current shift's suspect lineup has been processed. At 0 suspects
        /// processed the environment stays on the day preset; the instant
        /// <paramref name="suspectsProcessed"/> reaches <paramref name="totalSuspects"/> (dusk),
        /// the environment is switched to the night preset for the current day immediately —
        /// there is no gradient/blend, it's a hard cut.
        /// </summary>
        void SetSuspectProgress(int suspectsProcessed, int totalSuspects);

        /// <summary>
        /// Same calculation as <see cref="SetSuspectProgress"/>. Intended for editor debug
        /// tooling previews.
        /// </summary>
        void ForceSuspectProgress(int suspectsProcessed, int totalSuspects);

        /// <summary>
        /// True while the Trailer Console has locked the look. Scheduled changes (day start via
        /// <see cref="ApplyDay"/>, dusk switch via <see cref="SetSuspectProgress"/>) still update
        /// day/progress/night-preset bookkeeping but no longer change the active preset or rain.
        /// </summary>
        bool IsPresetLocked { get; }

        /// <summary>
        /// Locks/unlocks scheduled preset changes. On unlock, the scheduled preset and rain for the
        /// current day (and the night preset if dusk was reached) are re-applied.
        /// </summary>
        void SetPresetLocked(bool locked);

        /// <summary>Sets rain directly. Ignores <see cref="IsPresetLocked"/>.</summary>
        void SetRainEnabled(bool enabled);
    }

    public sealed class EnvironmentService : IEnvironmentService
    {
        private readonly EnvironmentSchedule schedule;
        private readonly EnvironmentModel model;
        private bool presetLocked;

        public EnvironmentService(EnvironmentSchedule schedule, EnvironmentModel model)
        {
            this.schedule = schedule;
            this.model = model;
        }

        public bool IsPresetLocked => presetLocked;

        public void SetPresetLocked(bool locked)
        {
            if (presetLocked == locked) return;
            presetLocked = locked;
            if (locked) return;

            // Restore the scheduled look for the current day.
            int day = Mathf.Max(1, model.CurrentDayMutable.Value);
            EnvironmentPreset preset = schedule != null ? schedule.GetPresetForDay(day) : null;
            if (preset != null)
            {
                model.SelectPreset(preset);
                model.SelectRainEnabled(schedule.GetRainEnabledForDay(day));
            }

            if (model.DayNightProgressMutable.Value >= 1f && model.CurrentNightPresetMutable.Value != null)
                model.SelectPreset(model.CurrentNightPresetMutable.Value);
        }

        public void SetRainEnabled(bool enabled)
        {
            model.SelectRainEnabled(enabled);
        }

        public void ApplyDay(int day)
        {
            int safeDay = Mathf.Max(1, day);
            model.SelectDay(safeDay);

            // Reset the progress tracker to the start of the shift before the day/night presets
            // change, so a fresh SetSuspectProgress(0, ...) call doesn't immediately re-trigger
            // the dusk night-switch from a stale progress value left over from the prior shift.
            model.SelectDayNightProgress(0f);

            EnvironmentPreset preset = schedule != null
                ? schedule.GetPresetForDay(safeDay)
                : null;

            if (preset == null)
            {
                Debug.LogWarning($"[EnvironmentService] No environment preset configured for Day {safeDay}.");
                return;
            }

            if (!presetLocked)
            {
                model.SelectPreset(preset);
                model.SelectRainEnabled(schedule.GetRainEnabledForDay(safeDay));
            }
            model.SelectNightPreset(schedule.GetNightPresetForDay(safeDay));
        }

        public void ApplyPreset(EnvironmentPreset preset)
        {
            if (preset == null)
            {
                Debug.LogWarning("[EnvironmentService] Cannot apply a null environment preset.");
                return;
            }

            model.SelectPreset(preset);
        }

        public void ApplyNightPreset(EnvironmentPreset preset)
        {
            if (preset == null)
            {
                Debug.LogWarning("[EnvironmentService] Cannot apply a null night environment preset.");
                return;
            }

            model.SelectNightPreset(preset);
        }

        public void ApplyNext()
        {
            ApplyDay(model.CurrentDayMutable.Value + 1);
        }

        public void ApplyPrevious()
        {
            ApplyDay(Mathf.Max(1, model.CurrentDayMutable.Value - 1));
        }

        public void SetSuspectProgress(int suspectsProcessed, int totalSuspects)
        {
            float progress = CalculateProgress(suspectsProcessed, totalSuspects);
            model.SelectDayNightProgress(progress);
            ApplyNightIfDuskReached(progress);
        }

        public void ForceSuspectProgress(int suspectsProcessed, int totalSuspects)
        {
            float progress = CalculateProgress(suspectsProcessed, totalSuspects);
            model.ForceDayNightProgress(progress);
            ApplyNightIfDuskReached(progress);
        }

        /// <summary>
        /// The instant the shift's suspect lineup is fully processed (progress reaches 1 —
        /// dusk), switches <see cref="EnvironmentModel.CurrentPresetMutable"/> straight to the
        /// current day's night preset. This is a hard cut, not a blend: EnvironmentRenderAdapter
        /// applies whatever preset it's handed as-is.
        /// </summary>
        private void ApplyNightIfDuskReached(float progress)
        {
            if (progress < 1f || presetLocked)
            {
                return;
            }

            EnvironmentPreset nightPreset = model.CurrentNightPresetMutable.Value;
            if (nightPreset == null)
            {
                Debug.LogWarning("[EnvironmentService] Dusk reached but no night preset is configured for the current day.");
                return;
            }

            model.SelectPreset(nightPreset);
        }

        private static float CalculateProgress(int suspectsProcessed, int totalSuspects)
        {
            return totalSuspects > 0
                ? Mathf.Clamp01((float)suspectsProcessed / totalSuspects)
                : 0f;
        }
    }

}
