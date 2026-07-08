using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public readonly struct EffectContext
    {
        public readonly GameObject Source;
        public readonly Vector3? WorldPosition;
        public readonly float Intensity;

        public EffectContext(GameObject source = null, Vector3? worldPosition = null, float intensity = 1f)
        {
            Source = source;
            WorldPosition = worldPosition;
            Intensity = intensity;
        }

        public static EffectContext Default => new EffectContext();
    }
}
