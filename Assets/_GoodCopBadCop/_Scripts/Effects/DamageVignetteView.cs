using System.Collections;
using System.Collections.Generic;
using GoodCopBadCop.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Local-only fullscreen feedback built from URP post-processing: a colored Vignette plus optional
    /// Chromatic Aberration, Lens Distortion, and Color Adjustments (saturation / color filter only —
    /// never post exposure, so the Brightness preference is left untouched).
    /// Each preset gets its own runtime global Volume ("channel") and the pulse is expressed through
    /// that volume's weight, so URP blends from the base look toward the effect, and overlapping
    /// effects (e.g. a hit while drunk) layer instead of cutting each other off.
    /// Driven by <see cref="FullscreenEffectService"/> for presets using <see cref="EFullscreenEffectMode.Vignette"/>.
    /// </summary>
    public sealed class DamageVignetteView : MonoBehaviour
    {
        // Above the base "Post Processing" volume (and any narrative volumes) so the effect
        // always layers on top regardless of URP's tie-break order between game instances.
        private const float VolumePriority = 100f;

        private sealed class Channel
        {
            public Volume Volume;
            public VolumeProfile Profile;
            public Vignette Vignette;
            public ChromaticAberration ChromaticAberration;
            public LensDistortion LensDistortion;
            public ColorAdjustments ColorAdjustments;
            public Coroutine Routine;
        }

        private readonly Dictionary<FullscreenEffectSettings, Channel> channels =
            new Dictionary<FullscreenEffectSettings, Channel>();

        public static DamageVignetteView CreateDefaultView()
        {
            var viewObject = new GameObject("Damage Vignette Volume");

            // Match the base volume's layer so channels fall inside the camera's Volume Mask.
            var baseAnchor = Object.FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();
            if (baseAnchor != null)
                viewObject.layer = baseAnchor.gameObject.layer;

            Object.DontDestroyOnLoad(viewObject);
            return viewObject.AddComponent<DamageVignetteView>();
        }

        public void Play(FullscreenEffectSettings settings)
        {
            if (settings == null)
                return;

            Channel channel = GetOrCreateChannel(settings);
            Configure(channel, settings);

            if (channel.Routine != null)
                StopCoroutine(channel.Routine);

            channel.Routine = StartCoroutine(PlayRoutine(channel, settings));
        }

        /// <summary>Immediately clears every active effect (e.g. on death/spectate).</summary>
        public void Hide()
        {
            foreach (Channel channel in channels.Values)
            {
                if (channel.Routine != null)
                {
                    StopCoroutine(channel.Routine);
                    channel.Routine = null;
                }

                if (channel.Volume != null)
                    channel.Volume.weight = 0f;
            }
        }

        private Channel GetOrCreateChannel(FullscreenEffectSettings settings)
        {
            if (channels.TryGetValue(settings, out Channel existing) && existing.Volume != null)
                return existing;

            var root = new GameObject("Effect Channel");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Effect Channel Profile (Runtime)";

            var channel = new Channel
            {
                Profile = profile,
                Vignette = profile.Add<Vignette>(),
                ChromaticAberration = profile.Add<ChromaticAberration>(),
                LensDistortion = profile.Add<LensDistortion>(),
                ColorAdjustments = profile.Add<ColorAdjustments>()
            };

            channel.Volume = root.AddComponent<Volume>();
            channel.Volume.isGlobal = true;
            channel.Volume.priority = VolumePriority;
            channel.Volume.weight = 0f;
            channel.Volume.sharedProfile = profile;

            channels[settings] = channel;
            return channel;
        }

        private static void Configure(Channel channel, FullscreenEffectSettings settings)
        {
            Color vignetteColor = settings.Tint;
            vignetteColor.a = 1f;

            channel.Vignette.active = settings.VignetteIntensity > 0f;
            channel.Vignette.color.Override(vignetteColor);
            channel.Vignette.intensity.Override(settings.VignetteIntensity);
            channel.Vignette.smoothness.Override(settings.VignetteSmoothness);

            channel.ChromaticAberration.active = settings.ChromaticAberration > 0f;
            channel.ChromaticAberration.intensity.Override(settings.ChromaticAberration);

            channel.LensDistortion.active = !Mathf.Approximately(settings.LensDistortion, 0f);
            channel.LensDistortion.intensity.Override(settings.LensDistortion);

            bool useSaturation = !Mathf.Approximately(settings.Saturation, 0f);
            bool useTint = settings.ScreenTintAmount > 0f;
            channel.ColorAdjustments.active = useSaturation || useTint;

            // Only saturation and color filter are ever overridden — post exposure belongs to the
            // Brightness preference on the base volume.
            channel.ColorAdjustments.saturation.overrideState = useSaturation;
            channel.ColorAdjustments.saturation.value = settings.Saturation;

            Color filter = Color.Lerp(Color.white, settings.ScreenTint, settings.ScreenTintAmount);
            filter.a = 1f;
            channel.ColorAdjustments.colorFilter.overrideState = useTint;
            channel.ColorAdjustments.colorFilter.value = filter;
        }

        private IEnumerator PlayRoutine(Channel channel, FullscreenEffectSettings settings)
        {
            Volume volume = channel.Volume;
            float duration = Mathf.Max(0.01f, settings.Duration);
            float peak = Mathf.Clamp01(settings.Opacity);
            // Carry over any still-fading previous pulse so rapid retriggers stack smoothly instead of popping.
            float carry = volume.weight;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float target = peak * Mathf.Clamp01(settings.OpacityCurve.Evaluate(t));
                volume.weight = Mathf.Max(target, carry * (1f - t));

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            volume.weight = 0f;
            channel.Routine = null;
        }

        private void OnDisable()
        {
            foreach (Channel channel in channels.Values)
            {
                channel.Routine = null;
                if (channel.Volume != null)
                    channel.Volume.weight = 0f;
            }
        }

        private void OnDestroy()
        {
            foreach (Channel channel in channels.Values)
            {
                if (channel.Profile != null)
                    Destroy(channel.Profile);
            }

            channels.Clear();
        }
    }
}
