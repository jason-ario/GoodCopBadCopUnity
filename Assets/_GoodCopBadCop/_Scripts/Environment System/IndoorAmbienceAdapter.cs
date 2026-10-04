using System;
using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Crossfades from outdoor ambience to an indoor ambience loop while the local camera is
    /// inside any of the assigned interior zones (e.g. the bunker).
    ///
    /// Outdoor sources are not tweened directly. Instead, an "outdoor gain" multiplier is
    /// applied in LateUpdate on top of whatever volume other systems (AudioManager,
    /// UnderwaterAmbienceAdapter, rain fades) set that frame, so those systems keep full
    /// ownership of their sources and nothing fights over AudioSource.volume.
    ///
    /// The indoor source is owned entirely by this component. It only plays while
    /// <see cref="ambienceGateSource"/> is playing, so it respects the game turning the
    /// ambience off (menus, shift reports, etc.).
    /// </summary>
    public sealed class IndoorAmbienceAdapter : MonoBehaviour
    {
        [Header("Zones")]
        [Tooltip("Trigger colliders covering the interior. The camera counts as indoors while inside any of them.")]
        [SerializeField] private Collider[] interiorZones = Array.Empty<Collider>();

        [Tooltip("Also treat every enabled BunkerFogZone (bunker, power plant, ...) as an interior zone.")]
        [SerializeField] private bool includeFogZones = true;

        [Tooltip("The camera to track. Falls back to Camera.main if not assigned.")]
        [SerializeField] private Camera targetCamera;

        [Header("Sources")]
        [Tooltip("Outdoor ambience sources faded out while indoors (e.g. wasteland, rain).")]
        [SerializeField] private AudioSource[] outdoorSources = Array.Empty<AudioSource>();

        [Tooltip("Indoor ambience loop faded in while indoors.")]
        [SerializeField] private AudioSource indoorSource;

        [Tooltip("Indoor ambience only plays while this source is playing (usually the main outdoor ambience). " +
                 "Leave empty to always allow indoor ambience.")]
        [SerializeField] private AudioSource ambienceGateSource;

        [Header("Fade")]
        [Tooltip("Seconds for a full crossfade when crossing the interior boundary.")]
        [SerializeField, Min(0.01f)] private float fadeSeconds = 2f;

        [Tooltip("Outdoor volume multiplier while fully indoors (0 = silent, >0 = muffled bleed-through).")]
        [SerializeField, Range(0f, 1f)] private float outdoorGainIndoors;

        private float _indoorVolume;
        private float _blend; // 0 = fully outdoors, 1 = fully indoors.
        private float _indoorGate; // 0..1, follows ambienceGateSource playback.

        private float[] _baseVolumes;
        private float[] _lastWritten;

        /// <summary>Eased 0..1 blend of the local camera being indoors (0 = outdoors, 1 = fully inside).</summary>
        public float IndoorBlend => Mathf.SmoothStep(0f, 1f, _blend);

        private void Awake()
        {
            _indoorVolume = indoorSource != null ? indoorSource.volume : 1f;

            _baseVolumes = new float[outdoorSources.Length];
            _lastWritten = new float[outdoorSources.Length];
            for (int i = 0; i < outdoorSources.Length; i++)
            {
                float v = outdoorSources[i] != null ? outdoorSources[i].volume : 1f;
                _baseVolumes[i] = v;
                _lastWritten[i] = v;
            }

            if (indoorSource != null)
            {
                indoorSource.playOnAwake = false;
                indoorSource.loop = true;
                indoorSource.volume = 0f;
                indoorSource.Stop();
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            bool isInside = targetCamera != null && IsInsideAnyZone(targetCamera.transform.position);
            float step = Time.deltaTime / fadeSeconds;

            _blend = Mathf.MoveTowards(_blend, isInside ? 1f : 0f, step);

            bool gateOpen = ambienceGateSource == null || ambienceGateSource.isPlaying;
            _indoorGate = Mathf.MoveTowards(_indoorGate, gateOpen ? 1f : 0f, step);

            float eased = Mathf.SmoothStep(0f, 1f, _blend);
            ApplyOutdoorGain(Mathf.Lerp(1f, outdoorGainIndoors, eased));
            ApplyIndoorVolume(eased * _indoorGate);
        }

        private bool IsInsideAnyZone(Vector3 point)
        {
            for (int i = 0; i < interiorZones.Length; i++)
            {
                if (IsInsideZone(interiorZones[i], point))
                    return true;
            }

            if (includeFogZones)
            {
                var fogZones = BunkerFogZone.Active;
                for (int i = 0; i < fogZones.Count; i++)
                {
                    if (fogZones[i] != null && fogZones[i].isActiveAndEnabled && fogZones[i].Contains(point))
                        return true;
                }
            }

            return false;
        }

        private static bool IsInsideZone(Collider zone, Vector3 point)
        {
            if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy)
                return false;

            // ClosestPoint returns the point itself when it's inside the collider (handles rotated boxes).
            return (zone.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
        }

        private void ApplyOutdoorGain(float gain)
        {
            for (int i = 0; i < outdoorSources.Length; i++)
            {
                AudioSource source = outdoorSources[i];
                if (source == null)
                    continue;

                // If another system changed the volume since our last write, adopt it as the new base.
                float current = source.volume;
                if (!Mathf.Approximately(current, _lastWritten[i]))
                    _baseVolumes[i] = current;

                float scaled = _baseVolumes[i] * gain;
                source.volume = scaled;
                _lastWritten[i] = scaled;
            }
        }

        private void ApplyIndoorVolume(float weight)
        {
            if (indoorSource == null)
                return;

            if (weight > 0f)
            {
                if (!indoorSource.gameObject.activeSelf)
                    indoorSource.gameObject.SetActive(true);
                if (!indoorSource.isPlaying)
                    indoorSource.Play();
                indoorSource.volume = _indoorVolume * weight;
            }
            else if (indoorSource.isPlaying)
            {
                indoorSource.volume = 0f;
                indoorSource.Stop();
            }
        }

        private void OnDisable()
        {
            // Hand outdoor sources back at their un-scaled volume.
            if (_baseVolumes == null)
                return;

            for (int i = 0; i < outdoorSources.Length; i++)
            {
                if (outdoorSources[i] != null && Mathf.Approximately(outdoorSources[i].volume, _lastWritten[i]))
                    outdoorSources[i].volume = _baseVolumes[i];
            }

            if (indoorSource != null)
            {
                indoorSource.volume = 0f;
                indoorSource.Stop();
            }

            _blend = 0f;
        }
    }
}
