using UnityEngine;
using UnityEngine.Animations.Rigging;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(BoneRenderer))]
public class AutoAssignHumanoidBones : MonoBehaviour
{
    [ContextMenu("Auto Assign Humanoid Bones")]
    public void AssignBones()
    {
        Animator animator = GetComponent<Animator>();
        BoneRenderer boneRenderer = GetComponent<BoneRenderer>();

        if (!animator.isHuman)
        {
            Debug.LogError("Animator is not Humanoid!");
            return;
        }

        List<Transform> boneList = new List<Transform>();

        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone)
                continue;

            Transform boneTransform = animator.GetBoneTransform(bone);

            if (boneTransform != null && !boneList.Contains(boneTransform))
            {
                boneList.Add(boneTransform);
            }
        }

        boneRenderer.transforms = boneList.ToArray();
        Debug.Log($"Assigned {boneList.Count} humanoid bones to BoneRenderer.");
    }
}