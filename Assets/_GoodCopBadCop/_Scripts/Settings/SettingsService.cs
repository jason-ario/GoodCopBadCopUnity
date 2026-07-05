using GoodCopBadCop.VoiceChat;

namespace GoodCopBadCop.Settings
{
    public interface ISettingsService
    {
        void SetDisplayMode(EDisplayMode displayMode);
        void SetScreenResolution(EScreenResolution screenResolution);
        void SetVSyncEnabled(bool isEnabled);
        void SetFpsLimit(EFpsLimit fpsLimit);
        void SetVoiceChatEnabled(bool isEnabled);
        void SetVoiceChatMuted(bool isMuted);
        void SetVoiceChatDeafened(bool isDeafened);
        void SetVoiceChatInputMode(EVoiceChatInputMode inputMode);
        void SetVoiceChatProximityRange(int proximityRange);
        void SetVoiceChatMicrophoneName(string microphoneName);
        void Flush();
    }

    public sealed class SettingsService : ISettingsService
    {
        private readonly SettingsModel model;

        public SettingsService(SettingsModel model)
        {
            this.model = model;
        }

        public void SetDisplayMode(EDisplayMode displayMode)
        {
            model.DisplayModeMutable.Value = displayMode;
        }

        public void SetScreenResolution(EScreenResolution screenResolution)
        {
            model.ScreenResolutionMutable.Value = screenResolution;
        }

        public void SetVSyncEnabled(bool isEnabled)
        {
            model.VSyncEnabledMutable.Value = isEnabled;

            if (isEnabled)
            {
                model.FpsLimitMutable.Value = EFpsLimit.Unlimited;
            }
        }

        public void SetFpsLimit(EFpsLimit fpsLimit)
        {
            model.FpsLimitMutable.Value = fpsLimit;

            if (fpsLimit != EFpsLimit.Unlimited)
            {
                model.VSyncEnabledMutable.Value = false;
            }
        }

        public void SetVoiceChatEnabled(bool isEnabled)
        {
            model.VoiceChatEnabledMutable.Value = isEnabled;
        }

        public void SetVoiceChatMuted(bool isMuted)
        {
            model.VoiceChatMutedMutable.Value = isMuted;
        }

        public void SetVoiceChatDeafened(bool isDeafened)
        {
            model.VoiceChatDeafenedMutable.Value = isDeafened;
        }

        public void SetVoiceChatInputMode(EVoiceChatInputMode inputMode)
        {
            model.VoiceChatInputModeMutable.Value = inputMode;
        }

        public void SetVoiceChatProximityRange(int proximityRange)
        {
            model.VoiceChatProximityRangeMutable.Value = proximityRange;
        }

        public void SetVoiceChatMicrophoneName(string microphoneName)
        {
            model.VoiceChatMicrophoneNameMutable.Value = microphoneName ?? string.Empty;
        }

        public void Flush()
        {
            model.Flush();
        }
    }
}
