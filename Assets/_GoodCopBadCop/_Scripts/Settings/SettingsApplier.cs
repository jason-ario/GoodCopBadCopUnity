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
                .Subscribe(_ => ApplyDisplaySettings())
                .AddTo(ref disposables);

            model.ScreenResolution
                .Subscribe(_ => ApplyDisplaySettings())
                .AddTo(ref disposables);

            model.VSyncEnabled
                .Subscribe(_ => ApplyDisplaySettings())
                .AddTo(ref disposables);

            model.FpsLimit
                .Subscribe(_ => ApplyDisplaySettings())
                .AddTo(ref disposables);

            model.MasterVolume
                .Subscribe(value => UnityEngine.AudioListener.volume = value / 100f)
                .AddTo(ref disposables);

            model.MusicVolume
                .Subscribe(value => global::MusicManager.Instance?.SetVolumeScale(value / 100f))
                .AddTo(ref disposables);

            model.SfxVolume
                .Subscribe(value =>
                {
                    if (global::SFXController.Instance != null)
                    {
                        global::SFXController.Instance.VolumeScale = value / 100f;
                    }
                })
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private void ApplyDisplaySettings()
        {
            screenAdapter.Apply(
                model.DisplayMode.CurrentValue,
                model.ScreenResolution.CurrentValue,
                model.VSyncEnabled.CurrentValue,
                model.FpsLimit.CurrentValue);
        }
    }
}
