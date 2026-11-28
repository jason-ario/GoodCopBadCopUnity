using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    // ── Track ─────────────────────────────────────────────────────────────────────
    /// <summary>Drives Time.timeScale (slow-mo) during Play Mode only. No binding.</summary>
    [TrackColor(0.3f, 0.55f, 0.95f)]
    [TrackClipType(typeof(TimeScaleClip))]
    public class TimeScaleTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<TimeScaleMixer>.Create(graph, inputCount);
        }
    }

    // ── Mixer ─────────────────────────────────────────────────────────────────────
    public class TimeScaleMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying) return;

            float weighted = 0f;
            float totalWeight = 0f;
            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f) continue;

                Playable input = playable.GetInput(i);
                if (!input.IsValid() || input.GetPlayableType() != typeof(TimeScaleBehaviour)) continue;

                TimeScaleBehaviour behaviour = ((ScriptPlayable<TimeScaleBehaviour>)input).GetBehaviour();
                weighted += behaviour.timeScale * weight;
                totalWeight += weight;
            }

            float scale = weighted + Mathf.Max(0f, 1f - totalWeight);
            Time.timeScale = Mathf.Max(0f, scale);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (Application.isPlaying) Time.timeScale = 1f;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (Application.isPlaying) Time.timeScale = 1f;
        }
    }
}
