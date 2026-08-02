using UnityEngine;
using VContainer;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Overrides <see cref="RenderSettings.fogColor"/> (and optionally fog density) with a fixed
    /// look — e.g. black fog inside the bunker — while the tracked camera is within this
    /// zone's BoxCollider bounds, smoothly blending in on entry and back out to whatever the
    /// current day/night <see cref="EnvironmentPreset"/> reports on exit.
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

        [Tooltip("Seconds for the fog color/density to blend fully in or out when crossing the zone boundary.")]
        [SerializeField, Min(0.01f)] private float transitionSeconds = 1.5f;

        [Tooltip("The camera to track. Falls back to Camera.main if not assigned.")]
        [SerializeField] private Camera targetCamera;

        private IEnvironmentModel _model;
        private BoxCollider _collider;

        // 0 = fully outside (day/night preset owns the fog look), 1 = fully inside (bunkerFogColor).
        private float _blend;

        [Inject]
        public void Construct(IEnvironmentModel model)
        {
            _model = model;
        }

        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();
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

            bool isInside = _collider.bounds.Contains(targetCamera.transform.position);
            float target = isInside ? 1f : 0f;
            float step = Time.deltaTime / transitionSeconds;
            _blend = Mathf.MoveTowards(_blend, target, step);

            ApplyBlendedFog();
        }

        private void ApplyBlendedFog()
        {
            // Fully outside and settled — leave RenderSettings entirely to EnvironmentRenderAdapter
            // so day/night preset switches keep working normally.
            if (_blend <= 0f)
                return;

            Color baseColor = RenderSettings.fogColor;
            float baseDensity = RenderSettings.fogDensity;

            EnvironmentPreset preset = _model != null ? _model.CurrentPreset.CurrentValue : null;
            if (preset != null)
            {
                baseColor = preset.fogColor;
                baseDensity = preset.fogDensity;
            }

            RenderSettings.fogColor = Color.Lerp(baseColor, bunkerFogColor, _blend);

            if (bunkerFogDensity >= 0f)
                RenderSettings.fogDensity = Mathf.Lerp(baseDensity, bunkerFogDensity, _blend);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col == null) return;

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
#endif
    }
}
