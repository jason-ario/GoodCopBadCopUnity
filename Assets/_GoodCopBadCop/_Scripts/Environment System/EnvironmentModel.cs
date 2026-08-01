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

        /// <summary>
        /// Fires whenever the day/night blend should snap immediately to
        /// <see cref="DayNightProgress"/>'s current value instead of smoothing toward it over
        /// time. Used by editor debug tooling so previewing a step is instant.
        /// </summary>
        Observable<Unit> ForceDayNightProgressRequested { get; }
    }

    public sealed class EnvironmentModel : IEnvironmentModel, IDisposable
    {
        public readonly ReactiveProperty<int> CurrentDayMutable = new(1);
        public readonly ReactiveProperty<EnvironmentPreset> CurrentPresetMutable = new(null);
        public readonly ReactiveProperty<EnvironmentPreset> CurrentNightPresetMutable = new(null);

        /// <summary>
        /// 0 at the start of a shift, 1 once the last suspect in the lineup has been processed
        /// (dusk). EnvironmentService watches this via SetSuspectProgress/ForceSuspectProgress
        /// and, the instant it reaches 1, hard-switches CurrentPreset to the night preset — no
        /// blending happens against this value, it's purely a progress readout.
        /// </summary>
        public readonly ReactiveProperty<float> DayNightProgressMutable = new(0f);
        public readonly ReactiveProperty<bool> CurrentRainEnabledMutable = new(false);
        private readonly Subject<Unit> forceDayNightProgressRequested = new();

        public ReadOnlyReactiveProperty<int> CurrentDay => CurrentDayMutable;
        public ReadOnlyReactiveProperty<EnvironmentPreset> CurrentPreset => CurrentPresetMutable;
        public ReadOnlyReactiveProperty<EnvironmentPreset> CurrentNightPreset => CurrentNightPresetMutable;
        public ReadOnlyReactiveProperty<float> DayNightProgress => DayNightProgressMutable;
        public ReadOnlyReactiveProperty<bool> CurrentRainEnabled => CurrentRainEnabledMutable;
        public Observable<Unit> ForceDayNightProgressRequested => forceDayNightProgressRequested;

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
        /// Sets the day/night progress readout (0-1). Purely informational for
        /// EnvironmentRenderAdapter/UI — EnvironmentService is what actually reacts to it by
        /// hard-switching the preset once it reaches 1.
        /// </summary>
        public void SelectDayNightProgress(float progress)
        {
            DayNightProgressMutable.Value = progress;
        }

        /// <summary>
        /// Sets the day/night progress readout and notifies listeners that this was a forced
        /// (editor-debug-driven) update rather than one arising from normal gameplay.
        /// </summary>
        public void ForceDayNightProgress(float progress)
        {
            DayNightProgressMutable.Value = progress;
            forceDayNightProgressRequested.OnNext(Unit.Default);
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
            forceDayNightProgressRequested.Dispose();
        }
    }

}
