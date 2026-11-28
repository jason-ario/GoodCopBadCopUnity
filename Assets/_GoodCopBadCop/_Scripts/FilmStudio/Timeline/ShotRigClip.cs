using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>One shot's camera animation. Data is read by <see cref="ShotRigMixer"/>.</summary>
    [System.Serializable]
    public class ShotRigClip : PlayableAsset, ITimelineClipAsset
    {
        public Vector3 fromOffset;
        public Vector3 toOffset;
        public float orbitDegrees;
        public float fromFov = 40f;
        public float toFov = 40f;
        public float dutch;
        public float whipPanDegrees;
        public float whipPanStart = 0.85f;
        public float fromZoomScale = 1f;
        public float toZoomScale = 1f;
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<StudioEmptyBehaviour>.Create(graph);
        }
    }
}
