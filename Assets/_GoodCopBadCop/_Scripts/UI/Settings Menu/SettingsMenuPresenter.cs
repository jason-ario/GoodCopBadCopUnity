using System;
using GoodCopBadCop.Settings;
using GoodCopBadCop.VoiceChat;
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
            view.VSyncChanged += OnVSyncChanged;
            view.FpsLimitChanged += OnFpsLimitChanged;
            view.VoiceChatEnabledChanged += OnVoiceChatEnabledChanged;
            view.VoiceChatMutedChanged += OnVoiceChatMutedChanged;
            view.VoiceChatDeafenedChanged += OnVoiceChatDeafenedChanged;
            view.VoiceChatInputModeChanged += OnVoiceChatInputModeChanged;
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

            settingsModel.VSyncEnabled
                .Subscribe(view.SetVSyncValue)
                .AddTo(ref disposables);

            settingsModel.FpsLimit
                .Subscribe(fpsLimit => view.SetFpsLimitValue((int)fpsLimit))
                .AddTo(ref disposables);

            settingsModel.VoiceChatEnabled
                .Subscribe(view.SetVoiceChatEnabledValue)
                .AddTo(ref disposables);

            settingsModel.VoiceChatMuted
                .Subscribe(view.SetVoiceChatMutedValue)
                .AddTo(ref disposables);

            settingsModel.VoiceChatDeafened
                .Subscribe(view.SetVoiceChatDeafenedValue)
                .AddTo(ref disposables);

            settingsModel.VoiceChatInputMode
                .Subscribe(inputMode => view.SetVoiceChatInputModeValue((int)inputMode))
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            view.DisplayModeChanged -= OnDisplayModeChanged;
            view.ScreenResolutionChanged -= OnScreenResolutionChanged;
            view.VSyncChanged -= OnVSyncChanged;
            view.FpsLimitChanged -= OnFpsLimitChanged;
            view.VoiceChatEnabledChanged -= OnVoiceChatEnabledChanged;
            view.VoiceChatMutedChanged -= OnVoiceChatMutedChanged;
            view.VoiceChatDeafenedChanged -= OnVoiceChatDeafenedChanged;
            view.VoiceChatInputModeChanged -= OnVoiceChatInputModeChanged;
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

        private void OnVSyncChanged(bool isEnabled)
        {
            settingsService.SetVSyncEnabled(isEnabled);
        }

        private void OnFpsLimitChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EFpsLimit), value))
            {
                return;
            }

            settingsService.SetFpsLimit((EFpsLimit)value);
        }

        private void OnVoiceChatEnabledChanged(bool isEnabled)
        {
            settingsService.SetVoiceChatEnabled(isEnabled);
        }

        private void OnVoiceChatMutedChanged(bool isMuted)
        {
            settingsService.SetVoiceChatMuted(isMuted);
        }

        private void OnVoiceChatDeafenedChanged(bool isDeafened)
        {
            settingsService.SetVoiceChatDeafened(isDeafened);
        }

        private void OnVoiceChatInputModeChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EVoiceChatInputMode), value))
            {
                return;
            }

            settingsService.SetVoiceChatInputMode((EVoiceChatInputMode)value);
        }

        private void OnClosed()
        {
            settingsService.Flush();
        }
    }
}
