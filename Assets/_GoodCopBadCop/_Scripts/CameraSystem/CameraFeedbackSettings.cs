using System;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    public enum ECameraSwayMotion
    {
        HeadSway,
        CigaretteDrag,
        HealRush
    }

    [Serializable]
    public sealed class CameraKickSettings
    {
        [SerializeField] private bool enabled;
        [SerializeField, Min(0.01f)] private float duration = 0.2f;
        [SerializeField] private Vector3 eulerKick = Vector3.zero;
        [SerializeField] private float fieldOfViewKick;

        public bool Enabled => enabled;
        public float Duration => duration;
        public Vector3 EulerKick => eulerKick;
        public float FieldOfViewKick => fieldOfViewKick;

        public static CameraKickSettings Disabled()
        {
            return new CameraKickSettings
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
        [SerializeField] private Vector3 eulerAmplitude = Vector3.zero;
        [SerializeField] private float fieldOfViewOffset;

        public bool Enabled => enabled;
        public ECameraSwayMotion Motion => motion;
        public float Duration => duration;
        public Vector3 EulerAmplitude => eulerAmplitude;
        public float FieldOfViewOffset => fieldOfViewOffset;

        public static CameraSwaySettings Disabled()
        {
            return new CameraSwaySettings
            {
                enabled = false
            };
        }
    }
}
