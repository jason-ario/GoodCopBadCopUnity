using System.Collections;
using UnityEngine;

/// <summary>
/// Biological anomaly that periodically plays a vomit particle system.
/// The vomit prefab is expected to be a direct child of this GameObject and must
/// contain one or more <see cref="ParticleSystem"/> components across its children.
///
/// On activation the prefab is reparented to the closest humanoid bone, then
/// a coroutine fires all particle systems 1–2 times at random intervals spread
/// across a 60-second window.
/// </summary>
public class VomitAnomaly : VitalsAnomaly
{
    [Tooltip("Vomit prefab child of this GameObject. Must have ParticleSystems across its children.")]
    [SerializeField] private GameObject vomitPrefab;

    [Header("Timing")]
    [Tooltip("Total duration (seconds) over which vomit events are distributed.")]
    [SerializeField] private float windowDuration = 60f;

    [Tooltip("Minimum number of vomit events during the window.")]
    [SerializeField] private int minEvents = 1;

    [Tooltip("Maximum number of vomit events during the window.")]
    [SerializeField] private int maxEvents = 2;

    [Header("Animation")]
    [Tooltip("Animator trigger fired on the suspect each time a vomit event plays.")]
    [SerializeField] private string vomitAnimTrigger = "Vomit";

    private ParticleSystem[] _particles;
    private Coroutine _activeCoroutine;
    private SuspectCharacter _suspect;

    private void Awake()
    {
        _suspect = transform.root.GetComponent<SuspectCharacter>();

        if (vomitPrefab == null)
        {
            Debug.LogWarning($"[VomitAnomaly] vomitPrefab is not assigned on '{gameObject.name}'.", this);
            return;
        }

        ParentToClosestBone();
        _particles = vomitPrefab.GetComponentsInChildren<ParticleSystem>();

        if (_particles.Length == 0)
            Debug.LogWarning($"[VomitAnomaly] No ParticleSystems found in children of vomitPrefab '{vomitPrefab.name}'.", this);
    }

    /// <inheritdoc/>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_particles == null || _particles.Length == 0) return;

        _activeCoroutine = StartCoroutine(ScheduleVomitEvents());
    }

    /// <inheritdoc/>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        foreach (ParticleSystem ps in _particles)
        {
            if (ps != null && ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>
    /// Picks 1–2 random timestamps within <see cref="windowDuration"/> and plays
    /// the particle system at each one.
    /// </summary>
    private IEnumerator ScheduleVomitEvents()
    {
        int eventCount = Random.Range(minEvents, maxEvents + 1);

        // Build sorted event times so they don't overlap awkwardly.
        float[] eventTimes = BuildSortedRandomTimes(eventCount, windowDuration);

        float elapsed = 0f;

        for (int i = 0; i < eventCount; i++)
        {
            float waitUntil = eventTimes[i];

            while (elapsed < waitUntil)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (_particles != null && _particles.Length > 0)
            {
                foreach (ParticleSystem ps in _particles)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play();
                }
            }

            if (_suspect != null && !string.IsNullOrEmpty(vomitAnimTrigger))
                _suspect.FireAnimatorTrigger(vomitAnimTrigger);
        }

        _activeCoroutine = null;
    }

    /// <summary>
    /// Returns <paramref name="count"/> distinct timestamps sorted ascending,
    /// distributed randomly within [0, <paramref name="duration"/>].
    /// </summary>
    private static float[] BuildSortedRandomTimes(int count, float duration)
    {
        float[] times = new float[count];

        for (int i = 0; i < count; i++)
            times[i] = Random.Range(0f, duration);

        System.Array.Sort(times);
        return times;
    }

    /// <summary>
    /// Parents the vomit prefab to the humanoid bone closest to its current world position.
    /// Preserves world position/rotation so the prefab stays in the authored location.
    /// Run <see cref="AutoAssignClosestBone"/> from the context menu while the prefab
    /// is open in Prefab Mode (T-Pose) to preview the result in the Editor.
    /// </summary>
    private void ParentToClosestBone()
    {
        if (_suspect == null)
            _suspect = transform.root.GetComponent<SuspectCharacter>();

        if (_suspect == null)
        {
            Debug.LogWarning("[VomitAnomaly] No SuspectCharacter found at hierarchy root.", this);
            return;
        }

        Animator animator = _suspect.animator;
        if (animator == null)
        {
            Debug.LogWarning("[VomitAnomaly] SuspectCharacter has no Animator.", this);
            return;
        }

        Transform[] bones = CollectHumanoidBones(animator);
        if (bones.Length == 0)
        {
            Debug.LogWarning("[VomitAnomaly] No humanoid bones found in Animator.", this);
            return;
        }

        Transform closest = FindClosestBone(vomitPrefab.transform.position, bones);
        if (closest != null)
            vomitPrefab.transform.SetParent(closest, worldPositionStays: true);
    }

    /// <summary>
    /// Returns every humanoid bone transform the Animator exposes via <see cref="HumanBodyBones"/>,
    /// explicitly excluding non-bone transforms such as mesh renderers.
    /// </summary>
    private static Transform[] CollectHumanoidBones(Animator animator)
    {
        var bones = new System.Collections.Generic.List<Transform>();

        foreach (HumanBodyBones boneId in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (boneId == HumanBodyBones.LastBone)
                continue;

            Transform bone = animator.GetBoneTransform(boneId);
            if (bone != null)
                bones.Add(bone);
        }

        return bones.ToArray();
    }

    /// <summary>Returns the bone whose world position is closest to <paramref name="worldPos"/>.</summary>
    private static Transform FindClosestBone(Vector3 worldPos, Transform[] bones)
    {
        Transform closest = null;
        float closestSqDist = float.MaxValue;

        foreach (Transform bone in bones)
        {
            float sqDist = (worldPos - bone.position).sqrMagnitude;
            if (sqDist < closestSqDist)
            {
                closestSqDist = sqDist;
                closest = bone;
            }
        }

        return closest;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor helper — parents the vomit prefab to its closest bone and marks the
    /// component dirty. Open the suspect prefab in Prefab Mode (T-Pose) before running.
    /// </summary>
    [ContextMenu("Auto-Assign Closest Bone")]
    private void AutoAssignClosestBone()
    {
        if (vomitPrefab == null)
        {
            Debug.LogError("[VomitAnomaly] vomitPrefab is not assigned.");
            return;
        }

        ParentToClosestBone();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[VomitAnomaly] Reparented vomit prefab to closest humanoid bone.");
    }
#endif
}
