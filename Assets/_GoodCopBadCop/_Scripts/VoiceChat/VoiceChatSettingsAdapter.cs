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
                    // Dissonance caps RemoteVoiceVolume at 1.0, so keep it at unity (or silent at 0%)
                    // and drive loudness through the playback gain stage, which can boost above 1.0.
                    VoicePlaybackGain.SetVolumePercent(value);
                    DissonanceComms comms = commsRuntime.Comms;
                    if (comms != null)
                    {
                        comms.RemoteVoiceVolume = value <= 0f ? 0f : 1f;
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
