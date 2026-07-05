using UnityEngine;
namespace GoodCopBadCop.Settings
{
    public interface ISettingsScreenAdapter
    {
        void Apply(EDisplayMode displayMode, EScreenResolution screenResolution);
    }
    public sealed class UnitySettingsScreenAdapter : ISettingsScreenAdapter
    {
        public void Apply(EDisplayMode displayMode, EScreenResolution screenResolution)
        {
            Vector2Int resolution = GetResolution(screenResolution);
            Screen.SetResolution(resolution.x, resolution.y, GetFullScreenMode(displayMode));
        }
        public static FullScreenMode GetFullScreenMode(EDisplayMode displayMode)
        {
            switch (displayMode)
            {
                case EDisplayMode.Fullscreen:
                    return FullScreenMode.ExclusiveFullScreen;
                case EDisplayMode.Windowed:
                    return FullScreenMode.Windowed;
                default:
                    return FullScreenMode.FullScreenWindow;
            }
        }
        public static Vector2Int GetResolution(EScreenResolution screenResolution)
        {
            switch (screenResolution)
            {
                case EScreenResolution.R1280x720:
                    return new Vector2Int(1280, 720);
                case EScreenResolution.R1600x900:
                    return new Vector2Int(1600, 900);
                default:
                    return new Vector2Int(1920, 1080);
            }
        }
    }
}