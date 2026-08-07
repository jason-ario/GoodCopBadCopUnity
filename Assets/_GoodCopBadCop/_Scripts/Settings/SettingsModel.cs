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

    public enum EFpsLimit
    {
        Unlimited,
        Fps30,
        Fps60,
        Fps120,
        Fps144
    }

    public enum EInputActivationMode
    {
        Hold,
        Toggle
    }

    public interface ISettingsModel
    {
        ReadOnlyReactiveProperty<EDisplayMode> DisplayMode { get; }
        ReadOnlyReactiveProperty<EScreenResolution> ScreenResolution { get; }
        ReadOnlyReactiveProperty<bool> VSyncEnabled { get; }
        ReadOnlyReactiveProperty<EFpsLimit> FpsLimit { get; }
        ReadOnlyReactiveProperty<float> MouseSensitivity { get; }
        ReadOnlyReactiveProperty<bool> InvertYAxis { get; }
        ReadOnlyReactiveProperty<EInputActivationMode> CrouchMode { get; }
        ReadOnlyReactiveProperty<EInputActivationMode> SprintMode { get; }
        ReadOnlyReactiveProperty<float> MasterVolume { get; }
        ReadOnlyReactiveProperty<float> MusicVolume { get; }
        ReadOnlyReactiveProperty<float> SfxVolume { get; }
        ReadOnlyReactiveProperty<float> VoiceVolume { get; }
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

        public readonly PersistentReactiveProperty<bool> VSyncEnabledMutable =
            new("settings.vSync.enabled", true);

        public readonly PersistentReactiveProperty<EFpsLimit> FpsLimitMutable =
            new("settings.fpsLimit", EFpsLimit.Unlimited);

        public readonly PersistentReactiveProperty<float> MouseSensitivityMutable =
            new("settings.controls.mouseSensitivity", 50f);

        public readonly PersistentReactiveProperty<bool> InvertYAxisMutable =
            new("settings.controls.invertYAxis", false);

        public readonly PersistentReactiveProperty<EInputActivationMode> CrouchModeMutable =
            new("settings.controls.crouchMode", EInputActivationMode.Hold);

        public readonly PersistentReactiveProperty<EInputActivationMode> SprintModeMutable =
            new("settings.controls.sprintMode", EInputActivationMode.Hold);

        public readonly PersistentReactiveProperty<float> MasterVolumeMutable =
            new("settings.audio.masterVolume", 80f);

        public readonly PersistentReactiveProperty<float> MusicVolumeMutable =
            new("settings.audio.musicVolume", 70f);

        public readonly PersistentReactiveProperty<float> SfxVolumeMutable =
            new("settings.audio.sfxVolume", 80f);

        public readonly PersistentReactiveProperty<float> VoiceVolumeMutable =
            new("settings.audio.voiceVolume", 80f);

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
        public ReadOnlyReactiveProperty<bool> VSyncEnabled => VSyncEnabledMutable;
        public ReadOnlyReactiveProperty<EFpsLimit> FpsLimit => FpsLimitMutable;
        public ReadOnlyReactiveProperty<float> MouseSensitivity => MouseSensitivityMutable;
        public ReadOnlyReactiveProperty<bool> InvertYAxis => InvertYAxisMutable;
        public ReadOnlyReactiveProperty<EInputActivationMode> CrouchMode => CrouchModeMutable;
        public ReadOnlyReactiveProperty<EInputActivationMode> SprintMode => SprintModeMutable;
        public ReadOnlyReactiveProperty<float> MasterVolume => MasterVolumeMutable;
        public ReadOnlyReactiveProperty<float> MusicVolume => MusicVolumeMutable;
        public ReadOnlyReactiveProperty<float> SfxVolume => SfxVolumeMutable;
        public ReadOnlyReactiveProperty<float> VoiceVolume => VoiceVolumeMutable;
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
            VSyncEnabledMutable.Flush();
            FpsLimitMutable.Flush();
            MouseSensitivityMutable.Flush();
            InvertYAxisMutable.Flush();
            CrouchModeMutable.Flush();
            SprintModeMutable.Flush();
            MasterVolumeMutable.Flush();
            MusicVolumeMutable.Flush();
            SfxVolumeMutable.Flush();
            VoiceVolumeMutable.Flush();
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
            VSyncEnabledMutable.Dispose();
            FpsLimitMutable.Dispose();
            MouseSensitivityMutable.Dispose();
            InvertYAxisMutable.Dispose();
            CrouchModeMutable.Dispose();
            SprintModeMutable.Dispose();
            MasterVolumeMutable.Dispose();
            MusicVolumeMutable.Dispose();
            SfxVolumeMutable.Dispose();
            VoiceVolumeMutable.Dispose();
            VoiceChatEnabledMutable.Dispose();
            VoiceChatMutedMutable.Dispose();
            VoiceChatDeafenedMutable.Dispose();
            VoiceChatInputModeMutable.Dispose();
            VoiceChatProximityRangeMutable.Dispose();
            VoiceChatMicrophoneNameMutable.Dispose();
        }
    }
}
