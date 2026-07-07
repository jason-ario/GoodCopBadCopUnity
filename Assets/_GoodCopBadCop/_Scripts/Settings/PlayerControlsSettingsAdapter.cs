using System;
using GoodCopBadCop.Player;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.Settings
{
    public sealed class PlayerControlsSettingsAdapter : IInitializable, IDisposable
    {
        private readonly ISettingsModel settingsModel;
        private readonly IPlayerRuntimeModel playerRuntimeModel;
        private DisposableBag disposables;

        public PlayerControlsSettingsAdapter(
            ISettingsModel settingsModel,
            IPlayerRuntimeModel playerRuntimeModel)
        {
            this.settingsModel = settingsModel;
            this.playerRuntimeModel = playerRuntimeModel;
        }

        public void Initialize()
        {
            settingsModel.MouseSensitivity.Subscribe(_ => ApplyToLocalPlayer()).AddTo(ref disposables);
            settingsModel.InvertYAxis.Subscribe(_ => ApplyToLocalPlayer()).AddTo(ref disposables);
            settingsModel.CrouchMode.Subscribe(_ => ApplyToLocalPlayer()).AddTo(ref disposables);
            settingsModel.SprintMode.Subscribe(_ => ApplyToLocalPlayer()).AddTo(ref disposables);
            playerRuntimeModel.LocalPlayer.Subscribe(_ => ApplyToLocalPlayer()).AddTo(ref disposables);

            ApplyToLocalPlayer();
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private void ApplyToLocalPlayer()
        {
            global::PlayerInstance localPlayer = playerRuntimeModel.LocalPlayer.CurrentValue;
            global::IPlayerControlsSettingsReceiver receiver = localPlayer != null
                ? localPlayer.GetComponent<global::IPlayerControlsSettingsReceiver>()
                : null;

            receiver?.ApplyControlSettings(new PlayerControlSettings(
                settingsModel.MouseSensitivity.CurrentValue,
                settingsModel.InvertYAxis.CurrentValue,
                settingsModel.CrouchMode.CurrentValue,
                settingsModel.SprintMode.CurrentValue));
        }
    }
}