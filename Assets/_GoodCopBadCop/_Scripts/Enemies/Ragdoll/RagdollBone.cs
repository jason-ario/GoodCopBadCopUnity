using UnityEngine;

/// <summary>
/// Marker component placed on every bone that the Ragdoll Builder tool turned into
/// a physics ragdoll part (Rigidbody + Collider + optional CharacterJoint).
/// Used purely so the "Remove Ragdoll" tool can find and cleanly strip exactly the
/// components it added, without touching any gameplay colliders/rigidbodies that
/// were already on the character for other reasons.
/// </summary>
[DisallowMultipleComponent]
public class RagdollBone : MonoBehaviour
{
    /// <summary>True for the single root bone of the ragdoll (e.g. hips/pelvis) which
    /// has a Rigidbody + Collider but no CharacterJoint back to a parent.</summary>
    public bool isRoot;
}
