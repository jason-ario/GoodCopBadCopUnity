using GoodCopBadCop.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Local-only, sustained "drunk" look shown for the whole time the local player is drunk:
    /// a purple edge vignette, slow woozy lens distortion that wobbles its center, and light
    /// chromatic aberration. This is the only drunk vignette: the Drunk preset pulse has its
    /// vignetteIntensity at 0, because its full-weight channel vignette (priority 100) would
    /// otherwise override this one and make the vignette appear only as a pulse. Built on its own runtime global URP Volume whose weight fades in when the player
    /// gets drunk and out when they sober up. Sits below <see cref="DamageVignetteView"/>'s channels,
    /// so the periodic Drunk preset pulse and hit effects still layer on top.
    /// Driven by <see cref="PlayerDrunkEffectsAdapter"/>.
    /// </summary>
    public sealed class DrunkScreenEffectView : MonoBehaviour
    {
        // Above the base "Post Processing" volume, below the effect channels (100).
        private const float VolumePriority = 90f;

        private const float FadeInSeconds = 1.5f;
        private const float FadeOutSeconds = 2.5f;

        // Purple, kept to the screen edges: moderate intensity with a tight falloff so the
        // tint doesn't creep toward the centre.
        private static readonly Color VignetteColor = new Color(0.42f, 0.08f, 0.55f);
        private const float VignetteIntensity = 0.4f;
        private const float VignetteSmoothness = 0.3f;

        private const float ChromaticAberrationIntensity = 0.35f;

        private const float LensDistortionBase = 0.18f;
        private const float LensDistortionWobble = 0.12f;
        private const float LensDistortionWobbleHz = 0.3f;
        private const float LensCenterWobble = 0.035f;

        private Volume _volume;
        private VolumeProfile _profile;
        private LensDistortion _lensDistortion;

        private float _weight;
        private float _targetWeight;

        public static DrunkScreenEffectView CreateDefaultView()
        {
            var viewObject = new GameObject("Drunk Screen Effect Volume");

            // Match the base volume's layer so the volume falls inside the camera's Volume Mask.
            var baseAnchor = Object.FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();
            if (baseAnchor != null)
                viewObject.layer = baseAnchor.gameObject.layer;

            Object.DontDestroyOnLoad(viewObject);
            return viewObject.AddComponent<DrunkScreenEffectView>();
        }

        /// <summary>Fades the drunk look in (true) or out (false).</summary>
        public void SetDrunk(bool isDrunk) => _targetWeight = isDrunk ? 1f : 0f;

        private void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Drunk Screen Effect Profile (Runtime)";

            Vignette vignette = _profile.Add<Vignette>(true);
            vignette.color.value = VignetteColor;
            vignette.intensity.value = VignetteIntensity;
            vignette.smoothness.value = VignetteSmoothness;

            ChromaticAberration chromatic = _profile.Add<ChromaticAberration>(true);
            chromatic.intensity.value = ChromaticAberrationIntensity;

            _lensDistortion = _profile.Add<LensDistortion>(true);
            _lensDistortion.intensity.value = LensDistortionBase;

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = VolumePriority;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;
            _volume.enabled = false;
        }

        private void Update()
        {
            if (_volume == null) return;

            if (!Mathf.Approximately(_weight, _targetWeight))
            {
                float fade = _targetWeight > _weight ? FadeInSeconds : FadeOutSeconds;
                _weight = Mathf.MoveTowards(_weight, _targetWeight, Time.unscaledDeltaTime / fade);
                _volume.weight = _weight;
            }

            bool visible = _weight > 0f;
            if (_volume.enabled != visible)
                _volume.enabled = visible;
            if (!visible) return;

            // Slow, uneven sway so the world seems to swim rather than pulse on a beat.
            float t = Time.unscaledTime;
            float wobble = Mathf.Sin(t * LensDistortionWobbleHz * 2f * Mathf.PI)
                           + 0.5f * Mathf.Sin(t * LensDistortionWobbleHz * 3.1f * Mathf.PI);
            _lensDistortion.intensity.value = LensDistortionBase + wobble * (LensDistortionWobble / 1.5f);
            _lensDistortion.center.value = new Vector2(
                0.5f + Mathf.Sin(t * 0.53f) * LensCenterWobble,
                0.5f + Mathf.Cos(t * 0.71f) * LensCenterWobble);
        }

        private void OnDestroy()
        {
            if (_profile != null)
                Destroy(_profile);
        }
    }
}
