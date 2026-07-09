using System;
using R3;

namespace GoodCopBadCop.SuspectPaperwork
{
    public interface ISuspectPaperworkModel
    {
        ReadOnlyReactiveProperty<SuspectPaperworkState> Current { get; }
    }

    public sealed class SuspectPaperworkModel : ISuspectPaperworkModel, IDisposable
    {
        public readonly ReactiveProperty<SuspectPaperworkState> CurrentMutable = new(SuspectPaperworkState.Empty);

        public ReadOnlyReactiveProperty<SuspectPaperworkState> Current => CurrentMutable;

        public void SetCurrent(SuspectPaperworkState state)
        {
            CurrentMutable.Value = state;
        }

        public void Dispose()
        {
            CurrentMutable.Dispose();
        }
    }
}
