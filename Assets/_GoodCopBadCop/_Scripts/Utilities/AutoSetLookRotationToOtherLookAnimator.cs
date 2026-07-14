using FIMSpace.FLook;
using UnityEngine;

/// <summary>
/// Placed on the mutant neck GameObject (Untitled).
/// When this object becomes active:
///   - disables the root FLookAnimator (head look) so it no longer fights the neck
///   - inherits the current look target
/// When it deactivates, the root FLookAnimator is re-enabled for the base form.
/// ObjectToFollow is mirrored from the root every LateUpdate so any camera assignment
/// made by SuspectController.EnableLook() is always picked up, even if it fires after activation.
/// </summary>
public class AutoSetLookRotationToOtherLookAnimator : MonoBehaviour
{
    private FLookAnimator _neckLookAnimator;
    private FLookAnimator _rootLookAnimator;

    private void Awake()
    {
        _neckLookAnimator = GetComponent<FLookAnimator>();
        _rootLookAnimator = transform.root.GetComponent<FLookAnimator>();

        if (_neckLookAnimator == null)
            Debug.LogWarning($"[{nameof(AutoSetLookRotationToOtherLookAnimator)}] No FLookAnimator found on '{name}'.", this);
        if (_rootLookAnimator == null)
            Debug.LogWarning($"[{nameof(AutoSetLookRotationToOtherLookAnimator)}] No FLookAnimator found on root '{transform.root.name}'.", this);
    }

    private void OnEnable()
    {
        if (_rootLookAnimator == null || _neckLookAnimator == null) return;

        // Inherit whatever target the root was already tracking.
        _neckLookAnimator.SetLookTarget(_rootLookAnimator.ObjectToFollow);
        _rootLookAnimator.enabled = false;
    }

    private void OnDisable()
    {
        if (_rootLookAnimator != null)
            _rootLookAnimator.enabled = true;
    }

    // ObjectToFollow is set directly on the root's FLookAnimator by SuspectController.EnableLook()
    // even while the root component is disabled. Mirroring it here keeps the neck always in sync.
    private void LateUpdate()
    {
        if (_neckLookAnimator == null || _rootLookAnimator == null) return;

        Transform rootTarget = _rootLookAnimator.ObjectToFollow;
        if (_neckLookAnimator.ObjectToFollow != rootTarget)
            _neckLookAnimator.SetLookTarget(rootTarget);
    }
}
