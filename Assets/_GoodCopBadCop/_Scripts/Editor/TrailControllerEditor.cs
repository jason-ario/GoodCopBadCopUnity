using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom scene-view editor for TrailController.
/// Draws a clickable, labelled sphere handle at each waypoint so designers can
/// identify and select individual waypoints directly in the scene view.
/// </summary>
[CustomEditor(typeof(TrailController))]
public class TrailControllerEditor : Editor
{
    private const float HandleSizeMultiplier  = 0.14f;
    private const float PickSizeMultiplier    = 0.20f;
    private const float LabelVerticalOffset   = 0.28f;

    private static GUIStyle _labelStyle;
    private static GUIStyle LabelStyle
    {
        get
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle
                {
                    fontSize  = 11,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = Color.white }
                };
            }
            return _labelStyle;
        }
    }

    private void OnSceneGUI()
    {
        TrailController controller = (TrailController)target;
        var waypoints = controller.Waypoints;

        if (waypoints == null || waypoints.Count == 0) return;

        Handles.color = controller.WaypointColor;

        for (int i = 0; i < waypoints.Count; i++)
        {
            Transform wp = waypoints[i];
            if (wp == null) continue;

            float handleSize = HandleUtility.GetHandleSize(wp.position) * HandleSizeMultiplier;
            float pickSize   = HandleUtility.GetHandleSize(wp.position) * PickSizeMultiplier;

            bool clicked = Handles.Button(
                wp.position,
                Quaternion.identity,
                handleSize,
                pickSize,
                Handles.SphereHandleCap
            );

            if (clicked)
                Selection.activeGameObject = wp.gameObject;

            // Index label above the sphere.
            Vector3 labelPos = wp.position + Vector3.up * (handleSize + LabelVerticalOffset);
            Handles.Label(labelPos, $"[{i}]", LabelStyle);
        }
    }
}
