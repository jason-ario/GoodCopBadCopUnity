using System.Collections;
using UnityEngine;

/// <summary>
/// Biological anomaly that periodically plays a vomit particle system.
/// The vomit prefab is expected to be a direct child of this GameObject and must
/// contain one or more <see cref="ParticleSystem"/> components across its children.
///
/// On activation the prefab is reparented to the closest humanoid bone, then
/// a coroutine fires all particle systems repeatedly at regular intervals
/// (with a small random jitter) for as long as the anomaly stays active.
/// </summary>
public class VomitAnomaly : VitalsAnomaly
{
    [Tooltip("Vomit prefab child of this GameObject. Must have ParticleSystems across its children.")]
    [SerializeField] private GameObject vomitPrefab;

    [Header("Timing")]
    [Tooltip("Delay (seconds) before the first vomit event after activation.")]
    [SerializeField] private float initialDelay = 3f;

    [Tooltip("Base interval (seconds) between consecutive vomit events.")]
    [SerializeField] private float interval = 10f;

    [Tooltip("Random +/- variation (seconds) applied to each interval so it doesn't feel mechanical.")]
    [SerializeField] private float intervalJitter = 2f;

    [Header("Animation")]
    [Tooltip("Animator trigger fired on the suspect each time a vomit event plays.")]
    [SerializeField] private string vomitAnimTrigger = "Vomit";

    private ParticleSystem[] _particles;
    private Coroutine _activeCoroutine;
    private SuspectCharacter _suspect;
    private bool _isActive;

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

        _isActive = true;
        StartVomitLoop();
    }

    /// <inheritdoc/>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        _isActive = false;
        StopVomitLoop();

        if (_particles == null) return;

        foreach (ParticleSystem ps in _particles)
        {
            if (ps != null && ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnEnable()
    {
        // Coroutines die when the GameObject is disabled; resume the loop if still active.
        if (_isActive)
            StartVomitLoop();
    }

    private void OnDisable()
    {
        _activeCoroutine = null;
    }

    private void StartVomitLoop()
    {
        if (_particles == null || _particles.Length == 0) return;
        if (!isActiveAndEnabled) return;

        StopVomitLoop();
        _activeCoroutine = StartCoroutine(VomitLoop());
    }

    private void StopVomitLoop()
    {
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }
    }

    /// <summary>
    /// Plays a vomit event after <see cref="initialDelay"/>, then repeats every
    /// <see cref="interval"/> ± <see cref="intervalJitter"/> seconds until deactivated.
    /// </summary>
    private IEnumerator VomitLoop()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, initialDelay));

        while (_isActive)
        {
            PlayVomitEvent();

            float wait = interval + Random.Range(-intervalJitter, intervalJitter);
            yield return new WaitForSeconds(Mathf.Max(1f, wait));
        }

        _activeCoroutine = null;
    }

    private void PlayVomitEvent()
    {
        foreach (ParticleSystem ps in _particles)
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }

        if (_suspect != null && !string.IsNullOrEmpty(vomitAnimTrigger))
            _suspect.FireAnimatorTrigger(vomitAnimTrigger);
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
