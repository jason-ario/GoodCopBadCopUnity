using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PipeSystem
{
    [SelectionBase]
    public class PipeSocket : MonoBehaviour
    {
        [Header("Socket Settings")]
        public PipeDiameter category = PipeDiameter.Mid_52;
        [Tooltip("Fine adjustment along forward vector to hide seams or support overlapping joints.")]
        public float insertionOffset = 0f;

        [Header("Connection State")]
        public PipeSocket connectedTo;

        // Computed property per Section 3 of the spec
        public bool IsOccupied => connectedTo != null;

        public void Connect(PipeSocket other)
        {
            connectedTo = other;
            other.connectedTo = this;
        }

        public void Disconnect()
        {
            if (connectedTo != null)
            {
                connectedTo.connectedTo = null;
                connectedTo = null;
            }
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (IsOccupied) return;

            Gizmos.color = GetSocketColor();
            Gizmos.matrix = transform.localToWorldMatrix;
            
            // Draw disc at socket origin
            Handles.color = GetSocketColor();
            Handles.DrawWireDisc(transform.position, transform.forward, GetRadius());

            // Draw outward direction arrow
            Gizmos.DrawRay(Vector3.zero, Vector3.forward * 0.3f);
            Handles.ArrowHandleCap(0, transform.position, transform.rotation, 0.3f, EventType.Repaint);
#endif
        }

        private void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, GetRadius() * 1.2f);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * (0.3f + insertionOffset));
#endif
        }

        private float GetRadius()
        {
            return category switch
            {
                PipeDiameter.Small_36 => 0.18f,
                PipeDiameter.MidSmall_44 => 0.22f,
                PipeDiameter.Mid_52 => 0.26f,
                PipeDiameter.Large_62 => 0.31f,
                _ => 0.25f
            };
        }

        private Color GetSocketColor()
        {
            return category switch
            {
                PipeDiameter.Small_36 => Color.cyan,
                PipeDiameter.MidSmall_44 => Color.green,
                PipeDiameter.Mid_52 => Color.yellow,
                PipeDiameter.Large_62 => new Color(1f, 0.5f, 0f), // Orange
                _ => Color.white
            };
        }
    }
}