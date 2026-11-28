using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Walks the actor to a position. Data is read by <see cref="ActorMoveMixer"/>.</summary>
    [System.Serializable]
    public class ActorMoveClip : PlayableAsset, ITimelineClipAsset
    {
        public Vector3 toPosition;
        public bool faceMoveDirection = true;
        public float endYaw;
        public bool useEndYaw;
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<StudioEmptyBehaviour>.Create(graph);
        }
    }

    /// <summary>Data-less clip behaviour; Film Studio mixers read clip assets directly.</summary>
    public class StudioEmptyBehaviour : PlayableBehaviour { }
}
