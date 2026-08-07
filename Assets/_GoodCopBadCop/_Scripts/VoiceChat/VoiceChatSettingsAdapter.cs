using System;
using Dissonance;
using GoodCopBadCop.Settings;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.VoiceChat
{
    public sealed class VoiceChatSettingsAdapter : IInitializable, IDisposable
    {
        private readonly ISettingsModel settingsModel;
        private readonly IVoiceChatService voiceChatService;
        private readonly IVoiceChatCommsRuntime commsRuntime;
        private DisposableBag disposables;

        public VoiceChatSettingsAdapter(
            ISettingsModel settingsModel,
            IVoiceChatService voiceChatService,
            IVoiceChatCommsRuntime commsRuntime)
        {
            this.settingsModel = settingsModel;
            this.voiceChatService = voiceChatService;
            this.commsRuntime = commsRuntime;
        }

        public void Initialize()
        {
            settingsModel.VoiceChatEnabled
                .Subscribe(voiceChatService.SetEnabled)
                .AddTo(ref disposables);

            settingsModel.VoiceChatMuted
                .Subscribe(voiceChatService.SetMuted)
                .AddTo(ref disposables);

            settingsModel.VoiceChatDeafened
                .Subscribe(voiceChatService.SetDeafened)
                .AddTo(ref disposables);

            settingsModel.VoiceChatInputMode
                .Subscribe(voiceChatService.SetInputMode)
                .AddTo(ref disposables);

            settingsModel.VoiceChatProximityRange
                .Subscribe(voiceChatService.SetProximityRange)
                .AddTo(ref disposables);

            settingsModel.VoiceChatMicrophoneName
                .Subscribe(voiceChatService.SetMicrophoneName)
                .AddTo(ref disposables);

            settingsModel.VoiceVolume
                .Subscribe(value =>
                {
                    DissonanceComms comms = commsRuntime.Comms;
                    if (comms != null)
                    {
                        comms.RemoteVoiceVolume = UnityEngine.Mathf.Clamp01(value / 100f);
                    }
                })
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
