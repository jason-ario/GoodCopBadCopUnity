using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Applies a URP Volume effect when the player camera is inside this zone's BoxCollider bounds.
    /// Broadcasts <see cref="OnUnderwaterStateChanged"/> so other systems (movement, ragdoll) can react.
    /// Also applies an <see cref="AudioLowPassFilter"/> to the scene AudioListener for muffled audio.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class UnderwaterZone : MonoBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Speed at which the Volume weight transitions when entering or exiting the zone.")]
        [SerializeField] private float transitionSpeed = 2f;

        [Tooltip("The camera to track. Falls back to Camera.main if not assigned.")]
        [SerializeField] private Camera targetCamera;

        [Header("Splash")]
        [Tooltip("Particle prefab to spawn at the water surface when the player enters or exits the zone.")]
        [SerializeField] private ParticleSystem splashEffectPrefab;
        [Tooltip("Audio clip to play at the water surface on entry and exit.")]
        [SerializeField] private AudioClip splashSoundClip;
        [Tooltip("Volume of the splash sound.")]
        [SerializeField] [Range(0f, 1f)] private float splashVolume = 1f;

        [Header("Audio")]
        [Tooltip("Normal (dry) AudioLowPassFilter cutoff in Hz. 22000 = full range.")]
        [SerializeField] private float normalCutoffHz = 22000f;
        [Tooltip("Underwater AudioLowPassFilter cutoff in Hz. Lower = more muffled.")]
        [SerializeField] private float underwaterCutoffHz = 800f;
        [Tooltip("Speed at which the audio filter cutoff transitions.")]
        [SerializeField] private float audioTransitionSpeed = 3f;

        /// <summary>Fired whenever the camera crosses into or out of the underwater zone.</summary>
        public static event Action<bool> OnUnderwaterStateChanged;

        /// <summary>
        /// Fired whenever the player's root body crosses into or out of the underwater zone.
        /// Used by movement and ragdoll systems so physics reacts to body position, not camera position.
        /// </summary>
        public static event Action<bool> OnPlayerBodyUnderwaterStateChanged;

        private static Transform _playerBodyTransform;

        /// <summary>
        /// Register the local player's root transform so all zones track body position independently.
        /// Call from <c>PlayerMovementController.OnNetworkSpawn</c> for the local player only.
        /// </summary>
        public static void RegisterPlayerBody(Transform t) => _playerBodyTransform = t;

        /// <summary>Unregister the body transform on despawn.</summary>
        public static void UnregisterPlayerBody(Transform t)
        {
            if (_playerBodyTransform == t)
                _playerBodyTransform = null;
        }

        private Volume _volume;
        private BoxCollider _collider;
        private AudioLowPassFilter _lowPassFilter;
        private bool _isUnderwater;
        private bool _isPlayerBodyUnderwater;

        private void Awake()
        {
            _volume = GetComponent<Volume>();
            _collider = GetComponent<BoxCollider>();
            _volume.isGlobal = false;
            _volume.weight = 0f;
        }

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            TryInitAudioFilter();
        }

        private void Update()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                return;
            }

            bool inside = _collider.bounds.Contains(targetCamera.transform.position);

            // Transition volume weight
            float targetWeight = inside ? 1f : 0f;
            _volume.weight = Mathf.MoveTowards(_volume.weight, targetWeight, transitionSpeed * Time.deltaTime);

            // Broadcast camera state change (drives visuals + audio)
            if (inside != _isUnderwater)
            {
                _isUnderwater = inside;
                OnUnderwaterStateChanged?.Invoke(_isUnderwater);
            }

            // Broadcast body state change (drives movement + ragdoll)
            if (_playerBodyTransform != null)
            {
                bool bodyInside = _collider.bounds.Contains(_playerBodyTransform.position);
                if (bodyInside != _isPlayerBodyUnderwater)
                {
                    _isPlayerBodyUnderwater = bodyInside;
                    OnPlayerBodyUnderwaterStateChanged?.Invoke(_isPlayerBodyUnderwater);
                    SpawnSplash(_playerBodyTransform.position);
                }
            }

            // Transition audio filter
            if (_lowPassFilter == null)
                TryInitAudioFilter();

            if (_lowPassFilter != null)
            {
                float targetCutoff = _isUnderwater ? underwaterCutoffHz : normalCutoffHz;
                float step = (normalCutoffHz - underwaterCutoffHz) * audioTransitionSpeed * Time.deltaTime;
                _lowPassFilter.cutoffFrequency = Mathf.MoveTowards(_lowPassFilter.cutoffFrequency, targetCutoff, step);
            }
        }

        private void OnDestroy()
        {
            if (_isUnderwater)
            {
                _isUnderwater = false;
                OnUnderwaterStateChanged?.Invoke(false);
            }

            if (_isPlayerBodyUnderwater)
            {
                _isPlayerBodyUnderwater = false;
                OnPlayerBodyUnderwaterStateChanged?.Invoke(false);
            }

            if (_lowPassFilter != null)
                Destroy(_lowPassFilter);
        }

        private void SpawnSplash(Vector3 bodyPosition)
        {
            float surfaceY = _collider.bounds.max.y;
            Vector3 splashPos = new Vector3(bodyPosition.x, surfaceY, bodyPosition.z);

            if (splashEffectPrefab != null)
            {
                ParticleSystem ps = Instantiate(splashEffectPrefab, splashPos, Quaternion.identity);
                float autoDestroyDelay = ps.main.duration + ps.main.startLifetime.constantMax;
                Destroy(ps.gameObject, autoDestroyDelay);
            }

            if (splashSoundClip != null)
                AudioSource.PlayClipAtPoint(splashSoundClip, splashPos, splashVolume);
        }

        private void TryInitAudioFilter()
        {
            AudioListener listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return;

            _lowPassFilter = listener.GetComponent<AudioLowPassFilter>();
            if (_lowPassFilter == null)
                _lowPassFilter = listener.gameObject.AddComponent<AudioLowPassFilter>();

            _lowPassFilter.cutoffFrequency = normalCutoffHz;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col == null) return;

            Gizmos.color = new Color(0f, 0.5f, 1f, 0.2f);
            Gizmos.matrix = Matrix4x4.TRS(
                transform.TransformPoint(col.center),
                transform.rotation,
                transform.lossyScale
            );
            Gizmos.DrawCube(Vector3.zero, col.size);

            Gizmos.color = new Color(0f, 0.5f, 1f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, col.size);
        }
#endif
    }
}
