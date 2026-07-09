using System;
using Unity.Cinemachine;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    public enum ECameraImpulseMode
    {
        DefaultVelocity,
        Force,
        Velocity
    }

    public enum ECameraImpulseShape
    {
        Recoil,
        Bump,
        Explosion,
        Rumble
    }

    public enum ECameraSwayMotion
    {
        HeadSway,
        CigaretteDrag,
        HealRush
    }

    [Serializable]
    public sealed class CameraImpulseSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private ECameraImpulseMode mode = ECameraImpulseMode.DefaultVelocity;
        [SerializeField] private float force = 1f;
        [SerializeField] private Vector3 velocity = Vector3.down;
        [SerializeField] private ECameraImpulseShape shape = ECameraImpulseShape.Bump;
        [SerializeField, Min(0.01f)] private float impulseDuration = 0.2f;
        [SerializeField, Min(0f)] private float attackTime;
        [SerializeField, Min(0f)] private float sustainTime = 0.2f;
        [SerializeField, Min(0f)] private float decayTime = 0.7f;
        [SerializeField] private bool scaleEnvelopeWithImpact = true;
        [SerializeField, Min(0f)] private float amplitudeGain = 1f;
        [SerializeField, Min(0f)] private float frequencyGain = 1f;

        public bool Enabled => enabled;
        public ECameraImpulseMode Mode => mode;
        public float Force => force;
        public Vector3 Velocity => velocity;
        public ECameraImpulseShape Shape => shape;
        public float ImpulseDuration => impulseDuration;
        public float AttackTime => attackTime;
        public float SustainTime => sustainTime;
        public float DecayTime => decayTime;
        public bool ScaleEnvelopeWithImpact => scaleEnvelopeWithImpact;
        public float AmplitudeGain => amplitudeGain;
        public float FrequencyGain => frequencyGain;

        public CinemachineImpulseDefinition.ImpulseShapes CinemachineShape => shape switch
        {
            ECameraImpulseShape.Recoil => CinemachineImpulseDefinition.ImpulseShapes.Recoil,
            ECameraImpulseShape.Explosion => CinemachineImpulseDefinition.ImpulseShapes.Explosion,
            ECameraImpulseShape.Rumble => CinemachineImpulseDefinition.ImpulseShapes.Rumble,
            _ => CinemachineImpulseDefinition.ImpulseShapes.Bump
        };

        public static CameraImpulseSettings DefaultHit()
        {
            return new CameraImpulseSettings
            {
                enabled = true,
                mode = ECameraImpulseMode.DefaultVelocity,
                force = 1f,
                velocity = Vector3.down,
                shape = ECameraImpulseShape.Bump,
                impulseDuration = 0.2f,
                attackTime = 0f,
                sustainTime = 0.2f,
                decayTime = 0.7f,
                scaleEnvelopeWithImpact = true,
                amplitudeGain = 1f,
                frequencyGain = 1f
            };
        }

        public static CameraImpulseSettings WithForce(float force)
        {
            return new CameraImpulseSettings
            {
                enabled = true,
                mode = ECameraImpulseMode.Force,
                force = force,
                velocity = Vector3.down
            };
        }

        public static CameraImpulseSettings WithVelocity(Vector3 velocity)
        {
            return new CameraImpulseSettings
            {
                enabled = true,
                mode = ECameraImpulseMode.Velocity,
                force = velocity.magnitude,
                velocity = velocity
            };
        }

        public static CameraImpulseSettings Disabled()
        {
            return new CameraImpulseSettings
            {
                enabled = false
            };
        }
    }

    [Serializable]
    public sealed class CameraSwaySettings
    {
        [SerializeField] private bool enabled;
        [SerializeField] private ECameraSwayMotion motion = ECameraSwayMotion.HeadSway;
        [SerializeField, Min(0.01f)] private float duration = 0.8f;
        [SerializeField, Min(0.1f)] private float cycles = 1.5f;
        [SerializeField] private Vector3 eulerAmplitude = Vector3.zero;
        [SerializeField] private float fieldOfViewOffset;
        [SerializeField] private AnimationCurve envelope = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f));

        public bool Enabled => enabled;
        public ECameraSwayMotion Motion => motion;
        public float Duration => duration;
        public float Cycles => cycles;
        public Vector3 EulerAmplitude => eulerAmplitude;
        public float FieldOfViewOffset => fieldOfViewOffset;
        public AnimationCurve Envelope => envelope;

        public static CameraSwaySettings Disabled()
        {
            return new CameraSwaySettings
            {
                enabled = false
            };
        }
    }
}
