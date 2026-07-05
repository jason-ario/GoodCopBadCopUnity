using System;
using R3;

namespace GoodCopBadCop.VoiceChat
{
    public enum EVoiceChatInputMode
    {
        VoiceActivation,
        PushToTalk,
        OpenMic
    }

    public interface IVoiceChatModel
    {
        ReadOnlyReactiveProperty<bool> IsEnabled { get; }
        ReadOnlyReactiveProperty<bool> IsMuted { get; }
        ReadOnlyReactiveProperty<bool> IsDeafened { get; }
        ReadOnlyReactiveProperty<EVoiceChatInputMode> InputMode { get; }
        ReadOnlyReactiveProperty<int> ProximityRange { get; }
        ReadOnlyReactiveProperty<string> MicrophoneName { get; }
        ReadOnlyReactiveProperty<bool> IsCommsAvailable { get; }
        ReadOnlyReactiveProperty<bool> IsNetworkReady { get; }
        ReadOnlyReactiveProperty<bool> IsLocalSpeaking { get; }
    }

    public sealed class VoiceChatModel : IVoiceChatModel, IDisposable
    {
        public readonly ReactiveProperty<bool> IsEnabledMutable = new(true);
        public readonly ReactiveProperty<bool> IsMutedMutable = new(false);
        public readonly ReactiveProperty<bool> IsDeafenedMutable = new(false);
        public readonly ReactiveProperty<EVoiceChatInputMode> InputModeMutable = new(EVoiceChatInputMode.VoiceActivation);
        public readonly ReactiveProperty<int> ProximityRangeMutable = new(10);
        public readonly ReactiveProperty<string> MicrophoneNameMutable = new(string.Empty);
        public readonly ReactiveProperty<bool> IsCommsAvailableMutable = new(false);
        public readonly ReactiveProperty<bool> IsNetworkReadyMutable = new(false);
        public readonly ReactiveProperty<bool> IsLocalSpeakingMutable = new(false);

        public ReadOnlyReactiveProperty<bool> IsEnabled => IsEnabledMutable;
        public ReadOnlyReactiveProperty<bool> IsMuted => IsMutedMutable;
        public ReadOnlyReactiveProperty<bool> IsDeafened => IsDeafenedMutable;
        public ReadOnlyReactiveProperty<EVoiceChatInputMode> InputMode => InputModeMutable;
        public ReadOnlyReactiveProperty<int> ProximityRange => ProximityRangeMutable;
        public ReadOnlyReactiveProperty<string> MicrophoneName => MicrophoneNameMutable;
        public ReadOnlyReactiveProperty<bool> IsCommsAvailable => IsCommsAvailableMutable;
        public ReadOnlyReactiveProperty<bool> IsNetworkReady => IsNetworkReadyMutable;
        public ReadOnlyReactiveProperty<bool> IsLocalSpeaking => IsLocalSpeakingMutable;

        public void Dispose()
        {
            IsEnabledMutable.Dispose();
            IsMutedMutable.Dispose();
            IsDeafenedMutable.Dispose();
            InputModeMutable.Dispose();
            ProximityRangeMutable.Dispose();
            MicrophoneNameMutable.Dispose();
            IsCommsAvailableMutable.Dispose();
            IsNetworkReadyMutable.Dispose();
            IsLocalSpeakingMutable.Dispose();
        }
    }
}
