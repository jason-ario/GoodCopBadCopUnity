using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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
        private AnimationClipPlayable baseClipPlayable;
        private AnimationClipPlayable overrideClipPlayable;
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

            int sequenceSeed = BuildSequenceSeed(source, preset);
            AnimationClip clip = preset.SelectClip(sequenceSeed);
            if (clip == null)
                return;

            activePresets[source] = new ActivePreset(source, preset, clip, sequenceSeed);
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
                if (active.Preset.UseFirstClipAsContinuousBase && active.Preset.Clips != null && active.Preset.Clips.Length > 1)
                {
                    playbackCoroutine = StartCoroutine(PlayBaseWithOverrides(active));
                    return;
                }

                Play(active, 0f);
                transitionCoroutine = StartCoroutine(FadeClipWeight(0f, 1f, active.Preset.BlendInSeconds));
                return;
            }

            playbackCoroutine = StartCoroutine(PlayWithPauses(active));
        }

        private IEnumerator PlayWithPauses(ActivePreset active)
        {
            int cycleIndex = 0;
            AnimationClip previousClip = null;

            while (ReferenceEquals(currentSource, active.Source))
            {
                AnimationClip clip = active.Preset.SelectClip(active.SequenceSeed, cycleIndex, previousClip);
                if (clip == null)
                    yield break;

                previousClip = clip;
                ActivePreset cycle = new(active.Source, active.Preset, clip, active.SequenceSeed);
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
                yield return new WaitForSeconds(active.Preset.SelectPauseSeconds(active.SequenceSeed, cycleIndex));
                cycleIndex++;
            }
        }

        private IEnumerator PlayBaseWithOverrides(ActivePreset active)
        {
            AnimationClip baseClip = active.Preset.SelectBaseClip();
            if (baseClip == null)
                yield break;

            ActivePreset baseActive = new(active.Source, active.Preset, baseClip, active.SequenceSeed);
            CreateBaseOverrideGraph(baseActive, null, 0f, 0f);
            yield return FadeBaseOverrideWeights(0f, 1f, 0f, 0f, active.Preset.BlendInSeconds);

            int cycleIndex = 0;
            AnimationClip previousOverrideClip = null;

            while (ReferenceEquals(currentSource, active.Source))
            {
                isInPause = false;
                currentClip = baseClip;
                yield return new WaitForSeconds(active.Preset.SelectPauseSeconds(active.SequenceSeed, cycleIndex));

                AnimationClip overrideClip = active.Preset.SelectOverrideClip(active.SequenceSeed, cycleIndex, previousOverrideClip);
                if (overrideClip == null)
                {
                    cycleIndex++;
                    continue;
                }

                previousOverrideClip = overrideClip;
                ActivePreset overrideActive = new(active.Source, active.Preset, overrideClip, active.SequenceSeed);
                CreateBaseOverrideGraph(baseActive, overrideActive, 1f, 0f);
                currentClip = overrideClip;
                yield return FadeBaseOverrideWeights(1f, 0f, 0f, 1f, active.Preset.BlendInSeconds);

                float clipSeconds = overrideClip.length / active.Preset.PlaybackSpeed;
                if (active.Preset.MaxPlaySeconds > 0f)
                    clipSeconds = Mathf.Min(clipSeconds, active.Preset.MaxPlaySeconds);

                float playSeconds = Mathf.Max(0f, clipSeconds - active.Preset.BlendInSeconds);
                if (playSeconds > 0f)
                    yield return new WaitForSeconds(playSeconds);

                yield return FadeBaseOverrideWeights(0f, 1f, 1f, 0f, active.Preset.BlendOutSeconds);
                currentClip = baseClip;
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

        private void CreateBaseOverrideGraph(ActivePreset baseActive, ActivePreset? overrideActive, float baseWeight, float overrideWeight)
        {
            if (animator == null || baseActive.Clip == null)
                return;

            StopTransitionCoroutine();
            StopGraph();
            currentPreset = baseActive.Preset;
            currentClip = overrideActive.HasValue ? overrideActive.Value.Clip : baseActive.Clip;
            isInPause = false;

            graph = PlayableGraph.Create($"{nameof(SuspectBehaviorAnimationAdapter)}:{gameObject.name}:BaseOverride");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            int inputCount = originalController != null ? 3 : 2;
            mixerPlayable = AnimationMixerPlayable.Create(graph, inputCount);

            int baseInputIndex = originalController != null ? 1 : 0;
            int overrideInputIndex = originalController != null ? 2 : 1;

            if (originalController != null)
            {
                controllerPlayable = AnimatorControllerPlayable.Create(graph, originalController);
                graph.Connect(controllerPlayable, 0, mixerPlayable, 0);
            }

            baseClipPlayable = AnimationClipPlayable.Create(graph, baseActive.Clip);
            baseClipPlayable.SetApplyFootIK(false);
            baseClipPlayable.SetSpeed(baseActive.Preset.PlaybackSpeed);
            graph.Connect(baseClipPlayable, 0, mixerPlayable, baseInputIndex);

            if (overrideActive.HasValue && overrideActive.Value.Clip != null)
            {
                overrideClipPlayable = AnimationClipPlayable.Create(graph, overrideActive.Value.Clip);
                overrideClipPlayable.SetApplyFootIK(false);
                overrideClipPlayable.SetSpeed(overrideActive.Value.Preset.PlaybackSpeed);
                graph.Connect(overrideClipPlayable, 0, mixerPlayable, overrideInputIndex);
            }

            SetBaseOverrideWeights(baseWeight, overrideWeight);

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

        private IEnumerator FadeBaseOverrideWeights(
            float fromBase,
            float toBase,
            float fromOverride,
            float toOverride,
            float duration)
        {
            if (!graph.IsValid() || !mixerPlayable.IsValid())
                yield break;

            if (duration <= 0f)
            {
                SetBaseOverrideWeights(toBase, toOverride);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration && graph.IsValid() && mixerPlayable.IsValid())
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetBaseOverrideWeights(
                    Mathf.Lerp(fromBase, toBase, t),
                    Mathf.Lerp(fromOverride, toOverride, t));
                yield return null;
            }

            SetBaseOverrideWeights(toBase, toOverride);
        }

        private void SetBaseOverrideWeights(float baseWeight, float overrideWeight)
        {
            if (!mixerPlayable.IsValid())
                return;

            float baseClamped = Mathf.Clamp01(baseWeight);
            float overrideClamped = Mathf.Clamp01(overrideWeight);
            float controllerWeight = Mathf.Clamp01(1f - Mathf.Max(baseClamped, overrideClamped));

            if (originalController != null)
            {
                mixerPlayable.SetInputWeight(0, controllerWeight);
                mixerPlayable.SetInputWeight(1, baseClamped);
                mixerPlayable.SetInputWeight(2, overrideClamped);
            }
            else
            {
                mixerPlayable.SetInputWeight(0, baseClamped);
                mixerPlayable.SetInputWeight(1, overrideClamped);
            }
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

        private int BuildSequenceSeed(object source, BehaviorAnimationPreset preset)
        {
            string sourceType = source != null ? source.GetType().FullName : "null";
            string presetName = preset != null ? preset.name : "null";

            if (TryGetNetworkObjectId(source, out ulong networkObjectId))
                return StableHash($"net:{networkObjectId}:{sourceType}:{presetName}");

            return StableHash($"local:{sourceType}:{GetHierarchyPath(source)}:{presetName}");
        }

        private bool TryGetNetworkObjectId(object source, out ulong networkObjectId)
        {
            NetworkObject networkObject = null;
            if (source is Component component)
                networkObject = component.GetComponentInParent<NetworkObject>();

            if (networkObject == null)
                networkObject = GetComponentInParent<NetworkObject>();

            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObjectId = networkObject.NetworkObjectId;
                return true;
            }

            networkObjectId = 0;
            return false;
        }

        private static string GetHierarchyPath(object source)
        {
            Transform transform = source switch
            {
                Component component => component.transform,
                GameObject gameObject => gameObject.transform,
                _ => null
            };

            if (transform == null)
                return source is Object unityObject ? unityObject.name : "null";

            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];

                return hash;
            }
        }

        private readonly struct ActivePreset
        {
            public readonly object Source;
            public readonly BehaviorAnimationPreset Preset;
            public readonly AnimationClip Clip;
            public readonly int SequenceSeed;

            public ActivePreset(object source, BehaviorAnimationPreset preset, AnimationClip clip, int sequenceSeed)
            {
                Source = source;
                Preset = preset;
                Clip = clip;
                SequenceSeed = sequenceSeed;
            }
        }
    }
}
