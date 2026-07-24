using System;
using VContainer.Unity;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Bridges <see cref="SuspectController.OnSuspectProgressChanged"/> into
    /// <see cref="IEnvironmentService.SetSuspectProgress"/>, so the day/night environment blend
    /// advances progressively as each suspect in the current shift's lineup is processed.
    /// By the time the last suspect is processed, the blend target reaches 1 (fully night).
    /// </summary>
    public sealed class EnvironmentSuspectProgressAdapter : IInitializable, IDisposable
    {
        private readonly IEnvironmentService service;

        public EnvironmentSuspectProgressAdapter(IEnvironmentService service)
        {
            this.service = service;
        }

        public void Initialize()
        {
            SuspectController.OnSuspectProgressChanged += HandleSuspectProgressChanged;
        }

        public void Dispose()
        {
            SuspectController.OnSuspectProgressChanged -= HandleSuspectProgressChanged;
        }

        private void HandleSuspectProgressChanged(int suspectsProcessed, int totalSuspects)
        {
            service.SetSuspectProgress(suspectsProcessed, totalSuspects);
        }
    }

}
