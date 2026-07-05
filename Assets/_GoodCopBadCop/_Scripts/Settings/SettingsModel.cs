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

    public interface ISettingsModel
    {
        ReadOnlyReactiveProperty<EDisplayMode> DisplayMode { get; }
    }

    public sealed class SettingsModel : ISettingsModel, IDisposable
    {
        public readonly PersistentReactiveProperty<EDisplayMode> DisplayModeMutable =
            new("settings.displayMode", EDisplayMode.Borderless);

        public ReadOnlyReactiveProperty<EDisplayMode> DisplayMode => DisplayModeMutable;

        public void Flush()
        {
            DisplayModeMutable.Flush();
        }

        public void Dispose()
        {
            DisplayModeMutable.Dispose();
        }
    }
}
