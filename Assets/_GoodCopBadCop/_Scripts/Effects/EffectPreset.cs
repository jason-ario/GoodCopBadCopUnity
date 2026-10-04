using System;
using System.Collections.Generic;
using GoodCopBadCop.CameraSystem;
using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public enum EFullscreenEffectMode
    {
        /// <summary>Full-screen UI sprite overlay (see <see cref="FullscreenEffectView"/>).</summary>
        OverlaySprite,
        /// <summary>
        /// Post-processing pulse (see <see cref="DamageVignetteView"/>): colored Vignette plus optional
        /// chromatic aberration, saturation, lens distortion, and a screen color wash.
        /// </summary>
        Vignette,
        /// <summary>
        /// Edge glitch after post-processing (see <see cref="GlitchEffectView"/>): warping, band tearing,
        /// RGB split and a tinted glow around the screen borders. Uses tint, opacity (peak intensity),
        /// duration, opacity curve and the Glitch settings.
        /// </summary>
        Glitch
    }

    [Serializable]
    public sealed class FullscreenEffectSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private EFullscreenEffectMode mode = EFullscreenEffectMode.OverlaySprite;
        [Tooltip("OverlaySprite mode only.")]
        [SerializeField] private Sprite overlaySprite;
        [Tooltip("OverlaySprite: image tint. Vignette: vignette color.")]
        [SerializeField] private Color tint = Color.white;
        [Tooltip("OverlaySprite: peak image alpha. Vignette: peak blend weight over the base look.")]
        [SerializeField, Range(0f, 1f)] private float opacity = 0.2f;
        [SerializeField, Min(0f)] private float duration = 0.35f;
        [SerializeField] private AnimationCurve opacityCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [Tooltip("Vignette mode only. Vignette intensity at full weight.")]
        [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.45f;
        [Tooltip("Vignette mode only. Higher = softer edge that bleeds further inward.")]
        [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.6f;

        [Header("Post-Processing Extras (Vignette mode, values at full weight; 0 = untouched)")]
        [Tooltip("URP Chromatic Aberration intensity.")]
        [SerializeField, Range(0f, 1f)] private float chromaticAberration;
        [Tooltip("URP Color Adjustments saturation. Negative desaturates, positive boosts.")]
        [SerializeField, Range(-100f, 100f)] private float saturation;
        [Tooltip("URP Lens Distortion intensity. Negative = pinch/punch, positive = bulge/woozy.")]
        [SerializeField, Range(-1f, 1f)] private float lensDistortion;
        [Tooltip("Color multiplied over the whole screen (Color Adjustments color filter).")]
        [SerializeField] private Color screenTint = Color.white;
        [Tooltip("How strongly the screen tint is applied. 0 disables the color wash.")]
        [SerializeField, Range(0f, 1f)] private float screenTintAmount;

        [Header("Glitch (Glitch mode only)")]
        [Tooltip("How far the glitch band reaches in from the screen edges.")]
        [SerializeField, Range(0.05f, 1f)] private float glitchEdgeWidth = 0.45f;
        [Tooltip("Noise warping and pulsing pull at the edges.")]
        [SerializeField, Range(0f, 1f)] private float glitchWarpStrength = 0.6f;
        [Tooltip("Horizontal band tearing and RGB split.")]
        [SerializeField, Range(0f, 1f)] private float glitchTearStrength = 0.6f;
        [Tooltip("Tinted glow strength at the edges.")]
        [SerializeField, Range(0f, 1f)] private float glitchGlowStrength = 0.55f;

        public bool Enabled => enabled;
        public EFullscreenEffectMode Mode => mode;
        public Sprite OverlaySprite => overlaySprite;
        public Color Tint => tint;
        public float Opacity => opacity;
        public float Duration => duration;
        public AnimationCurve OpacityCurve => opacityCurve;
        public float VignetteIntensity => vignetteIntensity;
        public float VignetteSmoothness => vignetteSmoothness;
        public float ChromaticAberration => chromaticAberration;
        public float Saturation => saturation;
        public float LensDistortion => lensDistortion;
        public Color ScreenTint => screenTint;
        public float ScreenTintAmount => screenTintAmount;
        public float GlitchEdgeWidth => glitchEdgeWidth;
        public float GlitchWarpStrength => glitchWarpStrength;
        public float GlitchTearStrength => glitchTearStrength;
        public float GlitchGlowStrength => glitchGlowStrength;

        public static FullscreenEffectSettings Disabled()
        {
            return new FullscreenEffectSettings
            {
                enabled = false
            };
        }
    }

    [Serializable]
    public sealed class CameraEffectSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private CameraSwaySettings localSway = CameraSwaySettings.Disabled();
        [SerializeField] private CameraKickSettings localCameraKick = CameraKickSettings.Disabled();

        public bool Enabled => enabled;
        public CameraSwaySettings LocalSway => localSway;
        public CameraKickSettings LocalCameraKick => localCameraKick;

        public static CameraEffectSettings LocalPlayerFeedback()
        {
            return new CameraEffectSettings
            {
                enabled = true,
                localSway = CameraSwaySettings.Disabled(),
                localCameraKick = CameraKickSettings.Disabled()
            };
        }

        public static CameraEffectSettings Disabled()
        {
            return new CameraEffectSettings
            {
                enabled = false,
                localSway = CameraSwaySettings.Disabled(),
                localCameraKick = CameraKickSettings.Disabled()
            };
        }
    }

    [Serializable]
    public sealed class AudioEffectSettings
    {
        [SerializeField] private bool enabled;
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [SerializeField] private bool playAtWorldPosition;
        [SerializeField, Min(0f)] private float maxDistance = 5f;

        public bool Enabled => enabled;
        public IReadOnlyList<AudioClip> Clips => clips;
        public float Volume => volume;
        public Vector2 PitchRange => pitchRange;
        public bool PlayAtWorldPosition => playAtWorldPosition;
        public float MaxDistance => maxDistance;
    }

    [CreateAssetMenu(menuName = "GoodCopBadCop/Effects/Effect Preset", fileName = "EffectPreset")]
    public sealed class EffectPreset : ScriptableObject
    {
        [SerializeField] private string key;
        [SerializeField] private string displayName;
        [SerializeField, Min(0f)] private float minInterval = 0.05f;
        [SerializeField] private FullscreenEffectSettings fullscreen = FullscreenEffectSettings.Disabled();
        [SerializeField] private CameraEffectSettings camera = CameraEffectSettings.LocalPlayerFeedback();
        [SerializeField] private AudioEffectSettings audio = new AudioEffectSettings();

        public string Key => key;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? key : displayName;
        public float MinInterval => minInterval;
        public FullscreenEffectSettings Fullscreen => fullscreen;
        public CameraEffectSettings Camera => camera;
        public AudioEffectSettings Audio => audio;
    }
}
