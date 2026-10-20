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
            FullScreenMode fullScreenMode = GetFullScreenMode(displayMode);

            // A window larger than the desktop is unusable; fullscreen modes may exceed it (downsampled).
            if (fullScreenMode == FullScreenMode.Windowed)
                resolution = ClampToDisplay(resolution);

            Screen.SetResolution(resolution.x, resolution.y, fullScreenMode);

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
                case EScreenResolution.R3840x2160:
                    return new Vector2Int(3840, 2160);
                case EScreenResolution.R3200x1800:
                    return new Vector2Int(3200, 1800);
                case EScreenResolution.R2560x1440:
                    return new Vector2Int(2560, 1440);
                case EScreenResolution.R1280x720:
                    return new Vector2Int(1280, 720);
                case EScreenResolution.R1600x900:
                    return new Vector2Int(1600, 900);
                default:
                    return new Vector2Int(1920, 1080);
            }
        }

        private static Vector2Int ClampToDisplay(Vector2Int resolution)
        {
            int displayWidth = Display.main.systemWidth;
            int displayHeight = Display.main.systemHeight;

            if (displayWidth <= 0 || displayHeight <= 0)
                return resolution;

            if (resolution.x <= displayWidth && resolution.y <= displayHeight)
                return resolution;

            float scale = Mathf.Min((float)displayWidth / resolution.x, (float)displayHeight / resolution.y);
            return new Vector2Int(
                Mathf.Max(1, Mathf.FloorToInt(resolution.x * scale)),
                Mathf.Max(1, Mathf.FloorToInt(resolution.y * scale)));
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
