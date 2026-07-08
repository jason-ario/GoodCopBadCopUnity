using System;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    public enum ECameraImpulseMode
    {
        DefaultVelocity,
        Force,
        Velocity
    }

    [Serializable]
    public sealed class CameraImpulseSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private ECameraImpulseMode mode = ECameraImpulseMode.DefaultVelocity;
        [SerializeField] private float force = 1f;
        [SerializeField] private Vector3 velocity = Vector3.down;

        public bool Enabled => enabled;
        public ECameraImpulseMode Mode => mode;
        public float Force => force;
        public Vector3 Velocity => velocity;

        public static CameraImpulseSettings DefaultHit()
        {
            return new CameraImpulseSettings
            {
                enabled = true,
                mode = ECameraImpulseMode.DefaultVelocity,
                force = 1f,
                velocity = Vector3.down
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
}
