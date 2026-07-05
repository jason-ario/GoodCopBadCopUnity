using System;
using R3;
using VContainer.Unity;
namespace GoodCopBadCop.Settings
{
    public sealed class SettingsApplier : IInitializable, IDisposable
    {
        private readonly ISettingsModel model;
        private readonly ISettingsScreenAdapter screenAdapter;
        private DisposableBag disposables;
        public SettingsApplier(ISettingsModel model, ISettingsScreenAdapter screenAdapter)
        {
            this.model = model;
            this.screenAdapter = screenAdapter;
        }
        public void Initialize()
        {
            model.DisplayMode
                .Subscribe(displayMode => ApplyDisplaySettings(displayMode, model.ScreenResolution.CurrentValue))
                .AddTo(ref disposables);
            model.ScreenResolution
                .Subscribe(screenResolution => ApplyDisplaySettings(model.DisplayMode.CurrentValue, screenResolution))
                .AddTo(ref disposables);
        }
        public void Dispose()
        {
            disposables.Dispose();
        }
        private void ApplyDisplaySettings(EDisplayMode displayMode, EScreenResolution screenResolution)
        {
            screenAdapter.Apply(displayMode, screenResolution);
        }
    }
}