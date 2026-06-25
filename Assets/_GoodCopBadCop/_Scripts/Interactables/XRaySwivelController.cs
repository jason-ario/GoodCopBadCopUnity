using UnityEngine;

/// <summary>
/// Drives the X-ray arm swivel procedurally each frame.
///
/// Two responsibilities:
///   1. Rail sync — slides <see cref="_railSlider"/> along the X axis to match the monitor's
///      world X position, keeping Y and Z pinned to the rail.
///   2. CCD IK — runs a planar Cyclic Coordinate Descent solver on the bone chain
///      (IK 1 → IK 2 → IK 3 → IK 4) each frame so that IK pin tracks Ik pin target.
///      Per-bone angle limits clamp local Z rotation after each CCD step.
///
/// Place this component on the X Ray Machine root. Runs in LateUpdate so the monitor
/// is always in its final position for the frame before IK solves.
/// </summary>
public class XRaySwivelController : MonoBehaviour
{
    /// <summary>
    /// Optional rotation constraint for a single bone in the IK chain.
    /// Angles are in local Z euler degrees, normalized to [-180, 180].
    /// </summary>
    [System.Serializable]
    public struct JointLimit
    {
        [Tooltip("Enable the angle constraint for this bone.")]
        public bool enabled;

        [Tooltip("Minimum allowed local Z rotation in degrees (e.g. -45).")]
        public float minAngle;

        [Tooltip("Maximum allowed local Z rotation in degrees (e.g. 45).")]
        public float maxAngle;
    }

    [Header("Rail Slider")]
    [Tooltip("The transform that slides along the horizontal rail to track the monitor's X position.")]
    [SerializeField] private Transform _railSlider;

    [Tooltip("World-space X offset applied on top of the monitor's X position. Positive = right, negative = left.")]
    [SerializeField] private float _railSliderXOffset = 0f;

    [Header("Monitor")]
    [Tooltip("The monitor transform driven by XRayJoystickController.")]
    [SerializeField] private Transform _monitorTransform;

    [Header("IK Chain")]
    [Tooltip("Bones in order from root to tip: IK 1, IK 2, IK 3, IK 4. Exclude IK Base and IK pin.")]
    [SerializeField] private Transform[] _ikChain;

    [Tooltip("End effector of the IK chain (IK pin).")]
    [SerializeField] private Transform _ikPin;

    [Tooltip("Target the end effector must reach (Ik pin target, child of the monitor).")]
    [SerializeField] private Transform _ikTarget;

    [Header("CCD Settings")]
    [Tooltip("CCD iterations per frame. Higher values are more accurate; 10 is sufficient for a 4-bone chain.")]
    [SerializeField] private int _iterations = 10;

    [Tooltip("Early-exit distance in metres. CCD stops when IK pin is within this distance of the target.")]
    [SerializeField] private float _tolerance = 0.001f;

    [Tooltip("World-space axis the arm rotates around. Vector3.forward (0,0,1) for an arm operating in the XY plane.")]
    [SerializeField] private Vector3 _planeNormal = Vector3.forward;

    [Header("Joint Limits")]
    [Tooltip("Per-bone rotation constraints, indexed to match IK Chain. Leave an entry disabled to apply no limit to that bone.")]
    [SerializeField] private JointLimit[] _jointLimits;

    private void LateUpdate()
    {
        if (_monitorTransform == null) return;

        SyncRailSliders();
        SolveCCD();
    }

    /// <summary>
    /// Moves <see cref="_railSlider"/> to the monitor's world X position,
    /// keeping Y and Z pinned to the rail.
    /// </summary>
    private void SyncRailSliders()
    {
        if (_railSlider == null) return;

        Vector3 p = _railSlider.position;
        p.x = _monitorTransform.position.x + _railSliderXOffset;
        _railSlider.position = p;
    }

    /// <summary>
    /// Planar CCD IK solver. Iterates from the tip of <see cref="_ikChain"/> back to the root,
    /// rotating each bone around <see cref="_planeNormal"/> until <see cref="_ikPin"/> reaches
    /// <see cref="_ikTarget"/> within <see cref="_tolerance"/>.
    /// After each per-bone rotation, applies the corresponding <see cref="JointLimit"/> if enabled.
    /// </summary>
    private void SolveCCD()
    {
        if (_ikChain == null || _ikChain.Length == 0 || _ikPin == null || _ikTarget == null) return;

        Vector3 normal = _planeNormal.normalized;

        for (int iter = 0; iter < _iterations; iter++)
        {
            if (Vector3.Distance(_ikPin.position, _ikTarget.position) < _tolerance)
                break;

            for (int i = _ikChain.Length - 1; i >= 0; i--)
            {
                Transform bone = _ikChain[i];
                if (bone == null) continue;

                Vector3 toPin    = _ikPin.position    - bone.position;
                Vector3 toTarget = _ikTarget.position - bone.position;

                float angle = Vector3.SignedAngle(toPin, toTarget, normal);
                bone.Rotate(normal, angle, Space.World);

                ApplyJointLimit(bone, i);
            }
        }
    }

    /// <summary>
    /// Clamps the local Z euler rotation of <paramref name="bone"/> to the limits defined
    /// at <paramref name="chainIndex"/> in <see cref="_jointLimits"/>, if that entry is enabled.
    /// Angles are normalised to [-180, 180] before clamping.
    /// </summary>
    private void ApplyJointLimit(Transform bone, int chainIndex)
    {
        if (_jointLimits == null || chainIndex >= _jointLimits.Length) return;

        JointLimit limit = _jointLimits[chainIndex];
        if (!limit.enabled) return;

        Vector3 localEuler = bone.localEulerAngles;
        localEuler.z = Mathf.Clamp(NormalizeAngle(localEuler.z), limit.minAngle, limit.maxAngle);
        bone.localEulerAngles = localEuler;
    }

    /// <summary>
    /// Remaps a Unity euler angle from the [0, 360] range to [-180, 180]
    /// so it can be compared against signed min/max limits.
    /// </summary>
    private static float NormalizeAngle(float angle)
    {
        while (angle >  180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
