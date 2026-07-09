using UnityEngine;

namespace GoodCopBadCop.SuspectBehaviorAnimation
{
    [CreateAssetMenu(
        fileName = "Behavior Animation Preset",
        menuName = "GoodCopBadCop/Anomalies/Behavior Animation Preset")]
    public sealed class BehaviorAnimationPreset : ScriptableObject
    {
        [SerializeField] private AnimationClip[] clips;
        [SerializeField] private bool randomizeClip;
        [SerializeField] private bool useFirstClipAsContinuousBase;
        [SerializeField] private float playbackSpeed = 1f;
        [SerializeField] private bool playContinuously = true;
        [SerializeField] private float blendInSeconds = 0.18f;
        [SerializeField] private float blendOutSeconds = 0.18f;
        [SerializeField] private float maxPlaySeconds;
        [SerializeField] private float minPauseSeconds = 2f;
        [SerializeField] private float maxPauseSeconds = 5f;
        [SerializeField] private int priority;

        public AnimationClip[] Clips => clips;
        public bool RandomizeClip => randomizeClip;
        public bool UseFirstClipAsContinuousBase => useFirstClipAsContinuousBase;
        public float PlaybackSpeed => Mathf.Max(0.01f, playbackSpeed);
        public bool PlayContinuously => playContinuously;
        public float BlendInSeconds => Mathf.Max(0f, blendInSeconds);
        public float BlendOutSeconds => Mathf.Max(0f, blendOutSeconds);
        public float MaxPlaySeconds => Mathf.Max(0f, maxPlaySeconds);
        public int Priority => priority;

        public AnimationClip SelectClip(int sequenceSeed, int cycleIndex = 0, AnimationClip previousClip = null)
        {
            if (clips == null || clips.Length == 0)
                return null;

            if (clips.Length == 1)
                return clips[0];

            if (!randomizeClip)
                return clips[PositiveModulo(cycleIndex, clips.Length)];

            int state = Mix(sequenceSeed, cycleIndex);
            int index = NextRange(ref state, clips.Length);

            if (previousClip != null && clips[index] == previousClip)
                index = (index + 1 + NextRange(ref state, clips.Length - 1)) % clips.Length;

            return clips[index];
        }

        public AnimationClip SelectBaseClip()
        {
            return clips != null && clips.Length > 0 ? clips[0] : null;
        }

        public AnimationClip SelectOverrideClip(int sequenceSeed, int cycleIndex = 0, AnimationClip previousClip = null)
        {
            if (clips == null || clips.Length <= 1)
                return null;

            int overrideCount = clips.Length - 1;
            if (overrideCount == 1)
                return clips[1];

            int state = Mix(sequenceSeed, cycleIndex);
            int index = 1 + NextRange(ref state, overrideCount);

            if (previousClip != null && clips[index] == previousClip)
                index = 1 + PositiveModulo(index - 1 + 1 + NextRange(ref state, overrideCount - 1), overrideCount);

            return clips[index];
        }

        public AnimationClip SelectClip(Object source, int cycleIndex = 0, AnimationClip previousClip = null)
        {
            int seed = source != null ? StableHash($"{source.GetType().FullName}:{source.name}") : 0;
            return SelectClip(seed, cycleIndex, previousClip);
        }

        public float SelectPauseSeconds(int sequenceSeed, int cycleIndex)
        {
            float min = Mathf.Max(0f, minPauseSeconds);
            float max = Mathf.Max(min, maxPauseSeconds);
            if (Mathf.Approximately(min, max))
                return min;

            int seed = Mix(sequenceSeed, cycleIndex);
            float t = ((uint)seed % 1000u) / 999f;
            return Mathf.Lerp(min, max, t);
        }

        public float SelectPauseSeconds(Object source, int cycleIndex)
        {
            int seed = source != null ? StableHash($"{source.GetType().FullName}:{source.name}:pause") : 0;
            return SelectPauseSeconds(seed, cycleIndex);
        }

        private static int NextRange(ref int state, int exclusiveMax)
        {
            if (exclusiveMax <= 1)
                return 0;

            state = Mix(state, unchecked((int)0x9E3779B9));
            return (int)((uint)state % (uint)exclusiveMax);
        }

        private static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                int value = seed + salt * 0x6D2B79F5;
                value ^= value >> 15;
                value *= unchecked((int)0x85EBCA6B);
                value ^= value >> 13;
                value *= unchecked((int)0xC2B2AE35);
                value ^= value >> 16;
                return value;
            }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];

                return hash;
            }
        }
    }
}
