using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// Syncs this transform's position (and optionally rotation) to a source transform in LateUpdate.
/// Runs at execution order 2 — after PlayerPickupController (order 1) has moved the folder to the
/// body-arm target — so folder documents always match the folder's final pitched position.
/// Mirrors the ParentConstraint API (SetSource, constraintActive) for drop-in compatibility.
/// </summary>
[DefaultExecutionOrder(2)]
public class SocketFollow : MonoBehaviour
{
    [SerializeField] private Transform _source;
    [SerializeField] private bool _followRotation = true;
    [SerializeField] private Vector3 _positionOffset;
    [SerializeField] private Vector3 _rotationOffset;

    // When set, overrides _rotationOffset and is applied as a quaternion local rotation.
    // Used by ExamNotebook to pass exact anchor local rotations without Euler precision loss.
    private bool _useLocalRotationOverride;
    private Vector3    _localPositionOverride;
    private Quaternion _localRotationOverride;

    public Transform Target => _source;
    public Vector3 Offset => _positionOffset;
    public Vector3 RotationOffset => _rotationOffset;

    /// <summary>
    /// Sets the source transform to follow at runtime.
    /// Clears any local-offset override set via <see cref="SetTargetWithLocalOffset"/>.
    /// </summary>
    public void SetTarget(Transform target)
    {
        _source = target;
        _useLocalRotationOverride = false;
    }

    /// <summary>
    /// Sets the source transform and a fixed local-space offset relative to that source.
    /// Position is computed via <c>source.TransformPoint(localPosition)</c> and rotation via
    /// <c>source.rotation * localRotation</c> — bypassing child-hierarchy dirty-flag evaluation
    /// so the result always reflects the source's definitive per-frame transform.
    /// </summary>
    /// <param name="source">Transform to follow (e.g. the notebook root).</param>
    /// <param name="localPosition">Offset position in source local space.</param>
    /// <param name="localRotation">Offset rotation in source local space.</param>
    public void SetTargetWithLocalOffset(Transform source, Vector3 localPosition, Quaternion localRotation)
    {
        _source                   = source;
        _localPositionOverride    = localPosition;
        _localRotationOverride    = localRotation;
        _useLocalRotationOverride = true;
    }

    private void LateUpdate()
    {
        if (_source == null) return;

        if (_useLocalRotationOverride)
        {
            // Derive world transform directly from the source's live position and rotation
            // plus the pre-baked local offsets. Avoids reading child-transform world positions
            // through Unity's lazy hierarchy evaluation on potentially inactive GameObjects.
            transform.position = _source.TransformPoint(_localPositionOverride);

            if (_followRotation)
                transform.rotation = _source.rotation * _localRotationOverride;
        }
        else
        {
            transform.position = _source.position + _source.TransformDirection(_positionOffset);

            if (_followRotation)
                transform.rotation = _source.rotation * Quaternion.Euler(_rotationOffset);
        }
    }
}
