using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class RandomTentacleAnomaly : MutationAnomaly
{
    [SerializeField] private GameObject[] tentacles;
    [SerializeField] private Transform[] parentBones;

    private void Awake()
    {
        InitializeTumors();
    }

    void InitializeTumors()
    {
        for (var i = 0; i < tentacles.Length; i++)
        {
            var tumor = tentacles[i];
            tumor.transform.parent = parentBones[i];
        }
    }

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        ActivateTumor();
    }

    private void ActivateTumor()
    {
        int randomTumorAmount = Random.Range(1, tentacles.Length + 1);

        ShuffleTumors();

        for (int i = 0; i < randomTumorAmount; i++)
        {
            tentacles[i].SetActive(true);
        }
    }

    private void ShuffleTumors()
    {
        for (int i = tentacles.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (tentacles[i], tentacles[j]) = (tentacles[j], tentacles[i]);
        }
    }

    /// <summary>
    /// For each tumor, finds and assigns the closest skeleton bone in the SuspectCharacter's avatar.
    /// Run this from the Inspector context menu while the prefab is open in Prefab Mode (T-Pose).
    /// Only considers actual humanoid bones — mesh renderers and other non-bone transforms are excluded.
    /// </summary>
    [ContextMenu("Auto-Assign Parent Bones")]
    private void AutoAssignParentBones()
    {
        SuspectCharacter suspect = transform.root.GetComponent<SuspectCharacter>();
        if (suspect == null)
        {
            Debug.LogError("[RandomTumorAnomaly] No SuspectCharacter found in hierarchy root.");
            return;
        }

        Animator animator = suspect.animator;
        Transform skeletonRoot = animator.avatarRoot;
        if (skeletonRoot == null)
        {
            Debug.LogError("[RandomTumorAnomaly] Animator avatarRoot is null. Open the prefab in Prefab Mode and try again.");
            return;
        }

        Transform[] bones = CollectHumanoidBones(animator);
        parentBones = new Transform[tentacles.Length];

        for (int i = 0; i < tentacles.Length; i++)
        {
            if (tentacles[i] == null)
                continue;

            parentBones[i] = FindClosestBone(tentacles[i].transform.position, bones);
        }

        Debug.Log($"[RandomTumorAnomaly] Assigned {parentBones.Length} parent bones from {bones.Length} humanoid bones.");

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>
    /// Returns every humanoid bone transform the Animator exposes via <see cref="HumanBodyBones"/>.
    /// This explicitly excludes mesh objects and other non-bone transforms that happen to be
    /// children of the skeleton root.
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
}
