using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    // ── Track ─────────────────────────────────────────────────────────────────────
    /// <summary>Animates a CinemachineCamera's follow offset, lens and recomposer per shot.</summary>
    [TrackColor(0.95f, 0.65f, 0.15f)]
    [TrackClipType(typeof(ShotRigClip))]
    [TrackBindingType(typeof(CinemachineCamera))]
    public class ShotRigTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<ShotRigMixer> mixer = ScriptPlayable<ShotRigMixer>.Create(graph, inputCount);
            mixer.GetBehaviour().clips = StudioClipSpan.FromTrack(this);
            return mixer;
        }
    }

    // ── Mixer ─────────────────────────────────────────────────────────────────────
    public class ShotRigMixer : PlayableBehaviour
    {
        public List<StudioClipSpan> clips = new List<StudioClipSpan>();

        private CinemachineCamera _camera;
        private CinemachineFollow _follow;
        private CinemachineRecomposer _recomposer;
        private bool _hasCached;
        private Vector3 _originalFollowOffset;
        private LensSettings _originalLens;
        private float _originalPan;
        private float _originalZoomScale;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            CinemachineCamera cam = playerData as CinemachineCamera;
            if (cam == null) return;

            if (!_hasCached || _camera != cam) Cache(cam);

            if (!TryGetClip(playable.GetTime(), out ShotRigClip clip, out float n)) return;

            float e = clip.ease != null && clip.ease.length > 0 ? clip.ease.Evaluate(n) : n;

            if (_follow != null)
            {
                Vector3 offset = Vector3.LerpUnclamped(clip.fromOffset, clip.toOffset, e);
                _follow.FollowOffset = Quaternion.Euler(0f, clip.orbitDegrees * e, 0f) * offset;
            }

            LensSettings lens = cam.Lens;
            lens.FieldOfView = Mathf.LerpUnclamped(clip.fromFov, clip.toFov, e);
            lens.Dutch = clip.dutch;
            cam.Lens = lens;

            if (_recomposer != null)
            {
                _recomposer.ZoomScale = Mathf.LerpUnclamped(clip.fromZoomScale, clip.toZoomScale, e);
                _recomposer.Pan = clip.whipPanDegrees * WhipFactor(n, clip.whipPanStart);
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (Application.isPlaying || !_hasCached || _camera == null) return;

            if (_follow != null) _follow.FollowOffset = _originalFollowOffset;
            _camera.Lens = _originalLens;
            if (_recomposer != null)
            {
                _recomposer.Pan = _originalPan;
                _recomposer.ZoomScale = _originalZoomScale;
            }
            _hasCached = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private void Cache(CinemachineCamera cam)
        {
            _camera = cam;
            _follow = cam.GetComponent<CinemachineFollow>();
            _recomposer = cam.GetComponent<CinemachineRecomposer>();
            _originalLens = cam.Lens;
            if (_follow != null) _originalFollowOffset = _follow.FollowOffset;
            if (_recomposer != null)
            {
                _originalPan = _recomposer.Pan;
                _originalZoomScale = _recomposer.ZoomScale;
            }
            _hasCached = true;
        }

        /// <summary>Active clip, else the most recently ended one (n=1), else the first upcoming one (n=0).</summary>
        private bool TryGetClip(double time, out ShotRigClip clip, out float n)
        {
            clip = null;
            n = 0f;
            if (clips == null) return false;

            foreach (StudioClipSpan span in clips)
            {
                if (!(span.asset is ShotRigClip shot)) continue;

                if (time < span.start)
                {
                    if (clip == null) { clip = shot; n = 0f; }
                    break;
                }

                clip = shot;
                if (time < span.end) { n = span.Normalized(time); break; }
                n = 1f;
            }
            return clip != null;
        }

        private static float WhipFactor(float n, float start)
        {
            if (start >= 1f) return n >= 1f ? 1f : 0f;
            float s = Mathf.Clamp01((n - start) / (1f - start));
            return s * s * (3f - 2f * s);
        }
    }
}
