using System;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.Settings
{
    /// <summary>
    /// Applies the Gameplay-tab "Text Wobble" preference by driving the static
    /// <see cref="TMPWobbleText.GlobalWobbleEnabled"/> switch that every <see cref="TMPWobbleText"/>
    /// instance (subtitles, End of Shift Report rows, etc.) checks before animating.
    /// </summary>
    public sealed class TextWobbleApplier : IInitializable, IDisposable
    {
        private readonly ISettingsModel model;
        private DisposableBag disposables;

        public TextWobbleApplier(ISettingsModel model)
        {
            this.model = model;
        }

        public void Initialize()
        {
            model.TextWobbleEnabled.Subscribe(ApplyTextWobbleEnabled).AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private static void ApplyTextWobbleEnabled(bool isEnabled)
        {
            TMPWobbleText.GlobalWobbleEnabled = isEnabled;
        }
    }
}
