using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class RandomTentacleAnomaly : PhysicalAnomaly
{
    [SerializeField] private GameObject[] tentacles;
    [SerializeField] private Transform[] parentBones;

    private void Awake()
    {
        InitializeTentacles();
    }

    private void InitializeTentacles()
    {
        for (var i = 0; i < tentacles.Length; i++)
        {
            var tentacle = tentacles[i];
            tentacle.transform.parent = parentBones[i];
        }
    }

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        int[] activeIndices = PickActiveIndices();
        ApplyActiveIndices(activeIndices);
    }

    /// <summary>
    /// Activates the specified tentacle indices. Called on clients to replicate
    /// the server's activation without running independent RNG.
    /// </summary>
    public void ActivateWithIndices(int[] activeIndices)
    {
        ApplyActiveIndices(activeIndices);
    }

    /// <summary>
    /// Picks a random subset of tentacle indices using a Fisher-Yates shuffle
    /// and returns them. Only call this on the server.
    /// </summary>
    public int[] PickActiveIndices()
    {
        int[] indices = BuildShuffledIndices();
        int count = Random.Range(2, tentacles.Length + 1);
        int[] activeIndices = new int[count];
        Array.Copy(indices, activeIndices, count);
        return activeIndices;
    }

    private void ApplyActiveIndices(int[] activeIndices)
    {
        SetTentaclesActive(false);

        foreach (int index in activeIndices)
        {
            if (index >= 0 && index < tentacles.Length && tentacles[index] != null)
                tentacles[index].SetActive(true);
        }
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        InitializeDisabled();
    }

    public override void InitializeDisabled()
    {
        SetTentaclesActive(false);
    }

    private void SetTentaclesActive(bool active)
    {
        foreach (GameObject tentacle in tentacles)
        {
            if (tentacle != null)
                tentacle.SetActive(active);
        }
    }

    /// <summary>Returns a Fisher-Yates shuffled array of tentacle indices.</summary>
    private int[] BuildShuffledIndices()
    {
        int[] indices = new int[tentacles.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        for (int i = indices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        return indices;
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
