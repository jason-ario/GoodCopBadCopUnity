using UnityEngine;

[ExecuteAlways]
public class HumanoidFingerController : MonoBehaviour
{
    public Animator animator;

    [Header("Left Hand")]
    [Range(0, 1)] public float leftFist;
    [Range(0, 1)] public float leftThumb;
    [Range(0, 1)] public float leftIndex;
    [Range(0, 1)] public float leftMiddle;
    [Range(0, 1)] public float leftRing;
    [Range(0, 1)] public float leftLittle;

    [Header("Right Hand")]
    [Range(0, 1)] public float rightFist;
    [Range(0, 1)] public float rightThumb;
    [Range(0, 1)] public float rightIndex;
    [Range(0, 1)] public float rightMiddle;
    [Range(0, 1)] public float rightRing;
    [Range(0, 1)] public float rightLittle;

    private FingerSet leftHand;
    private FingerSet rightHand;

    void OnEnable()
    {
        if (!animator) animator = GetComponent<Animator>();
        Setup();
    }

    void Update()
    {
        ApplyHand(leftHand, leftFist, leftThumb, leftIndex, leftMiddle, leftRing, leftLittle);
        ApplyHand(rightHand, rightFist, rightThumb, rightIndex, rightMiddle, rightRing, rightLittle);
    }

    void Setup()
    {
        if (!animator || !animator.isHuman) return;

        leftHand = CreateFingerSet(true);
        rightHand = CreateFingerSet(false);
    }

    FingerSet CreateFingerSet(bool left)
    {
        HumanBodyBones prefix = left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;

        return new FingerSet(animator, left);
    }

    void ApplyHand(FingerSet hand, float fist, float thumb, float index, float middle, float ring, float little)
    {
        if (hand == null) return;

        hand.ApplyFinger(hand.thumb, thumb + fist);
        hand.ApplyFinger(hand.index, index + fist);
        hand.ApplyFinger(hand.middle, middle + fist);
        hand.ApplyFinger(hand.ring, ring + fist);
        hand.ApplyFinger(hand.little, little + fist);
    }
}

class FingerSet
{
    public Finger thumb;
    public Finger index;
    public Finger middle;
    public Finger ring;
    public Finger little;

    public FingerSet(Animator animator, bool left)
    {
        if (!animator.isHuman) return;

        thumb = new Finger(
            animator,
            left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal,
            left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate,
            left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal,
            true    // invert for thumb
        );

        index  = new Finger(animator,
            left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
            left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate,
            left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);

        middle = new Finger(animator,
            left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
            left ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate,
            left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);

        ring   = new Finger(animator,
            left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
            left ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate,
            left ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal);

        little = new Finger(animator,
            left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
            left ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate,
            left ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal);
    }

    public void ApplyFinger(Finger finger, float amount)
    {
        finger?.Apply(amount);
    }
}

class Finger
{
    Transform p;
    Transform i;
    Transform d;

    Quaternion pOpen;
    Quaternion iOpen;
    Quaternion dOpen;

    Quaternion pClosed;
    Quaternion iClosed;
    Quaternion dClosed;

    bool initialized = false;

    public Finger(
        Animator animator,
        HumanBodyBones pBone,
        HumanBodyBones iBone,
        HumanBodyBones dBone,
        bool invertCurl = false)
    {
        p = animator.GetBoneTransform(pBone);
        i = animator.GetBoneTransform(iBone);
        d = animator.GetBoneTransform(dBone);

        if (!p || !i || !d) return;

        // Store open pose
        pOpen = p.localRotation;
        iOpen = i.localRotation;
        dOpen = d.localRotation;

        // Create closed pose (deterministic)
        float curl = invertCurl ? -90f : 90f;

        pClosed = pOpen * Quaternion.Euler(curl, 0, 0);
        iClosed = iOpen * Quaternion.Euler(curl * 1.2f, 0, 0);
        dClosed = dOpen * Quaternion.Euler(curl * 1.3f, 0, 0);

        initialized = true;
    }

    public void Apply(float t)
    {
        if (!initialized) return;

        p.localRotation = Quaternion.Slerp(pOpen, pClosed, t);
        i.localRotation = Quaternion.Slerp(iOpen, iClosed, t);
        d.localRotation = Quaternion.Slerp(dOpen, dClosed, t);
    }
}

