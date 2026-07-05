using UnityEngine;

namespace GoodCopBadCop.VoiceChat
{
    public interface IVoiceChatService
    {
        void SetEnabled(bool isEnabled);
        void SetMuted(bool isMuted);
        void SetDeafened(bool isDeafened);
        void SetInputMode(EVoiceChatInputMode inputMode);
        void SetProximityRange(int proximityRange);
        void SetMicrophoneName(string microphoneName);
        void SetCommsAvailable(bool isAvailable);
        void SetNetworkReady(bool isReady);
        void SetLocalSpeaking(bool isSpeaking);
    }

    public sealed class VoiceChatService : IVoiceChatService
    {
        public const int MinimumProximityRange = 1;
        public const int MaximumProximityRange = 100;

        private readonly VoiceChatModel model;

        public VoiceChatService(VoiceChatModel model)
        {
            this.model = model;
        }

        public void SetEnabled(bool isEnabled)
        {
            model.IsEnabledMutable.Value = isEnabled;
        }

        public void SetMuted(bool isMuted)
        {
            model.IsMutedMutable.Value = isMuted;
        }

        public void SetDeafened(bool isDeafened)
        {
            model.IsDeafenedMutable.Value = isDeafened;
        }

        public void SetInputMode(EVoiceChatInputMode inputMode)
        {
            model.InputModeMutable.Value = inputMode;
        }

        public void SetProximityRange(int proximityRange)
        {
            model.ProximityRangeMutable.Value = Mathf.Clamp(
                proximityRange,
                MinimumProximityRange,
                MaximumProximityRange);
        }

        public void SetMicrophoneName(string microphoneName)
        {
            model.MicrophoneNameMutable.Value = microphoneName ?? string.Empty;
        }

        public void SetCommsAvailable(bool isAvailable)
        {
            model.IsCommsAvailableMutable.Value = isAvailable;
        }

        public void SetNetworkReady(bool isReady)
        {
            model.IsNetworkReadyMutable.Value = isReady;
        }

        public void SetLocalSpeaking(bool isSpeaking)
        {
            model.IsLocalSpeakingMutable.Value = isSpeaking;
        }
    }
}
