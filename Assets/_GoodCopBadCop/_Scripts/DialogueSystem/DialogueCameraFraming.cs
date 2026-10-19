using UnityEngine;

/// <summary>
/// Optional per-speaker override for the outside-dialogue camera
/// (<see cref="PlayerMovementController.ZoomCameraForDialogue"/>). Put one or more on a speaker for
/// body shapes the default framing crops (e.g. Givi's long-necked mutant form): while
/// <see cref="LookTarget"/> is active in the hierarchy, the camera aims at it instead of the
/// humanoid head bone. Inactive targets are skipped, so an override placed on a form that's
/// currently hidden (e.g. a Mutated Version) only applies once that form is showing.
/// Read by <c>ScriptedDialogueRunner.EnterOutsideDialogueFacingSpeaker</c>.
/// </summary>
public class DialogueCameraFraming : MonoBehaviour
{
    [Tooltip("Transform the camera aims at (normally a head bone of the form this applies to).")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("How far below the target the camera aims, in meters. The default framing aims 0.35 m " +
             "below the head (at the chest). 0 = straight at the target.")]
    [SerializeField] private float _aimDropBelowTarget = 0f;

    [Tooltip("Raises (positive) or lowers (negative) the camera itself, in meters.")]
    [SerializeField] private float _cameraVerticalOffset = 0f;

    public Transform LookTarget => _lookTarget;
    public float AimDropBelowTarget => _aimDropBelowTarget;
    public float CameraVerticalOffset => _cameraVerticalOffset;

    /// <summary>True while the target exists and is visible (its form is the active one).</summary>
    public bool IsActive => isActiveAndEnabled && _lookTarget != null && _lookTarget.gameObject.activeInHierarchy;

    /// <summary>First active framing override on <paramref name="speakerRoot"/>, or null.</summary>
    public static DialogueCameraFraming FindActive(Transform speakerRoot)
    {
        if (speakerRoot == null) return null;
        foreach (DialogueCameraFraming framing in speakerRoot.GetComponentsInChildren<DialogueCameraFraming>())
            if (framing.IsActive) return framing;
        return null;
    }
}
