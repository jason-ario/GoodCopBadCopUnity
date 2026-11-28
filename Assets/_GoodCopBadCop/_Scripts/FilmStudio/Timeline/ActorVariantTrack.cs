using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    // ── Track ─────────────────────────────────────────────────────────────────────
    /// <summary>Switches a StudioActor between base and mutated art. Each clip's value holds after it ends.</summary>
    [TrackColor(0.8f, 0.25f, 0.6f)]
    [TrackClipType(typeof(ActorVariantClip))]
    [TrackBindingType(typeof(StudioActor))]
    public class ActorVariantTrack : TrackAsset
    {
        public bool initialMutated;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<ActorVariantMixer> mixer = ScriptPlayable<ActorVariantMixer>.Create(graph, inputCount);
            ActorVariantMixer behaviour = mixer.GetBehaviour();
            behaviour.initialMutated = initialMutated;
            behaviour.clips = StudioClipSpan.FromTrack(this);
            return mixer;
        }
    }

    // ── Mixer ─────────────────────────────────────────────────────────────────────
    public class ActorVariantMixer : PlayableBehaviour
    {
        public bool initialMutated;
        public List<StudioClipSpan> clips = new List<StudioClipSpan>();

        private StudioActor _actor;
        private bool _hasCached;
        private bool _originalMutated;
        private bool _hasApplied;
        private bool _lastApplied;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            StudioActor actor = playerData as StudioActor;
            if (actor == null) return;

            if (!_hasCached || _actor != actor)
            {
                _actor = actor;
                _originalMutated = actor.IsMutated;
                _hasCached = true;
                _hasApplied = false;
            }

            bool state = Evaluate(playable.GetTime());
            if (_hasApplied && state == _lastApplied) return;

            actor.SetVariant(state);
            _lastApplied = state;
            _hasApplied = true;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (Application.isPlaying || !_hasCached || _actor == null) return;
            _actor.SetVariant(_originalMutated);
            _hasCached = false;
            _hasApplied = false;
        }

        // ── Evaluation ────────────────────────────────────────────────────────────
        public bool Evaluate(double time)
        {
            bool state = initialMutated;
            if (clips == null) return state;

            foreach (StudioClipSpan span in clips)
            {
                if (span.start > time) break;
                if (span.asset is ActorVariantClip variant) state = variant.mutated;
            }
            return state;
        }
    }
}
