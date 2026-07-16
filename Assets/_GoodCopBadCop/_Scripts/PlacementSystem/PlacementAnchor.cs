using UnityEngine;

/// <summary>
/// Marks the point on a pickup that is aligned to a placement surface.
/// Position this child visually in the prefab; the placer uses it only when present.
/// </summary>
[AddComponentMenu("Good Cop Bad Cop/Placement Anchor")]
public class PlacementAnchor : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.035f);
        Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.2f);
    }
}