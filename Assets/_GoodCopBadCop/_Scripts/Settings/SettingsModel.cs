using System;
using GoodCopBadCop.Infrastructure.Persistence;
using GoodCopBadCop.VoiceChat;
using R3;

namespace GoodCopBadCop.Settings
{
    public enum EDisplayMode
    {
        Fullscreen,
        Borderless,
        Windowed
    }

    public enum EScreenResolution
    {
        R1920x1080,
        R1600x900,
        R1280x720
    }

    public interface ISettingsModel
    {
        ReadOnlyReactiveProperty<EDisplayMode> DisplayMode { get; }
        ReadOnlyReactiveProperty<EScreenResolution> ScreenResolution { get; }
        ReadOnlyReactiveProperty<bool> VoiceChatEnabled { get; }
        ReadOnlyReactiveProperty<bool> VoiceChatMuted { get; }
        ReadOnlyReactiveProperty<bool> VoiceChatDeafened { get; }
        ReadOnlyReactiveProperty<EVoiceChatInputMode> VoiceChatInputMode { get; }
        ReadOnlyReactiveProperty<int> VoiceChatProximityRange { get; }
        ReadOnlyReactiveProperty<string> VoiceChatMicrophoneName { get; }
    }

    public sealed class SettingsModel : ISettingsModel, IDisposable
    {
        public readonly PersistentReactiveProperty<EDisplayMode> DisplayModeMutable =
            new("settings.displayMode", EDisplayMode.Borderless);

        public readonly PersistentReactiveProperty<EScreenResolution> ScreenResolutionMutable =
            new("settings.screenResolution", EScreenResolution.R1920x1080);

        public readonly PersistentReactiveProperty<bool> VoiceChatEnabledMutable =
            new("settings.voiceChat.enabled", true);

        public readonly PersistentReactiveProperty<bool> VoiceChatMutedMutable =
            new("settings.voiceChat.muted", false);

        public readonly PersistentReactiveProperty<bool> VoiceChatDeafenedMutable =
            new("settings.voiceChat.deafened", false);

        public readonly PersistentReactiveProperty<EVoiceChatInputMode> VoiceChatInputModeMutable =
            new("settings.voiceChat.inputMode", EVoiceChatInputMode.VoiceActivation);

        public readonly PersistentReactiveProperty<int> VoiceChatProximityRangeMutable =
            new("settings.voiceChat.proximityRange", 10);

        public readonly PersistentReactiveProperty<string> VoiceChatMicrophoneNameMutable =
            new("settings.voiceChat.microphoneName", string.Empty);

        public ReadOnlyReactiveProperty<EDisplayMode> DisplayMode => DisplayModeMutable;
        public ReadOnlyReactiveProperty<EScreenResolution> ScreenResolution => ScreenResolutionMutable;
        public ReadOnlyReactiveProperty<bool> VoiceChatEnabled => VoiceChatEnabledMutable;
        public ReadOnlyReactiveProperty<bool> VoiceChatMuted => VoiceChatMutedMutable;
        public ReadOnlyReactiveProperty<bool> VoiceChatDeafened => VoiceChatDeafenedMutable;
        public ReadOnlyReactiveProperty<EVoiceChatInputMode> VoiceChatInputMode => VoiceChatInputModeMutable;
        public ReadOnlyReactiveProperty<int> VoiceChatProximityRange => VoiceChatProximityRangeMutable;
        public ReadOnlyReactiveProperty<string> VoiceChatMicrophoneName => VoiceChatMicrophoneNameMutable;

        public void Flush()
        {
            DisplayModeMutable.Flush();
            ScreenResolutionMutable.Flush();
            VoiceChatEnabledMutable.Flush();
            VoiceChatMutedMutable.Flush();
            VoiceChatDeafenedMutable.Flush();
            VoiceChatInputModeMutable.Flush();
            VoiceChatProximityRangeMutable.Flush();
            VoiceChatMicrophoneNameMutable.Flush();
        }

        public void Dispose()
        {
            DisplayModeMutable.Dispose();
            ScreenResolutionMutable.Dispose();
            VoiceChatEnabledMutable.Dispose();
            VoiceChatMutedMutable.Dispose();
            VoiceChatDeafenedMutable.Dispose();
            VoiceChatInputModeMutable.Dispose();
            VoiceChatProximityRangeMutable.Dispose();
            VoiceChatMicrophoneNameMutable.Dispose();
        }
    }
}
