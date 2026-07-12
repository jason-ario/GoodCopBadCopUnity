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
        private readonly ISettingsMenuService menuService;
        private readonly ISettingsMenuView view;
        private DisposableBag disposables;

        public SettingsMenuPresenter(
            ISettingsMenuModel model,
            ISettingsModel settingsModel,
            ISettingsService settingsService,
            ISettingsMenuService menuService,
            ISettingsMenuView view)
        {
            this.model = model;
            this.settingsModel = settingsModel;
            this.settingsService = settingsService;
            this.menuService = menuService;
            this.view = view;
        }

        public void Initialize()
        {
            view.Initialize();

            view.TabSelected.Subscribe(OnTabSelected).AddTo(ref disposables);
            view.DisplayModeChanged.Subscribe(OnDisplayModeChanged).AddTo(ref disposables);
            view.ScreenResolutionChanged.Subscribe(OnScreenResolutionChanged).AddTo(ref disposables);
            view.VSyncChanged.Subscribe(OnVSyncChanged).AddTo(ref disposables);
            view.FpsLimitChanged.Subscribe(OnFpsLimitChanged).AddTo(ref disposables);
            view.MouseSensitivityChanged.Subscribe(OnMouseSensitivityChanged).AddTo(ref disposables);
            view.InvertYAxisChanged.Subscribe(OnInvertYAxisChanged).AddTo(ref disposables);
            view.CrouchModeChanged.Subscribe(OnCrouchModeChanged).AddTo(ref disposables);
            view.SprintModeChanged.Subscribe(OnSprintModeChanged).AddTo(ref disposables);
            view.VoiceChatEnabledChanged.Subscribe(OnVoiceChatEnabledChanged).AddTo(ref disposables);
            view.VoiceChatMutedChanged.Subscribe(OnVoiceChatMutedChanged).AddTo(ref disposables);
            view.VoiceChatDeafenedChanged.Subscribe(OnVoiceChatDeafenedChanged).AddTo(ref disposables);
            view.VoiceChatInputModeChanged.Subscribe(OnVoiceChatInputModeChanged).AddTo(ref disposables);
            view.Closed.Subscribe(_ => OnClosed()).AddTo(ref disposables);

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

            settingsModel.MouseSensitivity
                .Subscribe(view.SetMouseSensitivityValue)
                .AddTo(ref disposables);

            settingsModel.InvertYAxis
                .Subscribe(view.SetInvertYAxisValue)
                .AddTo(ref disposables);

            settingsModel.CrouchMode
                .Subscribe(crouchMode => view.SetCrouchModeValue((int)crouchMode))
                .AddTo(ref disposables);

            settingsModel.SprintMode
                .Subscribe(sprintMode => view.SetSprintModeValue((int)sprintMode))
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
            disposables.Dispose();
        }

        private void OnTabSelected(ESettingsMenuTab tab)
        {
            menuService.SelectTab(tab);
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

        private void OnMouseSensitivityChanged(float value)
        {
            settingsService.SetMouseSensitivity(value);
        }

        private void OnInvertYAxisChanged(bool isInverted)
        {
            settingsService.SetInvertYAxis(isInverted);
        }

        private void OnCrouchModeChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EInputActivationMode), value))
            {
                return;
            }

            settingsService.SetCrouchMode((EInputActivationMode)value);
        }

        private void OnSprintModeChanged(int value)
        {
            if (!Enum.IsDefined(typeof(EInputActivationMode), value))
            {
                return;
            }

            settingsService.SetSprintMode((EInputActivationMode)value);
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