using UnityEngine;

namespace GoodCopBadCop.Settings
{
    public interface ISettingsScreenAdapter
    {
        void Apply(
            EDisplayMode displayMode,
            EScreenResolution screenResolution,
            bool vSyncEnabled,
            EFpsLimit fpsLimit);
    }

    public sealed class UnitySettingsScreenAdapter : ISettingsScreenAdapter
    {
        public void Apply(
            EDisplayMode displayMode,
            EScreenResolution screenResolution,
            bool vSyncEnabled,
            EFpsLimit fpsLimit)
        {
            Vector2Int resolution = GetResolution(screenResolution);
            Screen.SetResolution(resolution.x, resolution.y, GetFullScreenMode(displayMode));

            if (vSyncEnabled)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = GetTargetFrameRate(fpsLimit);
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

        public static int GetTargetFrameRate(EFpsLimit fpsLimit)
        {
            switch (fpsLimit)
            {
                case EFpsLimit.Fps30:
                    return 30;
                case EFpsLimit.Fps60:
                    return 60;
                case EFpsLimit.Fps120:
                    return 120;
                case EFpsLimit.Fps144:
                    return 144;
                default:
                    return -1;
            }
        }
    }
}
