using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    // ── Track ─────────────────────────────────────────────────────────────────────
    /// <summary>Moves / turns a StudioActor deterministically from an initial position and yaw.</summary>
    [TrackColor(0.25f, 0.75f, 0.35f)]
    [TrackClipType(typeof(ActorMoveClip))]
    [TrackClipType(typeof(ActorFaceClip))]
    [TrackBindingType(typeof(StudioActor))]
    public class ActorMoveTrack : TrackAsset
    {
        public Vector3 initialPosition;
        public float initialYaw;

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<ActorMoveMixer> mixer = ScriptPlayable<ActorMoveMixer>.Create(graph, inputCount);
            ActorMoveMixer behaviour = mixer.GetBehaviour();
            behaviour.initialPosition = initialPosition;
            behaviour.initialYaw = initialYaw;
            behaviour.clips = StudioClipSpan.FromTrack(this);
            return mixer;
        }
    }

    // ── Mixer ─────────────────────────────────────────────────────────────────────
    public class ActorMoveMixer : PlayableBehaviour
    {
        private const float TurnDuration = 0.25f;
        private const float EndYawPortion = 0.2f;
        private const float MinMoveDistance = 0.01f;

        public Vector3 initialPosition;
        public float initialYaw;
        public List<StudioClipSpan> clips = new List<StudioClipSpan>();

        private StudioActor _actor;
        private bool _hasCached;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            StudioActor actor = playerData as StudioActor;
            if (actor == null) return;

            if (!_hasCached || _actor != actor)
            {
                _actor = actor;
                _originalPosition = actor.transform.position;
                _originalRotation = actor.transform.rotation;
                _hasCached = true;
            }

            Evaluate(playable.GetTime(), out Vector3 position, out float yaw);
            actor.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (Application.isPlaying || !_hasCached || _actor == null) return;
            _actor.transform.SetPositionAndRotation(_originalPosition, _originalRotation);
            _hasCached = false;
        }

        // ── Evaluation ────────────────────────────────────────────────────────────
        public void Evaluate(double time, out Vector3 position, out float yaw)
        {
            position = initialPosition;
            yaw = initialYaw;
            if (clips == null) return;

            foreach (StudioClipSpan span in clips)
            {
                if (time < span.start) break;

                bool finished = time >= span.end;
                float localTime = (float)(time - span.start);
                float n = span.Normalized(time);

                if (span.asset is ActorMoveClip move)
                {
                    if (finished) ApplyMoveFinal(move, ref position, ref yaw);
                    else ApplyMoveAt(move, n, localTime, ref position, ref yaw);
                }
                else if (span.asset is ActorFaceClip face)
                {
                    if (finished) yaw = face.toYaw;
                    else yaw = Mathf.LerpAngle(yaw, face.toYaw, EaseValue(face.ease, n));
                }

                if (!finished) break; // stop after the active clip
            }
        }

        private static void ApplyMoveFinal(ActorMoveClip move, ref Vector3 position, ref float yaw)
        {
            Vector3 delta = move.toPosition - position;
            delta.y = 0f;

            if (move.useEndYaw) yaw = move.endYaw;
            else if (move.faceMoveDirection && delta.magnitude > MinMoveDistance) yaw = YawOf(delta);

            position = move.toPosition;
        }

        private static void ApplyMoveAt(ActorMoveClip move, float n, float localTime, ref Vector3 position, ref float yaw)
        {
            Vector3 startPosition = position;
            float startYaw = yaw;

            Vector3 delta = move.toPosition - startPosition;
            delta.y = 0f;

            float e = EaseValue(move.ease, n);
            position = Vector3.LerpUnclamped(startPosition, move.toPosition, e);

            float current = startYaw;
            if (move.faceMoveDirection && delta.magnitude > MinMoveDistance)
                current = Mathf.LerpAngle(startYaw, YawOf(delta), Mathf.Clamp01(localTime / TurnDuration));

            if (move.useEndYaw && n > 1f - EndYawPortion)
                current = Mathf.LerpAngle(current, move.endYaw, Mathf.Clamp01((n - (1f - EndYawPortion)) / EndYawPortion));

            yaw = current;
        }

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static float EaseValue(AnimationCurve curve, float n)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(n) : n;
        }
    }
}
