using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public interface IFullscreenEffectService
    {
        void Play(FullscreenEffectSettings settings, EffectContext context);
    }

    public sealed class FullscreenEffectService : IFullscreenEffectService
    {
        private FullscreenEffectView view;

        public void Play(FullscreenEffectSettings settings, EffectContext context)
        {
            if (settings == null || !settings.Enabled)
                return;

            if (settings.Mode != EFullscreenEffectMode.OverlaySprite)
                return;

            if (!TryGetView(out FullscreenEffectView fullscreenView))
            {
                Debug.LogWarning("[FullscreenEffectService] FullscreenEffectView is not available.");
                return;
            }

            fullscreenView.Play(settings);
        }

        private bool TryGetView(out FullscreenEffectView fullscreenView)
        {
            if (view == null)
                view = Object.FindFirstObjectByType<FullscreenEffectView>();

            if (view == null)
                view = FullscreenEffectView.CreateDefaultView();

            fullscreenView = view;
            return fullscreenView != null;
        }
    }
}