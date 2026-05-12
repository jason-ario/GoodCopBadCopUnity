using UnityEngine;

[ExecuteAlways]
public class IKGizmo : MonoBehaviour
{
    [Header("Gizmo Settings")]
    public float sphereRadius = 0.05f;
    public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);
    public bool showAxes = true;
    public float axisLength = 0.1f;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, sphereRadius);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, gizmoColor.a * 0.3f);
        Gizmos.DrawWireSphere(transform.position, sphereRadius * 1.4f);

        if (showAxes)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, transform.right * axisLength);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, transform.up * axisLength);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, transform.forward * axisLength);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, sphereRadius * 1.6f);
    }
#endif
}
