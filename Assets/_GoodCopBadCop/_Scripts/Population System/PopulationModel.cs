using System;
using R3;

namespace GoodCopBadCop.Population
{
    public interface IPopulationModel
    {
        ReadOnlyReactiveProperty<int> TotalPopulation { get; }
        ReadOnlyReactiveProperty<int> PopulationAlive { get; }
        ReadOnlyReactiveProperty<int> ContactableAlive { get; }
        ReadOnlyReactiveProperty<int> ContactableDead { get; }
        ReadOnlyReactiveProperty<int> BackgroundAlive { get; }
        ReadOnlyReactiveProperty<int> BackgroundDead { get; }
        ReadOnlyReactiveProperty<int> DeadOvernight { get; }
        ReadOnlyReactiveProperty<int> LastSimulatedDay { get; }
    }

    public sealed class PopulationModel : IPopulationModel, IDisposable
    {
        public readonly ReactiveProperty<int> TotalPopulationMutable = new(100);
        public readonly ReactiveProperty<int> PopulationAliveMutable = new(100);
        public readonly ReactiveProperty<int> ContactableAliveMutable = new(0);
        public readonly ReactiveProperty<int> ContactableDeadMutable = new(0);
        public readonly ReactiveProperty<int> BackgroundAliveMutable = new(100);
        public readonly ReactiveProperty<int> BackgroundDeadMutable = new(0);
        public readonly ReactiveProperty<int> DeadOvernightMutable = new(0);
        public readonly ReactiveProperty<int> LastSimulatedDayMutable = new(0);

        public ReadOnlyReactiveProperty<int> TotalPopulation => TotalPopulationMutable;
        public ReadOnlyReactiveProperty<int> PopulationAlive => PopulationAliveMutable;
        public ReadOnlyReactiveProperty<int> ContactableAlive => ContactableAliveMutable;
        public ReadOnlyReactiveProperty<int> ContactableDead => ContactableDeadMutable;
        public ReadOnlyReactiveProperty<int> BackgroundAlive => BackgroundAliveMutable;
        public ReadOnlyReactiveProperty<int> BackgroundDead => BackgroundDeadMutable;
        public ReadOnlyReactiveProperty<int> DeadOvernight => DeadOvernightMutable;
        public ReadOnlyReactiveProperty<int> LastSimulatedDay => LastSimulatedDayMutable;

        public void Dispose()
        {
            TotalPopulationMutable.Dispose();
            PopulationAliveMutable.Dispose();
            ContactableAliveMutable.Dispose();
            ContactableDeadMutable.Dispose();
            BackgroundAliveMutable.Dispose();
            BackgroundDeadMutable.Dispose();
            DeadOvernightMutable.Dispose();
            LastSimulatedDayMutable.Dispose();
        }
    }
}
