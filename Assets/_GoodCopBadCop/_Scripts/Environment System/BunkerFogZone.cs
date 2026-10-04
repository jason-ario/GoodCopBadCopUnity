using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Overrides <see cref="RenderSettings.fogColor"/> (and optionally fog density) with a fixed
    /// look — e.g. black fog inside the bunker — while the tracked camera is within this
    /// zone's BoxCollider bounds, smoothly blending in on entry and back out to whatever the
    /// current day/night <see cref="EnvironmentPreset"/> reports on exit. The Volumetric Fog And
    /// Mist 2 profile is blended the same way, either toward an explicit override profile or
    /// toward a black-tinted copy of the current preset's profile (<c>darkenVolumetricFog</c>).
    ///
    /// This intentionally does not touch RenderSettings at all while fully outside the zone
    /// (blend == 0), so <see cref="EnvironmentRenderAdapter"/> remains the sole owner of the
    /// day/night fog look whenever the player isn't inside a fog zone.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BunkerFogZone : MonoBehaviour
    {
        [Header("Bunker Fog")]
        [Tooltip("Fog color applied once fully inside this zone (e.g. black for the bunker interior).")]
        [SerializeField] private Color bunkerFogColor = Color.black;

        [Tooltip("Optional fog density override applied once fully inside. Set to a negative value to keep whatever density the current day/night preset already uses.")]
        [SerializeField] private float bunkerFogDensity = -1f;

        [Tooltip("Optional Volumetric Fog And Mist 2 profile applied to the scene's VolumetricFog once fully inside this zone (e.g. a denser/darker interior look). Leave unassigned to keep whatever volumetric profile the current day/night preset already uses.")]
        [SerializeField] private VolumetricFogProfile bunkerVolumetricFogProfile;

        [Tooltip("When no explicit volumetric profile is assigned, tint the current day/night volumetric fog to volumetricFogColor while inside " +
                 "(keeps the preset's density/shape, removes its light scattering so the fog reads as solid color).")]
        [SerializeField] private bool darkenVolumetricFog = true;

        [Tooltip("Volumetric fog color used by darkenVolumetricFog once fully inside this zone.")]
        [SerializeField, ColorUsage(false)] private Color volumetricFogColor = Color.black;

        [Tooltip("Seconds for the fog color/density to blend fully in or out when crossing the zone boundary.")]
        [SerializeField, Min(0.01f)] private float transitionSeconds = 1.5f;

        [Tooltip("The camera to track. Falls back to Camera.main if not assigned.")]
        [SerializeField] private Camera targetCamera;

        private IEnvironmentModel _model;
        private VolumetricFog _volumetricFog;
        private BoxCollider[] _colliders;

        // Scratch instance used to blend between the preset's volumetric profile and bunkerVolumetricFogProfile
        // without mutating either asset. Created lazily the first time it's needed.
        private VolumetricFogProfile _lerpVolumetricProfile;
        private bool _isOverridingVolumetricProfile;

        // Runtime copy of the current preset's volumetric profile, tinted to volumetricFogColor.
        private VolumetricFogProfile _darkVolumetricProfile;
        private VolumetricFogProfile _darkVolumetricSource;

        // 0 = fully outside (day/night preset owns the fog look), 1 = fully inside (bunkerFogColor).
        private float _blend;

        [Inject]
        public void Construct(IEnvironmentModel model, VolumetricFog volumetricFog)
        {
            _model = model;
            _volumetricFog = volumetricFog;
        }

        private static readonly List<BunkerFogZone> s_active = new List<BunkerFogZone>();

        /// <summary>All enabled fog zones. Other systems (e.g. <see cref="IndoorAmbienceAdapter"/>) treat these as interiors.</summary>
        public static IReadOnlyList<BunkerFogZone> Active => s_active;

        /// <summary>The trigger volumes defining this zone (every BoxCollider on this GameObject).</summary>
        public IReadOnlyList<BoxCollider> Zones => _colliders;

        /// <summary>True if <paramref name="point"/> lies inside any enabled collider of this zone.</summary>
        public bool Contains(Vector3 point)
        {
            if (_colliders == null)
                return false;

            for (int i = 0; i < _colliders.Length; i++)
            {
                BoxCollider col = _colliders[i];
                if (col != null && col.enabled && col.bounds.Contains(point))
                    return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_active.Clear();

        private void Awake()
        {
            _colliders = GetComponents<BoxCollider>();
        }

        private void OnEnable()
        {
            if (!s_active.Contains(this))
                s_active.Add(this);
        }

        private void OnDisable()
        {
            s_active.Remove(this);
        }

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Update()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                return;
            }

            bool isInside = Contains(targetCamera.transform.position);
            float target = isInside ? 1f : 0f;
            float step = Time.deltaTime / transitionSeconds;
            _blend = Mathf.MoveTowards(_blend, target, step);

            ApplyBlendedFog();
        }

        private void ApplyBlendedFog()
        {
            EnvironmentPreset preset = _model != null ? _model.CurrentPreset.CurrentValue : null;

            // Fully outside and settled — leave RenderSettings/VolumetricFog entirely to
            // EnvironmentRenderAdapter so day/night preset switches keep working normally.
            if (_blend <= 0f)
            {
                RestoreVolumetricProfile(preset);
                return;
            }

            Color baseColor = RenderSettings.fogColor;
            float baseDensity = RenderSettings.fogDensity;

            if (preset != null)
            {
                baseColor = preset.fogColor;
                baseDensity = preset.fogDensity;
            }

            RenderSettings.fogColor = Color.Lerp(baseColor, bunkerFogColor, _blend);

            if (bunkerFogDensity >= 0f)
                RenderSettings.fogDensity = Mathf.Lerp(baseDensity, bunkerFogDensity, _blend);

            ApplyBlendedVolumetricFog(preset);
        }

        private void ApplyBlendedVolumetricFog(EnvironmentPreset preset)
        {
            if (_volumetricFog == null)
                return;

            VolumetricFogProfile baseProfile = preset != null ? preset.volumetricFogProfile : _volumetricFog.profile;
            if (baseProfile == null || baseProfile == _lerpVolumetricProfile)
                return;

            VolumetricFogProfile targetProfile = ResolveTargetVolumetricProfile(baseProfile);
            if (targetProfile == null)
                return;

            if (_lerpVolumetricProfile == null)
                _lerpVolumetricProfile = ScriptableObject.CreateInstance<VolumetricFogProfile>();

            _lerpVolumetricProfile.Lerp(baseProfile, targetProfile, _blend);

            _volumetricFog.profile = _lerpVolumetricProfile;
            _volumetricFog.UpdateMaterialPropertiesNow();
            _isOverridingVolumetricProfile = true;
        }

        private VolumetricFogProfile ResolveTargetVolumetricProfile(VolumetricFogProfile baseProfile)
        {
            if (bunkerVolumetricFogProfile != null)
                return bunkerVolumetricFogProfile;

            if (!darkenVolumetricFog)
                return null;

            // Rebuild the tinted copy whenever the day/night preset swaps its volumetric profile.
            if (_darkVolumetricProfile == null || _darkVolumetricSource != baseProfile)
            {
                if (_darkVolumetricProfile != null)
                    Destroy(_darkVolumetricProfile);

                _darkVolumetricProfile = Instantiate(baseProfile);
                _darkVolumetricProfile.hideFlags = HideFlags.DontSave;
                TintVolumetricProfile(_darkVolumetricProfile, volumetricFogColor);
                _darkVolumetricSource = baseProfile;
            }

            return _darkVolumetricProfile;
        }

        private static void TintVolumetricProfile(VolumetricFogProfile profile, Color color)
        {
            profile.albedo = color;
            profile.distantFogColor = color;

            // Strip every additive light contribution so the fog renders as the flat tint color.
            profile.ambientLightMultiplier = 0f;
            profile.specularIntensity = 0f;
            profile.specularColor = Color.black;
            profile.lightDiffusionIntensity = 0f;
            profile.distantFogDiffusionIntensity = 0f;

            TintGradient(profile.depthGradient, color);
            TintGradient(profile.heightGradient, color);
        }

        private static void TintGradient(Gradient gradient, Color color)
        {
            if (gradient == null)
                return;

            // Keep the key layout (so profile Lerp key-count limits are unchanged) and alpha; only recolor.
            GradientColorKey[] keys = gradient.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                keys[i].color = color;
            gradient.SetKeys(keys, gradient.alphaKeys);
        }

        private void RestoreVolumetricProfile(EnvironmentPreset preset)
        {
            if (!_isOverridingVolumetricProfile)
                return;

            if (_volumetricFog != null && preset != null)
            {
                _volumetricFog.profile = preset.volumetricFogProfile;
                _volumetricFog.UpdateMaterialPropertiesNow();
            }

            _isOverridingVolumetricProfile = false;
        }

        private void OnDestroy()
        {
            if (_isOverridingVolumetricProfile)
                RestoreVolumetricProfile(_model != null ? _model.CurrentPreset.CurrentValue : null);

            if (_lerpVolumetricProfile != null)
                Destroy(_lerpVolumetricProfile);

            if (_darkVolumetricProfile != null)
                Destroy(_darkVolumetricProfile);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            foreach (BoxCollider col in GetComponents<BoxCollider>())
            {
                Gizmos.color = new Color(0f, 0f, 0f, 0.25f);
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(col.center),
                    transform.rotation,
                    transform.lossyScale
                );
                Gizmos.DrawCube(Vector3.zero, col.size);

                Gizmos.color = new Color(0f, 0f, 0f, 0.85f);
                Gizmos.DrawWireCube(Vector3.zero, col.size);
            }
        }
#endif
    }
}
