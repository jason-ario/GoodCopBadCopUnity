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
        [SerializeField] private CameraImpulseSettings localImpulse = CameraImpulseSettings.DefaultHit();

        public bool Enabled => enabled;
        public CameraImpulseSettings LocalImpulse => localImpulse;

        public static CameraEffectSettings LocalPlayerShake()
        {
            return new CameraEffectSettings
            {
                enabled = true,
                localImpulse = CameraImpulseSettings.DefaultHit()
            };
        }

        public static CameraEffectSettings LocalPlayerShake(float force)
        {
            return new CameraEffectSettings
            {
                enabled = true,
                localImpulse = CameraImpulseSettings.WithForce(force)
            };
        }

        public static CameraEffectSettings LocalPlayerImpulse(CameraImpulseSettings impulse)
        {
            return new CameraEffectSettings
            {
                enabled = impulse != null && impulse.Enabled,
                localImpulse = impulse ?? CameraImpulseSettings.Disabled()
            };
        }

        public static CameraEffectSettings Disabled()
        {
            return new CameraEffectSettings
            {
                enabled = false,
                localImpulse = CameraImpulseSettings.Disabled()
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
        [SerializeField] private CameraEffectSettings camera = CameraEffectSettings.LocalPlayerShake();
        [SerializeField] private AudioEffectSettings audio = new AudioEffectSettings();

        public string Key => key;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? key : displayName;
        public float MinInterval => minInterval;
        public FullscreenEffectSettings Fullscreen => fullscreen;
        public CameraEffectSettings Camera => camera;
        public AudioEffectSettings Audio => audio;

    }
}
