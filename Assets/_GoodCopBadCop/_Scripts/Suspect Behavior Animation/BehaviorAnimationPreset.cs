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
        public float PlaybackSpeed => Mathf.Max(0.01f, playbackSpeed);
        public bool PlayContinuously => playContinuously;
        public float BlendInSeconds => Mathf.Max(0f, blendInSeconds);
        public float BlendOutSeconds => Mathf.Max(0f, blendOutSeconds);
        public float MaxPlaySeconds => Mathf.Max(0f, maxPlaySeconds);
        public int Priority => priority;

        public AnimationClip SelectClip(Object source, int cycleIndex = 0)
        {
            if (clips == null || clips.Length == 0)
                return null;

            if (!randomizeClip || clips.Length == 1)
                return clips[0];

            int seed = source != null ? StableHash($"{source.GetType().FullName}:{source.name}:{cycleIndex}") : cycleIndex;
            int index = (int)((uint)seed % (uint)clips.Length);
            return clips[index];
        }

        public float SelectPauseSeconds(Object source, int cycleIndex)
        {
            float min = Mathf.Max(0f, minPauseSeconds);
            float max = Mathf.Max(min, maxPauseSeconds);
            if (Mathf.Approximately(min, max))
                return min;

            int seed = source != null ? StableHash($"{source.GetType().FullName}:{source.name}:pause:{cycleIndex}") : cycleIndex;
            float t = ((uint)seed % 1000u) / 999f;
            return Mathf.Lerp(min, max, t);
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
