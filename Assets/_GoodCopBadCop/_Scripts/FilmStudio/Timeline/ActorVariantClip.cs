using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Sets the actor's variant at the clip start; the value holds after the clip ends.</summary>
    [System.Serializable]
    public class ActorVariantClip : PlayableAsset, ITimelineClipAsset
    {
        public bool mutated = true;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<StudioEmptyBehaviour>.Create(graph);
        }
    }
}
