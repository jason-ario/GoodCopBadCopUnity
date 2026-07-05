using System;
using GoodCopBadCop.Settings;
using R3;
using VContainer.Unity;
namespace GoodCopBadCop.UI.SettingsMenu
{
    public sealed class SettingsMenuPresenter : IInitializable, IDisposable
    {
        private readonly ISettingsMenuModel model;
        private readonly ISettingsModel settingsModel;
        private readonly ISettingsService settingsService;
        private readonly ISettingsMenuView view;
        private DisposableBag disposables;
        public SettingsMenuPresenter(
            ISettingsMenuModel model,
            ISettingsModel settingsModel,
            ISettingsService settingsService,
            ISettingsMenuView view)
        {
            this.model = model;
            this.settingsModel = settingsModel;
            this.settingsService = settingsService;
            this.view = view;
        }
        public void Initialize()
        {
            view.DisplayModeChanged += OnDisplayModeChanged;
            view.ScreenResolutionChanged += OnScreenResolutionChanged;
            view.Closed += OnClosed;
            model.SelectedTab
                .Subscribe(view.ShowTab)
                .AddTo(ref disposables);
            settingsModel.DisplayMode
                .Subscribe(displayMode => view.SetDisplayModeValue((int)displayMode))
                .AddTo(ref disposables);
            settingsModel.ScreenResolution
                .Subscribe(screenResolution => view.SetScreenResolutionValue((int)screenResolution))
                .AddTo(ref disposables);
        }
        public void Dispose()
        {
            view.DisplayModeChanged -= OnDisplayModeChanged;
            view.ScreenResolutionChanged -= OnScreenResolutionChanged;
            view.Closed -= OnClosed;
            disposables.Dispose();
        }
        private void OnDisplayModeChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EDisplayMode), value))
            {
                return;
            }
            settingsService.SetDisplayMode((EDisplayMode)value);
        }
        private void OnScreenResolutionChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EScreenResolution), value))
            {
                return;
            }
            settingsService.SetScreenResolution((EScreenResolution)value);
        }
        private void OnClosed()
        {
            settingsService.Flush();
        }
    }
}