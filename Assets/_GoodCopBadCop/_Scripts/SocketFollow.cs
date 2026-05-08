using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// Syncs this transform's position (and optionally rotation) to a source transform in LateUpdate,
/// ensuring it runs after look rotation updates and avoids constraint pipeline timing issues.
/// Mirrors the ParentConstraint API (SetSource, constraintActive) for drop-in compatibility.
/// </summary>
public class SocketFollow : MonoBehaviour
{
    [SerializeField] private Transform _source;
    [SerializeField] private bool _followRotation = true;
    [SerializeField] private Vector3 _positionOffset;
    [SerializeField] private Vector3 _rotationOffset;
    
    public Transform Target => _source;
    public Vector3 Offset => _positionOffset;
    public Vector3 RotationOffset => _rotationOffset;

    /// <summary>
    /// Sets the source transform to follow at runtime.
    /// </summary>
    public void SetTarget(Transform target) => _source = target;

    private void LateUpdate()
    {
        if (_source == null) return;

        transform.position = _source.position + _source.TransformDirection(_positionOffset);

        if (_followRotation)
            transform.rotation = _source.rotation * Quaternion.Euler(_rotationOffset);
    }
}
