using System;
using System.Collections.Generic;
using GoodCopBadCop.CameraSystem;
using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public enum EFullscreenEffectMode
    {
        OverlaySprite
    }

    [Serializable]
    public sealed class FullscreenEffectSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private EFullscreenEffectMode mode = EFullscreenEffectMode.OverlaySprite;
        [SerializeField] private Sprite overlaySprite;
        [SerializeField] private Color tint = Color.white;
        [SerializeField, Range(0f, 1f)] private float opacity = 0.2f;
        [SerializeField, Min(0f)] private float duration = 0.35f;
        [SerializeField] private AnimationCurve opacityCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        public bool Enabled => enabled;
        public EFullscreenEffectMode Mode => mode;
        public Sprite OverlaySprite => overlaySprite;
        public Color Tint => tint;
        public float Opacity => opacity;
        public float Duration => duration;
        public AnimationCurve OpacityCurve => opacityCurve;

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
