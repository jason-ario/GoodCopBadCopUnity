using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Target Time.timeScale while active (Play Mode only). Supports blending.</summary>
    [System.Serializable]
    public class TimeScaleClip : PlayableAsset, ITimelineClipAsset
    {
        public float timeScale = 1f;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<TimeScaleBehaviour> playable = ScriptPlayable<TimeScaleBehaviour>.Create(graph);
            playable.GetBehaviour().timeScale = timeScale;
            return playable;
        }
    }

    public class TimeScaleBehaviour : PlayableBehaviour
    {
        public float timeScale = 1f;
    }
}
