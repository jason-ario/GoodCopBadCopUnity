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
            public ChromaticAberration ChromaticAberration;
            public LensDistortion LensDistortion;
            public ColorAdjustments ColorAdjustments;

            // The vignette lives on its own full-weight volume and is blended manually. Blending it
            // through volume weight would lerp its color from the base (black) vignette, turning
            // colored hit vignettes muddy/black for most of the pulse.
            public Volume VignetteVolume;
            public VolumeProfile VignetteProfile;
            public Vignette Vignette;
            public Color BaseVignetteColor = Color.black;
            public float BaseVignetteIntensity;
            public float BaseVignetteSmoothness = 0.2f;

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

            // Only sample the underlying look while this channel's vignette is idle, otherwise the
            // stack would already contain our own contribution.
            if (channel.VignetteVolume.weight <= 0f)
                CaptureBaseVignette(channel);

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

                SetChannelIdle(channel);
            }
        }

        private static void SetChannelIdle(Channel channel)
        {
            if (channel.Volume != null)
                channel.Volume.weight = 0f;
            if (channel.VignetteVolume != null)
                channel.VignetteVolume.weight = 0f;
        }

        private static void CaptureBaseVignette(Channel channel)
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            Vignette baseVignette = stack != null ? stack.GetComponent<Vignette>() : null;
            if (baseVignette == null || !baseVignette.active)
            {
                channel.BaseVignetteColor = Color.black;
                channel.BaseVignetteIntensity = 0f;
                channel.BaseVignetteSmoothness = 0.2f;
                return;
            }

            channel.BaseVignetteColor = baseVignette.color.value;
            channel.BaseVignetteIntensity = baseVignette.intensity.value;
            channel.BaseVignetteSmoothness = baseVignette.smoothness.value;
        }

        private Volume CreateVolume(string name, VolumeProfile profile)
        {
            var root = new GameObject(name);
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);

            var volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        private Channel GetOrCreateChannel(FullscreenEffectSettings settings)
        {
            if (channels.TryGetValue(settings, out Channel existing) && existing.Volume != null)
                return existing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Effect Channel Profile (Runtime)";

            var vignetteProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            vignetteProfile.name = "Effect Channel Vignette Profile (Runtime)";

            var channel = new Channel
            {
                Profile = profile,
                ChromaticAberration = profile.Add<ChromaticAberration>(),
                LensDistortion = profile.Add<LensDistortion>(),
                ColorAdjustments = profile.Add<ColorAdjustments>(),
                VignetteProfile = vignetteProfile,
                Vignette = vignetteProfile.Add<Vignette>()
            };

            channel.Volume = CreateVolume("Effect Channel", profile);
            channel.VignetteVolume = CreateVolume("Effect Channel Vignette", vignetteProfile);

            channels[settings] = channel;
            return channel;
        }

        private static void Configure(Channel channel, FullscreenEffectSettings settings)
        {
            channel.Vignette.active = settings.VignetteIntensity > 0f;
            ApplyVignette(channel, settings, 0f, 0f);

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

        /// <summary>
        /// Blends the vignette manually from the captured base look toward the preset.
        /// <paramref name="colorBlend"/> is normalized to the pulse peak so the vignette reaches the
        /// full tint color at peak, instead of a darkened mix with the base vignette color.
        /// </summary>
        private static void ApplyVignette(Channel channel, FullscreenEffectSettings settings, float blend, float colorBlend)
        {
            Color tint = settings.Tint;
            tint.a = 1f;

            Color color = Color.Lerp(channel.BaseVignetteColor, tint, colorBlend);
            color.a = 1f;

            channel.Vignette.color.Override(color);
            channel.Vignette.intensity.Override(Mathf.Lerp(channel.BaseVignetteIntensity, settings.VignetteIntensity, blend));
            channel.Vignette.smoothness.Override(Mathf.Lerp(channel.BaseVignetteSmoothness, settings.VignetteSmoothness, blend));
        }

        private IEnumerator PlayRoutine(Channel channel, FullscreenEffectSettings settings)
        {
            Volume volume = channel.Volume;
            float duration = Mathf.Max(0.01f, settings.Duration);
            float peak = Mathf.Clamp01(settings.Opacity);
            // Carry over any still-fading previous pulse so rapid retriggers stack smoothly instead of popping.
            float carry = volume.weight;
            float elapsed = 0f;

            if (channel.Vignette.active)
                channel.VignetteVolume.weight = 1f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float target = peak * Mathf.Clamp01(settings.OpacityCurve.Evaluate(t));
                float weight = Mathf.Max(target, carry * (1f - t));
                volume.weight = weight;

                if (channel.Vignette.active)
                    ApplyVignette(channel, settings, weight, peak > 0f ? Mathf.Clamp01(weight / peak) : 0f);

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetChannelIdle(channel);
            channel.Routine = null;
        }

        private void OnDisable()
        {
            foreach (Channel channel in channels.Values)
            {
                channel.Routine = null;
                SetChannelIdle(channel);
            }
        }

        private void OnDestroy()
        {
            foreach (Channel channel in channels.Values)
            {
                if (channel.Profile != null)
                    Destroy(channel.Profile);
                if (channel.VignetteProfile != null)
                    Destroy(channel.VignetteProfile);
            }

            channels.Clear();
        }
    }
}
