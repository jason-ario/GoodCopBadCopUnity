using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    public interface IEnvironmentService
    {
        void ApplyDay(int day);
        void ApplyPreset(EnvironmentPreset preset);
        void ApplyNext();
        void ApplyPrevious();
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

            EnvironmentPreset preset = schedule != null
                ? schedule.GetPresetForDay(safeDay)
                : null;

            if (preset == null)
            {
                Debug.LogWarning($"[EnvironmentService] No environment preset configured for Day {safeDay}.");
                return;
            }

            model.SelectPreset(preset);
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
    }

}
