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
        private DamageVignetteView vignetteView;
        private GlitchEffectView glitchView;

        public void Play(FullscreenEffectSettings settings, EffectContext context)
        {
            if (settings == null || !settings.Enabled)
                return;

            switch (settings.Mode)
            {
                case EFullscreenEffectMode.OverlaySprite:
                    if (!TryGetView(out FullscreenEffectView fullscreenView))
                    {
                        Debug.LogWarning("[FullscreenEffectService] FullscreenEffectView is not available.");
                        return;
                    }

                    fullscreenView.Play(settings);
                    break;

                case EFullscreenEffectMode.Vignette:
                    if (!TryGetVignetteView(out DamageVignetteView damageVignetteView))
                    {
                        Debug.LogWarning("[FullscreenEffectService] DamageVignetteView is not available.");
                        return;
                    }

                    damageVignetteView.Play(settings);
                    break;

                case EFullscreenEffectMode.Glitch:
                    if (glitchView == null)
                        glitchView = Object.FindFirstObjectByType<GlitchEffectView>();
                    if (glitchView == null)
                        glitchView = GlitchEffectView.CreateDefaultView();

                    glitchView.Play(settings);
                    break;
            }
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

        private bool TryGetVignetteView(out DamageVignetteView damageVignetteView)
        {
            if (vignetteView == null)
                vignetteView = Object.FindFirstObjectByType<DamageVignetteView>();

            if (vignetteView == null)
                vignetteView = DamageVignetteView.CreateDefaultView();

            damageVignetteView = vignetteView;
            return damageVignetteView != null;
        }
    }
}
