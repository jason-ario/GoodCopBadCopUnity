using UnityEngine;

[ExecuteAlways]
public class HumanoidFingerController : MonoBehaviour
{
    public Animator animator;

    [Header("Left Hand")]
    [Range(0, 1)] public float leftFist;
    [Range(0, 1)] public float leftIndex;
    [Range(0, 1)] public float leftMiddle;
    [Range(0, 1)] public float leftRing;
    [Range(0, 1)] public float leftLittle;

    [Header("Right Hand")]
    [Range(0, 1)] public float rightFist;
    [Range(0, 1)] public float rightIndex;
    [Range(0, 1)] public float rightMiddle;
    [Range(0, 1)] public float rightRing;
    [Range(0, 1)] public float rightLittle;

    FingerSet leftHand;
    FingerSet rightHand;

    void OnEnable()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>();

        if (animator && animator.isHuman)
        {
            leftHand = new FingerSet(animator, true);
            rightHand = new FingerSet(animator, false);
        }
    }

    void LateUpdate()
    {
        if (!animator || !animator.isHuman)
            return;

        ApplyHand(leftHand, leftFist, leftIndex, leftMiddle, leftRing, leftLittle);
        ApplyHand(rightHand, rightFist, rightIndex, rightMiddle, rightRing, rightLittle);
    }

    void ApplyHand(FingerSet hand, float fist, float index, float middle, float ring, float little)
    {
        if (hand == null) return;

        hand.index.Apply(Mathf.Clamp01(index + fist));
        hand.middle.Apply(Mathf.Clamp01(middle + fist));
        hand.ring.Apply(Mathf.Clamp01(ring + fist));
        hand.little.Apply(Mathf.Clamp01(little + fist));
    }

#if UNITY_EDITOR
    [ContextMenu("Recalibrate Base Pose")]
    void RecalibrateBase()
    {
        leftHand?.Recalibrate();
        rightHand?.Recalibrate();
    }

    [ContextMenu("Reset To Original Base Pose")]
    void ResetToOriginalBase()
    {
        leftHand?.ResetToOriginal();
        rightHand?.ResetToOriginal();
    }
#endif
}


class FingerSet
{
    public Finger index;
    public Finger middle;
    public Finger ring;
    public Finger little;

    public FingerSet(Animator animator, bool left)
    {
        index = new Finger(animator,
            left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
            left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate,
            left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);

        middle = new Finger(animator,
            left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
            left ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate,
            left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);

        ring = new Finger(animator,
            left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
            left ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate,
            left ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal);

        little = new Finger(animator,
            left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
            left ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate,
            left ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal);
    }

    public void Recalibrate()
    {
        index?.CaptureBasePose();
        middle?.CaptureBasePose();
        ring?.CaptureBasePose();
        little?.CaptureBasePose();
    }

    public void ResetToOriginal()
    {
        index?.ResetToOriginal();
        middle?.ResetToOriginal();
        ring?.ResetToOriginal();
        little?.ResetToOriginal();
    }
}


class Finger
{
    Transform p, i, d;

    Quaternion pBase, iBase, dBase;
    Quaternion pOriginal, iOriginal, dOriginal;

    Vector3 bendAxis = Vector3.right; // humanoid fingers usually bend on local X
    float maxAngle = 90f;

    public Finger(Animator animator,
                  HumanBodyBones pBone,
                  HumanBodyBones iBone,
                  HumanBodyBones dBone)
    {
        p = animator.GetBoneTransform(pBone);
        i = animator.GetBoneTransform(iBone);
        d = animator.GetBoneTransform(dBone);

        if (!p) return;

        // Original pose (first time component initialized)
        pOriginal = p.localRotation;
        iOriginal = i.localRotation;
        dOriginal = d.localRotation;

        // Working base (used for curl math)
        CaptureBasePose();
    }

    public void CaptureBasePose()
    {
        if (!p) return;

        pBase = p.localRotation;
        iBase = i.localRotation;
        dBase = d.localRotation;
    }

    public void ResetToOriginal()
    {
        if (!p) return;
    
        // Restore original calibration pose
        p.localRotation = pOriginal;
        i.localRotation = iOriginal;
        d.localRotation = dOriginal;
    
    #if UNITY_EDITOR
        // Clear prefab overrides so Unity doesn't re-apply them
        RevertPrefabOverride(p);
        RevertPrefabOverride(i);
        RevertPrefabOverride(d);
    #endif
    
        // Reset working base
        CaptureBasePose();
    }
    
#if UNITY_EDITOR
    void RevertPrefabOverride(Transform t)
    {
        if (t == null) return;

        var prefabInstance = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(t);
        if (prefabInstance != null)
        {
            UnityEditor.PrefabUtility.RevertObjectOverride(
                t,
                UnityEditor.InteractionMode.AutomatedAction
            );
        }
    }
#endif

    public void Apply(float t)
    {
        if (!p) return;

        float angle = Mathf.Lerp(0f, maxAngle, t);
        Quaternion offset = Quaternion.AngleAxis(angle, bendAxis);

        p.localRotation = pBase * offset;
        i.localRotation = iBase * offset;
        d.localRotation = dBase * offset;
    }
}
