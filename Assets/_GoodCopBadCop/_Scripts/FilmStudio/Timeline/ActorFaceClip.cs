using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Turns the actor to a yaw in place. Data is read by <see cref="ActorMoveMixer"/>.</summary>
    [System.Serializable]
    public class ActorFaceClip : PlayableAsset, ITimelineClipAsset
    {
        public float toYaw;
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<StudioEmptyBehaviour>.Create(graph);
        }
    }
}
