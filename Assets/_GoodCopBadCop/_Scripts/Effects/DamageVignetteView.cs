using System.Collections;
using GoodCopBadCop.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Local-only hurt feedback: pulses a red post-processing Vignette on a dedicated runtime global
    /// Volume. Only the Vignette override is touched (never Color Adjustments), and the pulse is
    /// expressed through the volume's weight, so URP blends from the base volume's own vignette toward
    /// the hurt vignette and the Brightness preference / base color grade are left untouched.
    /// Driven by <see cref="FullscreenEffectService"/> for presets using <see cref="EFullscreenEffectMode.Vignette"/>.
    /// </summary>
    public sealed class DamageVignetteView : MonoBehaviour
    {
        // Above the base "Post Processing" volume (and any narrative volumes) so the hurt vignette
        // always layers on top regardless of URP's tie-break order between game instances.
        private const float VolumePriority = 100f;

        private Volume volume;
        private VolumeProfile runtimeProfile;
        private Vignette vignette;
        private Coroutine activeRoutine;

        public static DamageVignetteView CreateDefaultView()
        {
            var viewObject = new GameObject("Damage Vignette Volume");

            // Match the base volume's layer so it falls inside the camera's Volume Mask.
            var baseAnchor = Object.FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();
            if (baseAnchor != null)
                viewObject.layer = baseAnchor.gameObject.layer;

            Object.DontDestroyOnLoad(viewObject);

            var view = viewObject.AddComponent<DamageVignetteView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Damage Vignette Profile (Runtime)";

            vignette = runtimeProfile.Add<Vignette>();
            vignette.active = true;
            vignette.color.overrideState = true;
            vignette.intensity.overrideState = true;
            vignette.smoothness.overrideState = true;

            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.weight = 0f;
            volume.sharedProfile = runtimeProfile;
        }

        public void Play(FullscreenEffectSettings settings)
        {
            if (vignette == null || volume == null)
                return;

            Color color = settings.Tint;
            color.a = 1f;
            vignette.color.value = color;
            vignette.intensity.value = settings.VignetteIntensity;
            vignette.smoothness.value = settings.VignetteSmoothness;

            if (activeRoutine != null)
                StopCoroutine(activeRoutine);

            activeRoutine = StartCoroutine(PlayRoutine(settings));
        }

        /// <summary>Immediately clears any active hurt vignette (e.g. on death/spectate).</summary>
        public void Hide()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            if (volume != null)
                volume.weight = 0f;
        }

        private IEnumerator PlayRoutine(FullscreenEffectSettings settings)
        {
            float duration = Mathf.Max(0.01f, settings.Duration);
            float peak = Mathf.Clamp01(settings.Opacity);
            // Carry over any still-fading previous hit so rapid hits stack smoothly instead of popping.
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
            activeRoutine = null;
        }

        private void OnDisable()
        {
            activeRoutine = null;
            if (volume != null)
                volume.weight = 0f;
        }

        private void OnDestroy()
        {
            if (runtimeProfile != null)
                Destroy(runtimeProfile);
        }
    }
}
