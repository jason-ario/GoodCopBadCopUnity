using UnityEngine;

/// <summary>
/// Drives the X-ray arm swivel procedurally each frame.
///
/// Two responsibilities:
///   1. Rail sync — slides <see cref="_railSlider"/> along the X axis to match the monitor's
///      world X position, keeping Y and Z pinned to the rail.
///   2. CCD IK — runs a planar Cyclic Coordinate Descent solver on the bone chain
///      (IK 1 → IK 2 → IK 3 → IK 4) each frame so that IK pin tracks Ik pin target.
///
/// Place this component on the X Ray Machine root. Runs in LateUpdate so the monitor
/// is always in its final position for the frame before IK solves.
/// </summary>
public class XRaySwivelController : MonoBehaviour
{
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
    /// </summary>
    private void SolveCCD()
    {
        if (_ikChain == null || _ikChain.Length == 0 || _ikPin == null || _ikTarget == null) return;

        Vector3 normal = _planeNormal.normalized;

        for (int iter = 0; iter < _iterations; iter++)
        {
            if (Vector3.Distance(_ikPin.position, _ikTarget.position) < _tolerance)
                break;

            // Iterate from tip bone to root bone so earlier bones benefit from
            // the corrections already applied by bones closer to the end effector.
            for (int i = _ikChain.Length - 1; i >= 0; i--)
            {
                Transform bone = _ikChain[i];
                if (bone == null) continue;

                Vector3 toPin    = _ikPin.position    - bone.position;
                Vector3 toTarget = _ikTarget.position - bone.position;

                float angle = Vector3.SignedAngle(toPin, toTarget, normal);
                bone.Rotate(normal, angle, Space.World);
            }
        }
    }
}
