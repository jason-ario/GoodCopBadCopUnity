using System;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.EnvironmentSystem
{
    public sealed class RainAdapter : IInitializable, IDisposable
    {
        private readonly IEnvironmentModel model;
        private readonly RainEffectController rainEffect;
        private DisposableBag disposables;

        public RainAdapter(IEnvironmentModel model, RainEffectController rainEffect)
        {
            this.model = model;
            this.rainEffect = rainEffect;
        }

        public void Initialize()
        {
            model.CurrentRainEnabled
                .Subscribe(SetRain)
                .AddTo(ref disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }

        private void SetRain(bool enabled)
        {
            if (rainEffect != null)
            {
                rainEffect.SetEnabled(enabled);
            }

            if (global::AudioManager.Instance != null)
            {
                global::AudioManager.Instance.SetRainAmbience(enabled);
            }
        }
    }
}
