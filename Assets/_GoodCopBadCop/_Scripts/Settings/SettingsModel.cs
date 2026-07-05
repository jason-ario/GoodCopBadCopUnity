using System;
using GoodCopBadCop.Infrastructure.Persistence;
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
    }
    public sealed class SettingsModel : ISettingsModel, IDisposable
    {
        public readonly PersistentReactiveProperty<EDisplayMode> DisplayModeMutable =
            new("settings.displayMode", EDisplayMode.Borderless);
        public readonly PersistentReactiveProperty<EScreenResolution> ScreenResolutionMutable =
            new("settings.screenResolution", EScreenResolution.R1920x1080);
        public ReadOnlyReactiveProperty<EDisplayMode> DisplayMode => DisplayModeMutable;
        public ReadOnlyReactiveProperty<EScreenResolution> ScreenResolution => ScreenResolutionMutable;
        public void Flush()
        {
            DisplayModeMutable.Flush();
            ScreenResolutionMutable.Flush();
        }
        public void Dispose()
        {
            DisplayModeMutable.Dispose();
            ScreenResolutionMutable.Dispose();
        }
    }
}