using UnityEngine;

/// <summary>
/// Optional per-speaker override for the outside-dialogue camera
/// (<see cref="PlayerMovementController.ZoomCameraForDialogue"/>). Put one on a speaker for body
/// shapes the default framing crops (e.g. Givi's long-necked mutant form). It only applies while
/// this GameObject is active in the hierarchy, so an override placed on a form that's currently
/// hidden (e.g. a Mutated Version) only kicks in once that form is showing.
///
/// Aim point: with <see cref="_aimAtMeshTop"/> the camera aims at the top of this object's visible
/// renderers (the head of a tall mesh, independent of how the rig's head bone is mapped), minus
/// <see cref="_aimDropBelowTarget"/>. Otherwise it aims at <see cref="_lookTarget"/> minus the drop.
/// Read by <c>ScriptedDialogueRunner.EnterOutsideDialogueFacingSpeaker</c>.
/// </summary>
public class DialogueCameraFraming : MonoBehaviour
{
    [Tooltip("Aim at the top of this object's visible renderers (recommended for tall/odd silhouettes).")]
    [SerializeField] private bool _aimAtMeshTop = true;

    [Tooltip("Used when 'Aim At Mesh Top' is off: transform the camera aims at (e.g. a head bone).")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("How far below the aim point the camera aims, in meters. With 'Aim At Mesh Top', ~0.2 " +
             "centres the head. Negative aims above it.")]
    [SerializeField] private float _aimDropBelowTarget = 0.2f;

    [Tooltip("Raises (positive) or lowers (negative) the camera itself, in meters.")]
    [SerializeField] private float _cameraVerticalOffset = 0f;

    [Tooltip("Horizontal camera distance from the speaker, in meters. When > 0 the camera also steps " +
             "back to reach it if the player is standing closer, which flattens the upward angle on " +
             "tall speakers. <= 0 uses the player's default dialogue distance (dolly-in only).")]
    [SerializeField] private float _standoffDistance = -1f;

    private Transform _aimPoint;

    public float AimDropBelowTarget => _aimDropBelowTarget;
    public float CameraVerticalOffset => _cameraVerticalOffset;
    public float StandoffDistance => _standoffDistance;

    /// <summary>True while this override's form is showing and it can produce an aim point.</summary>
    public bool IsActive => isActiveAndEnabled &&
        (_aimAtMeshTop || (_lookTarget != null && _lookTarget.gameObject.activeInHierarchy));

    /// <summary>
    /// The transform the camera should aim at right now. In mesh-top mode a helper child is moved to
    /// the top-centre of the visible renderer bounds (computed at call time, so it follows the pose).
    /// </summary>
    public Transform ResolveAimTarget()
    {
        if (!_aimAtMeshTop) return _lookTarget;

        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!hasBounds) return _lookTarget != null ? _lookTarget : transform;

        if (_aimPoint == null)
        {
            _aimPoint = new GameObject("Dialogue Aim Point").transform;
            _aimPoint.SetParent(transform, false);
        }

        _aimPoint.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        return _aimPoint;
    }

    /// <summary>First active framing override on <paramref name="speakerRoot"/>, or null.</summary>
    public static DialogueCameraFraming FindActive(Transform speakerRoot)
    {
        if (speakerRoot == null) return null;
        foreach (DialogueCameraFraming framing in speakerRoot.GetComponentsInChildren<DialogueCameraFraming>())
            if (framing.IsActive) return framing;
        return null;
    }
}
