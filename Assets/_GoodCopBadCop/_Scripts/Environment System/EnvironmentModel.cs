using System;
using R3;

namespace GoodCopBadCop.EnvironmentSystem
{
    public interface IEnvironmentModel
    {
        ReadOnlyReactiveProperty<int> CurrentDay { get; }
        ReadOnlyReactiveProperty<EnvironmentPreset> CurrentPreset { get; }
        ReadOnlyReactiveProperty<EnvironmentPreset> CurrentNightPreset { get; }
        ReadOnlyReactiveProperty<float> DayNightProgress { get; }
        ReadOnlyReactiveProperty<bool> CurrentRainEnabled { get; }
    }

    public sealed class EnvironmentModel : IEnvironmentModel, IDisposable
    {
        public readonly ReactiveProperty<int> CurrentDayMutable = new(1);
        public readonly ReactiveProperty<EnvironmentPreset> CurrentPresetMutable = new(null);
        public readonly ReactiveProperty<EnvironmentPreset> CurrentNightPresetMutable = new(null);

        /// <summary>
        /// 0 at the start of a shift (fully on the day preset), 1 once the last suspect in the
        /// lineup has been processed (fully lerped to the night preset).
        /// </summary>
        public readonly ReactiveProperty<float> DayNightProgressMutable = new(0f);
        public readonly ReactiveProperty<bool> CurrentRainEnabledMutable = new(false);

        public ReadOnlyReactiveProperty<int> CurrentDay => CurrentDayMutable;
        public ReadOnlyReactiveProperty<EnvironmentPreset> CurrentPreset => CurrentPresetMutable;
        public ReadOnlyReactiveProperty<EnvironmentPreset> CurrentNightPreset => CurrentNightPresetMutable;
        public ReadOnlyReactiveProperty<float> DayNightProgress => DayNightProgressMutable;
        public ReadOnlyReactiveProperty<bool> CurrentRainEnabled => CurrentRainEnabledMutable;

        public void SelectDay(int day)
        {
            if (CurrentDayMutable.Value == day)
            {
                CurrentDayMutable.OnNext(day);
                return;
            }

            CurrentDayMutable.Value = day;
        }

        public void SelectPreset(EnvironmentPreset preset)
        {
            if (CurrentPresetMutable.Value == preset)
            {
                CurrentPresetMutable.OnNext(preset);
                return;
            }

            CurrentPresetMutable.Value = preset;
        }

        public void SelectNightPreset(EnvironmentPreset preset)
        {
            if (CurrentNightPresetMutable.Value == preset)
            {
                CurrentNightPresetMutable.OnNext(preset);
                return;
            }

            CurrentNightPresetMutable.Value = preset;
        }

        /// <summary>
        /// Sets the target day/night blend amount (0-1). EnvironmentRenderAdapter smoothly
        /// catches up to this target rather than snapping, so repeated calls as suspects are
        /// processed read as a progressive lerp rather than a hard cut.
        /// </summary>
        public void SelectDayNightProgress(float progress)
        {
            DayNightProgressMutable.Value = progress;
        }

        public void SelectRainEnabled(bool enabled)
        {
            CurrentRainEnabledMutable.Value = enabled;
        }

        public void Dispose()
        {
            CurrentDayMutable.Dispose();
            CurrentPresetMutable.Dispose();
            CurrentNightPresetMutable.Dispose();
            DayNightProgressMutable.Dispose();
            CurrentRainEnabledMutable.Dispose();
        }
    }

}
