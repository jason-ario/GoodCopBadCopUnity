using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IKGizmo))]
public class IKGizmoEditor : Editor
{
    private const float PickingRadiusMultiplier = 1.4f;

    private void OnSceneGUI()
    {
        IKGizmo ikGizmo = (IKGizmo)target;
        Transform t = ikGizmo.transform;

        Handles.color = ikGizmo.gizmoColor;

        bool clicked = Handles.Button(
            t.position,
            t.rotation,
            ikGizmo.sphereRadius * PickingRadiusMultiplier,
            ikGizmo.sphereRadius * PickingRadiusMultiplier,
            Handles.SphereHandleCap
        );

        if (clicked)
        {
            Selection.activeGameObject = ikGizmo.gameObject;
        }

        if (ikGizmo.showAxes)
        {
            float len = ikGizmo.axisLength;

            Handles.color = Color.red;
            Handles.DrawLine(t.position, t.position + t.right * len);

            Handles.color = Color.green;
            Handles.DrawLine(t.position, t.position + t.up * len);

            Handles.color = Color.blue;
            Handles.DrawLine(t.position, t.position + t.forward * len);
        }
    }
}
