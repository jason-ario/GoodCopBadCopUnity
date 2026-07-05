using System;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace GoodCopBadCop.Settings
{
    public sealed class SettingsApplier : IInitializable, IDisposable
    {
        private readonly ISettingsModel model;
        private DisposableBag disposables;

        public SettingsApplier(ISettingsModel model)
        {
            this.model = model;
        }

        public void Initialize()
        {
            model.DisplayMode
                .Subscribe(ApplyDisplayMode)
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private static void ApplyDisplayMode(EDisplayMode displayMode)
        {
            switch (displayMode)
            {
                case EDisplayMode.Fullscreen:
                    Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                    break;
                case EDisplayMode.Windowed:
                    Screen.fullScreenMode = FullScreenMode.Windowed;
                    break;
                default:
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                    break;
            }
        }
    }
}
