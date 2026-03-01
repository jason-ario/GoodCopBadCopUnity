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

    void Update()
    {
        if (!hip || !knee || !ankle || !target) return;
        Solve();
    }

    void Solve()
    {
        // --- CCD Position ---
        for (int i = 0; i < iterations; i++)
        {
            RotateJointToward(knee, ankle, target.position);
            RotateJointToward(hip, ankle, target.position);

            if ((ankle.position - target.position).sqrMagnitude <= tolerance * tolerance)
                break;
        }

        // --- Pole Alignment ---
        if (pole != null)
            AlignKneeToPole();

        // --- Rotation Matching ---
        if (rotationWeight > 0f)
        {
            Quaternion desired = target.rotation;

            if (rotationWeight < 1f)
                desired = Quaternion.Slerp(ankle.rotation, target.rotation, rotationWeight);

            ankle.rotation = desired;
        }
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

    static void RotateJointToward(Transform joint, Transform endEffector, Vector3 targetPos)
    {
        Vector3 toEff = endEffector.position - joint.position;
        Vector3 toTarget = targetPos - joint.position;

        if (toEff.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f)
            return;

        Quaternion delta = Quaternion.FromToRotation(toEff, toTarget);
        joint.rotation = delta * joint.rotation;
    }
}
