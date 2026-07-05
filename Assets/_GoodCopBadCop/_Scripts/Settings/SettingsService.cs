namespace GoodCopBadCop.Settings
{
    public interface ISettingsService
    {
        void SetDisplayMode(EDisplayMode displayMode);
        void SetScreenResolution(EScreenResolution screenResolution);
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
        public void Flush()
        {
            model.Flush();
        }
    }
}