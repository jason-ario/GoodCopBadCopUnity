using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.BeamEditor
{
    public class EditorUtils
    {
        private static readonly List<string> s_MenuLabels = new List<string>()
        {
            "Documentation",
            "Support",
            "Poseidon",
            "Jupiter",
            "Contour"
        };

        private static readonly List<string> s_MenuLinks = new List<string>()
        {
            "https://docs.pinwheelstud.io/beam",
            "https://discord.gg/nYSB7SNafe",
            "https://assetstore.unity.com/packages/vfx/shaders/low-poly-water-poseidon-153826?aid=1100l3QbW&pubref=beam-editor",
            "https://assetstore.unity.com/packages/2d/textures-materials/sky/procedural-sky-shader-day-night-cycle-jupiter-159992?aid=1100l3QbW&pubref=beam-editor",
            "https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/contour-edge-detection-outline-post-effect-urp-render-graph-302915?aid=1100l3QbW&pubref=beam-editor"
        };

        private static GUIStyle s_MenuStyle;
        private static GUIStyle GetMenuStyle()
        {
            if (s_MenuStyle==null)
            {
                s_MenuStyle = new GUIStyle(EditorStyles.miniLabel);
            }
            s_MenuStyle.normal.textColor = new Color32(125, 170, 240, 255);
            return s_MenuStyle;
        }

        public static void DrawExternalLinks()
        {
            Rect r = EditorGUILayout.GetControlRect();
            var rects = EditorGUIUtility.GetFlowLayoutedRects(r, GetMenuStyle(), 4, 0, s_MenuLabels);

            for (int i = 0; i < rects.Count; ++i)
            {
                EditorGUIUtility.AddCursorRect(rects[i], MouseCursor.Link);
                if (GUI.Button(rects[i], s_MenuLabels[i], GetMenuStyle()))
                {
                    Application.OpenURL(s_MenuLinks[i]);
                }
            }
        }
    }
}