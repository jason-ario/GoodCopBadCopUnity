using UnityEngine;

[ExecuteAlways]
public class SimpleLegIK_CCD : MonoBehaviour
{
    [Header("Bones")]
    public Transform hip;
    public Transform knee;
    public Transform ankle;

    [Header("IK Target")]
    public Transform target;

    [Header("Optional Pole")]
    public Transform pole;

    [Header("Settings")]
    [Range(1, 64)] public int iterations = 20;
    public float tolerance = 0.0001f;

    [Range(0f, 1f)] public float rotationWeight = 1f;

    [Header("Optional")]
    public bool lockHipTwistY = true; // since yours twists on Y

    float upperLength;
    float lowerLength;
    float maxReach;

    void OnEnable()
    {
        CacheLengths();
    }

    void CacheLengths()
    {
        if (!hip || !knee || !ankle) return;

        upperLength = Vector3.Distance(hip.position, knee.position);
        lowerLength = Vector3.Distance(knee.position, ankle.position);
        maxReach = upperLength + lowerLength;
    }

    void LateUpdate()
    {
        if (!hip || !knee || !ankle || !target)
            return;

        Solve();
    }

    void Solve()
    {
        Vector3 desiredTarget = target.position;

        // ---- Clamp Reach (important fix) ----
        Vector3 toTarget = desiredTarget - hip.position;
        float dist = toTarget.magnitude;

        if (dist > maxReach)
        {
            desiredTarget = hip.position + toTarget.normalized * (maxReach * 0.999f);
        }

        // ---- CCD Position Solve ----
        for (int i = 0; i < iterations; i++)
        {
            RotateJointToward(knee, ankle, desiredTarget);
            RotateJointToward(hip, ankle, desiredTarget);

            if ((ankle.position - desiredTarget).sqrMagnitude <= tolerance * tolerance)
                break;
        }

        // ---- Pole ----
        if (pole != null)
            AlignKneeToPole();

        // ---- Optional Hip Twist Lock (light touch, after solve) ----
        if (lockHipTwistY)
            LockHipTwistY();

        // ---- Match Foot Rotation ----
        if (rotationWeight > 0f)
        {
            Quaternion desiredRot = target.rotation;

            if (rotationWeight < 1f)
                desiredRot = Quaternion.Slerp(ankle.rotation, target.rotation, rotationWeight);

            ankle.rotation = desiredRot;
        }
    }

    void RotateJointToward(Transform joint, Transform endEffector, Vector3 targetPos)
    {
        Vector3 toEff = endEffector.position - joint.position;
        Vector3 toTarget = targetPos - joint.position;

        if (toEff.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f)
            return;

        Quaternion delta = Quaternion.FromToRotation(toEff, toTarget);
        joint.rotation = delta * joint.rotation;
    }

    void LockHipTwistY()
    {
        Vector3 euler = hip.localEulerAngles;

        euler.y = 0f;

        hip.localRotation = Quaternion.Euler(euler);
    }

    void AlignKneeToPole()
    {
        Vector3 rootPos = hip.position;
        Vector3 endPos = ankle.position;

        Vector3 rootToEnd = endPos - rootPos;
        Vector3 rootToKnee = knee.position - rootPos;
        Vector3 rootToPole = pole.position - rootPos;

        Vector3 projectedPole = Vector3.ProjectOnPlane(rootToPole, rootToEnd);
        Vector3 projectedKnee = Vector3.ProjectOnPlane(rootToKnee, rootToEnd);

        float angle = Vector3.SignedAngle(projectedKnee, projectedPole, rootToEnd);

        hip.Rotate(rootToEnd.normalized, angle, Space.World);
    }
}