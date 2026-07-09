using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace GoodCopBadCop.SuspectBehaviorAnimation
{
    public sealed class SuspectBehaviorAnimationAdapter : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        private readonly Dictionary<object, ActivePreset> activePresets = new();
        private RuntimeAnimatorController originalController;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixerPlayable;
        private AnimatorControllerPlayable controllerPlayable;
        private AnimationClipPlayable clipPlayable;
        private AnimationPlayableOutput output;
        private Coroutine playbackCoroutine;
        private Coroutine transitionCoroutine;
        private object currentSource;
        private BehaviorAnimationPreset currentPreset;
        private AnimationClip currentClip;
        private bool isInPause;

        public BehaviorAnimationPreset CurrentPreset => currentPreset;
        public AnimationClip CurrentClip => currentClip;
        public bool IsInPause => isInPause;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (animator != null)
                originalController = animator.runtimeAnimatorController;
        }

        private void OnDisable()
        {
            StopGraph();
            StopPlaybackCoroutine();
            StopTransitionCoroutine();
            activePresets.Clear();
            currentSource = null;
            currentPreset = null;
            currentClip = null;
            isInPause = false;

            if (animator != null && originalController != null)
                animator.runtimeAnimatorController = originalController;
        }

        public void Apply(object source, BehaviorAnimationPreset preset)
        {
            if (source == null || preset == null)
                return;

            AnimationClip clip = preset.SelectClip(source as Object);
            if (clip == null)
                return;

            activePresets[source] = new ActivePreset(source, preset, clip);
            ApplyHighestPriorityPreset();
        }

        public void Release(object source)
        {
            if (source == null)
                return;

            if (!activePresets.Remove(source))
                return;

            ApplyHighestPriorityPreset();
        }

        public void ReleaseAll()
        {
            activePresets.Clear();
            currentSource = null;
            currentPreset = null;
            currentClip = null;
            isInPause = false;
            StopPlaybackCoroutine();
            StopTransitionCoroutine();
            StopGraph();
        }

        private void ApplyHighestPriorityPreset()
        {
            if (activePresets.Count == 0)
            {
                currentSource = null;
                currentPreset = null;
                currentClip = null;
                isInPause = false;
                StopPlaybackCoroutine();
                StopTransitionCoroutine();
                StopGraph();
                return;
            }

            object nextSource = null;
            ActivePreset next = default;
            bool hasNext = false;

            foreach (KeyValuePair<object, ActivePreset> entry in activePresets)
            {
                if (!hasNext || entry.Value.Preset.Priority >= next.Preset.Priority)
                {
                    hasNext = true;
                    nextSource = entry.Key;
                    next = entry.Value;
                }
            }

            if (!hasNext)
                return;

            if (ReferenceEquals(currentSource, nextSource) && (graph.IsValid() || playbackCoroutine != null))
                return;

            currentSource = nextSource;
            Apply(next);
        }

        private void Apply(ActivePreset active)
        {
            StopPlaybackCoroutine();

            if (active.Preset.PlayContinuously)
            {
                Play(active, 0f);
                transitionCoroutine = StartCoroutine(FadeClipWeight(0f, 1f, active.Preset.BlendInSeconds));
                return;
            }

            playbackCoroutine = StartCoroutine(PlayWithPauses(active));
        }

        private IEnumerator PlayWithPauses(ActivePreset active)
        {
            int cycleIndex = 0;

            while (ReferenceEquals(currentSource, active.Source))
            {
                AnimationClip clip = active.Preset.SelectClip(active.Source as Object, cycleIndex);
                if (clip == null)
                    yield break;

                ActivePreset cycle = new(active.Source, active.Preset, clip);
                Play(cycle, 0f);
                yield return FadeClipWeight(0f, 1f, active.Preset.BlendInSeconds);

                float clipSeconds = clip.length / active.Preset.PlaybackSpeed;
                if (active.Preset.MaxPlaySeconds > 0f)
                    clipSeconds = Mathf.Min(clipSeconds, active.Preset.MaxPlaySeconds);

                float playSeconds = Mathf.Max(0f, clipSeconds - active.Preset.BlendInSeconds);
                if (playSeconds > 0f)
                    yield return new WaitForSeconds(playSeconds);

                yield return FadeClipWeight(1f, 0f, active.Preset.BlendOutSeconds);
                isInPause = true;
                currentClip = null;
                StopGraph();
                yield return new WaitForSeconds(active.Preset.SelectPauseSeconds(active.Source as Object, cycleIndex));
                cycleIndex++;
            }
        }

        private void Play(ActivePreset active, float initialClipWeight)
        {
            if (animator == null || active.Clip == null)
                return;

            StopTransitionCoroutine();
            StopGraph();
            currentPreset = active.Preset;
            currentClip = active.Clip;
            isInPause = false;

            graph = PlayableGraph.Create($"{nameof(SuspectBehaviorAnimationAdapter)}:{gameObject.name}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            clipPlayable = AnimationClipPlayable.Create(graph, active.Clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetSpeed(active.Preset.PlaybackSpeed);

            int clipInputIndex = 0;
            if (originalController != null)
            {
                mixerPlayable = AnimationMixerPlayable.Create(graph, 2);
                controllerPlayable = AnimatorControllerPlayable.Create(graph, originalController);
                graph.Connect(controllerPlayable, 0, mixerPlayable, 0);
                mixerPlayable.SetInputWeight(0, 1f - Mathf.Clamp01(initialClipWeight));
                clipInputIndex = 1;
            }
            else
            {
                mixerPlayable = AnimationMixerPlayable.Create(graph, 1);
            }

            graph.Connect(clipPlayable, 0, mixerPlayable, clipInputIndex);
            mixerPlayable.SetInputWeight(clipInputIndex, Mathf.Clamp01(initialClipWeight));

            output = AnimationPlayableOutput.Create(graph, "Behavior Animation", animator);
            output.SetSourcePlayable(mixerPlayable);
            graph.Play();
        }

        private IEnumerator FadeClipWeight(float from, float to, float duration)
        {
            if (!graph.IsValid() || !mixerPlayable.IsValid())
                yield break;

            if (duration <= 0f)
            {
                SetClipWeight(to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration && graph.IsValid() && mixerPlayable.IsValid())
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetClipWeight(Mathf.Lerp(from, to, t));
                yield return null;
            }

            SetClipWeight(to);
        }

        private void SetClipWeight(float weight)
        {
            if (!mixerPlayable.IsValid())
                return;

            float clamped = Mathf.Clamp01(weight);
            int clipInputIndex = originalController != null ? 1 : 0;
            if (originalController != null)
                mixerPlayable.SetInputWeight(0, 1f - clamped);

            mixerPlayable.SetInputWeight(clipInputIndex, clamped);
        }

        private void StopGraph()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        private void StopPlaybackCoroutine()
        {
            if (playbackCoroutine == null)
                return;

            StopCoroutine(playbackCoroutine);
            playbackCoroutine = null;
        }

        private void StopTransitionCoroutine()
        {
            if (transitionCoroutine == null)
                return;

            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        private readonly struct ActivePreset
        {
            public readonly object Source;
            public readonly BehaviorAnimationPreset Preset;
            public readonly AnimationClip Clip;

            public ActivePreset(object source, BehaviorAnimationPreset preset, AnimationClip clip)
            {
                Source = source;
                Preset = preset;
                Clip = clip;
            }
        }
    }
}
