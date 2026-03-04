using UnityEngine;

public class NameTag : MonoBehaviour
{
    [Tooltip("The transform to follow (e.g. head bone of the character)")]
    public Transform target;

    [Tooltip("Optional offset from the target position (e.g. to float above the head)")]
    public Vector3 offset = new Vector3(0f, 0.2f, 0f);

    [SerializeField] Camera _camera;

    void LateUpdate()
    {
        if (target == null || _camera == null) return;

        // Follow the target's position with an optional offset
        transform.position = target.position + offset;

        // Face the camera: copy the camera's rotation so the tag always looks at the viewer
        transform.rotation = _camera.transform.rotation;
    }
}
