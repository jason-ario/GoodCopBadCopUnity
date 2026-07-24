using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    public interface IEnvironmentService
    {
        void ApplyDay(int day);
        void ApplyPreset(EnvironmentPreset preset);
        void ApplyNext();
        void ApplyPrevious();

        /// <summary>
        /// Reports how far the current shift's suspect lineup has been processed and updates
        /// the day/night blend target accordingly. At 0 suspects processed the environment
        /// targets the day preset; once <paramref name="suspectsProcessed"/> reaches
        /// <paramref name="totalSuspects"/> it targets the night preset for the current day.
        /// </summary>
        void SetSuspectProgress(int suspectsProcessed, int totalSuspects);
    }

    public sealed class EnvironmentService : IEnvironmentService
    {
        private readonly EnvironmentSchedule schedule;
        private readonly EnvironmentModel model;

        public EnvironmentService(EnvironmentSchedule schedule, EnvironmentModel model)
        {
            this.schedule = schedule;
            this.model = model;
        }

        public void ApplyDay(int day)
        {
            int safeDay = Mathf.Max(1, day);
            model.SelectDay(safeDay);

            // Reset the blend target to the start of the shift before the day/night presets
            // change, so EnvironmentRenderAdapter snaps to the new day's morning look instead
            // of animating backward from wherever the previous shift ended.
            model.SelectDayNightProgress(0f);

            EnvironmentPreset preset = schedule != null
                ? schedule.GetPresetForDay(safeDay)
                : null;

            if (preset == null)
            {
                Debug.LogWarning($"[EnvironmentService] No environment preset configured for Day {safeDay}.");
                return;
            }

            model.SelectPreset(preset);
            model.SelectRainEnabled(schedule.GetRainEnabledForDay(safeDay));
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
            float progress = totalSuspects > 0
                ? Mathf.Clamp01((float)suspectsProcessed / totalSuspects)
                : 0f;

            model.SelectDayNightProgress(progress);
        }
    }

}
