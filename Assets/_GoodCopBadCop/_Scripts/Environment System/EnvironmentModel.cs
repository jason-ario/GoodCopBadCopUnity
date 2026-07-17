using System;
using R3;

namespace GoodCopBadCop.EnvironmentSystem
{
    public interface IEnvironmentModel
    {
        ReadOnlyReactiveProperty<int> CurrentDay { get; }
        ReadOnlyReactiveProperty<EnvironmentPreset> CurrentPreset { get; }
        ReadOnlyReactiveProperty<bool> CurrentRainEnabled { get; }
    }

    public sealed class EnvironmentModel : IEnvironmentModel, IDisposable
    {
        public readonly ReactiveProperty<int> CurrentDayMutable = new(1);
        public readonly ReactiveProperty<EnvironmentPreset> CurrentPresetMutable = new(null);
        public readonly ReactiveProperty<bool> CurrentRainEnabledMutable = new(false);

        public ReadOnlyReactiveProperty<int> CurrentDay => CurrentDayMutable;
        public ReadOnlyReactiveProperty<EnvironmentPreset> CurrentPreset => CurrentPresetMutable;
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

        public void SelectRainEnabled(bool enabled)
        {
            CurrentRainEnabledMutable.Value = enabled;
        }

        public void Dispose()
        {
            CurrentDayMutable.Dispose();
            CurrentPresetMutable.Dispose();
            CurrentRainEnabledMutable.Dispose();
        }
    }

}
