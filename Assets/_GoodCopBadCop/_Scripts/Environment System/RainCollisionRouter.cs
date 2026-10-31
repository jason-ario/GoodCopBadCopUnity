using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    /// <summary>
    /// Routes rain-drop collisions to impact effects based on what was hit.
    /// Ground hits play the ripple (which still births its WaterSpray/ImpactSplash children);
    /// hits on characters (players, suspects, monsters) or steep surfaces play only spray + splash.
    ///
    /// Replaces the rain system's Collision sub-emitter: on enable it turns the rain's
    /// Sub Emitters module off and enables collision messages so OnParticleCollision fires.
    /// Place on the rain ParticleSystem GameObject.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RainCollisionRouter : MonoBehaviour
    {
        [Header("Effects")]
        [Tooltip("Ground-only ripple. Its Birth sub-emitters (spray/splash) fire automatically when it emits.")]
        [SerializeField] private ParticleSystem _ripple;
        [Tooltip("Played directly on non-ground hits.")]
        [SerializeField] private ParticleSystem _waterSpray;
        [Tooltip("Played directly on non-ground hits.")]
        [SerializeField] private ParticleSystem _impactSplash;

        [Header("Ground Detection")]
        [Tooltip("Layers that never count as ground (players, suspects, monsters, body parts).")]
        [SerializeField] private LayerMask _nonGroundLayers;
        [Tooltip("Treat any collider under an Animator or CharacterController as a character (non-ground).")]
        [SerializeField] private bool _treatAnimatedAsCharacter = true;
        [Tooltip("Minimum world-up component of the hit normal to count as ground (0.7 ≈ 45° slope).")]
        [Range(0f, 1f)]
        [SerializeField] private float _minGroundNormalY = 0.7f;

        private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>(16);
        private readonly Dictionary<Component, bool> _characterCache = new Dictionary<Component, bool>();
        private ParticleSystem _rain;

        private void Awake()
        {
            _rain = GetComponent<ParticleSystem>();
        }

        private void OnEnable() => SetRoutingActive(true);

        private void OnDisable()
        {
            SetRoutingActive(false);
            _characterCache.Clear();
        }

        // Active: rain's sub-emitter is off and the ripple only spawns when this script emits it
        // (otherwise it would fire its own looping burst at the emitter's position).
        // Inactive: restores the original sub-emitter setup.
        private void SetRoutingActive(bool active)
        {
            var subEmitters = _rain.subEmitters;
            subEmitters.enabled = !active;

            var collision = _rain.collision;
            collision.sendCollisionMessages = active;

            if (_ripple != null)
            {
                var rippleEmission = _ripple.emission;
                rippleEmission.enabled = !active;
            }
        }

        private void OnParticleCollision(GameObject other)
        {
            int count = _rain.GetCollisionEvents(other, _events);
            if (count == 0) return;

            bool isCharacter = IsCharacter(other, _events[0].colliderComponent);

            for (int i = 0; i < count; i++)
            {
                ParticleCollisionEvent evt = _events[i];
                bool isGround = !isCharacter && evt.normal.y >= _minGroundNormalY;

                if (isGround)
                {
                    EmitRipple(evt.intersection);
                }
                else
                {
                    Emit(_waterSpray, evt.intersection, null);
                    Emit(_impactSplash, evt.intersection, null);
                }
            }
        }

        // The original Collision sub-emitter used "Inherit Size", so the ripple's start size
        // is multiplied by the rain drop's size (e.g. 10 x 0.1 = 1).
        private void EmitRipple(Vector3 position)
        {
            if (_ripple == null) return;

            float rainSize = _rain.main.startSize.Evaluate(0f, Random.value);
            float rippleSize = _ripple.main.startSize.Evaluate(0f, Random.value);
            Emit(_ripple, position, rippleSize * rainSize);
        }

        private bool IsCharacter(GameObject other, Component colliderComponent)
        {
            if ((_nonGroundLayers.value & (1 << other.layer)) != 0) return true;
            if (!_treatAnimatedAsCharacter) return false;

            Component key = colliderComponent != null ? colliderComponent : other.transform;
            if (_characterCache.TryGetValue(key, out bool cached)) return cached;

            bool result = other.GetComponentInParent<Animator>() != null
                          || other.GetComponentInParent<CharacterController>() != null;
            _characterCache[key] = result;
            return result;
        }

        // Mirrors sub-emitter behaviour: a triggered sub-emitter fires its emission bursts.
        private static void Emit(ParticleSystem system, Vector3 position, float? startSize)
        {
            if (system == null) return;

            int count = GetBurstCount(system);
            if (count <= 0) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true
            };
            if (startSize.HasValue) emitParams.startSize = startSize.Value;

            system.Emit(emitParams, count);
        }

        private static int GetBurstCount(ParticleSystem system)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            int burstCount = emission.burstCount;
            if (burstCount == 0) return 1;

            int total = 0;
            for (int i = 0; i < burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                if (burst.probability < 1f && Random.value > burst.probability) continue;
                total += Mathf.Max(0, Mathf.RoundToInt(burst.count.Evaluate(0f, Random.value)));
            }
            return total;
        }
    }
}
