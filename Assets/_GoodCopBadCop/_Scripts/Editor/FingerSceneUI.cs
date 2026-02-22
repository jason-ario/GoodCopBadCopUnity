using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class FingerSceneUI
{
    static FingerSceneUI()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        GameObject selected = Selection.activeGameObject;
        if (!selected) return;

        HumanoidFingerController controller =
            selected.GetComponentInParent<HumanoidFingerController>();

        if (!controller) return;

        Handles.BeginGUI();

        float sliderWidth = 22f;
        float spacing = 6f;
        float fistSpacing = 12f;
        float sideMargin = 20f;

        float singleHandWidth =
            (sliderWidth * 6) +      // 5 fingers + fist
            (spacing * 4) +
            fistSpacing;

        float panelWidth = (singleHandWidth * 2) + 60f;
        float panelHeight = 260f;

        // Clamp panel so it never exits Scene view bounds
        float xPos = Mathf.Clamp(
            sceneView.position.width - panelWidth - sideMargin,
            10f,
            sceneView.position.width - panelWidth - 10f
        );

        Rect area = new Rect(
            xPos,
            40,
            panelWidth,
            panelHeight
        );

        GUILayout.BeginArea(area, GUI.skin.box);

        GUILayout.Label("HAND CONTROLS", EditorStyles.boldLabel);
        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        
        GUILayout.BeginHorizontal();
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        DrawHand(
            "Left",
            ref controller.leftIndex,
            ref controller.leftMiddle,
            ref controller.leftRing,
            ref controller.leftLittle,
            ref controller.leftFist,
            false
        );

        GUILayout.Space(30);

        DrawHand(
            "Right",
            ref controller.rightIndex,
            ref controller.rightMiddle,
            ref controller.rightRing,
            ref controller.rightLittle,
            ref controller.rightFist,
            true
        );

        GUILayout.EndHorizontal();

        GUILayout.EndArea();

        Handles.EndGUI();

        if (GUI.changed)
            EditorUtility.SetDirty(controller);
    }

    static void DrawHand(
        string label,
        ref float index,
        ref float middle,
        ref float ring,
        ref float little,
        ref float fist,
        bool isRightHand)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(label + " Hand", EditorStyles.boldLabel);
        GUILayout.Space(5);

        Rect handRect = GUILayoutUtility.GetRect(200, 180);

        float baseY = handRect.yMax;
        float sliderWidth = 22f;
        float spacing = 6f;

        float thumbHeight = 60f;
        float littleHeight = 90f;
        float ringHeight = 110f;
        float indexHeight = 130f;
        float middleHeight = 150f;

        float x = handRect.x;

        if (isRightHand)
        {

            DrawBottomAlignedSlider(ref index,  x, baseY, indexHeight,  sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref middle, x, baseY, middleHeight, sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref ring,   x, baseY, ringHeight,   sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref little, x, baseY, littleHeight, sliderWidth);
        }
        else
        {
            DrawBottomAlignedSlider(ref little, x, baseY, littleHeight, sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref ring,   x, baseY, ringHeight, sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref middle, x, baseY, middleHeight, sliderWidth);
            x += sliderWidth + spacing;

            DrawBottomAlignedSlider(ref index,  x, baseY, indexHeight, sliderWidth);
            x += sliderWidth + spacing;
        }

        // Fist slider beside hand
        x += sliderWidth + 12f;
        DrawBottomAlignedSlider(ref fist, x, baseY, 120f, sliderWidth);

        GUILayout.EndVertical();
    }

    static void DrawBottomAlignedSlider(
        ref float value,
        float x,
        float baseY,
        float height,
        float width)
    {
        Rect r = new Rect(
            x,
            baseY - height,
            width,
            height
        );

        value = GUI.VerticalSlider(
            r,
            value,
            1f,  // top = closed
            0f   // bottom = open
        );
    }
}
