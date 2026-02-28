using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class FingerSceneUI
{
    const float SCALE = 0.65f;

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

        float sliderWidth = 18f * SCALE;
        float spacing = 6f * SCALE;
        float fistSpacing = 12f * SCALE;

        float singleHandWidth =
            (sliderWidth * 6) +
            (spacing * 4) +
            fistSpacing;

        float panelWidth = (singleHandWidth * 2) + 30f;
        float panelHeight = 135f; // tighter panel

        Rect sceneRect = sceneView.position;

        float rightPadding = 15f;
        float bottomPadding = 60f;

        Rect area = new Rect(
            sceneRect.width - panelWidth - rightPadding,
            sceneRect.height - panelHeight - bottomPadding,
            panelWidth,
            panelHeight
        );

        // Dark translucent background
        EditorGUI.DrawRect(area, new Color(0f, 0f, 0f, 0.45f));

        float startX = area.x + 12f;
        float startY = area.y + 6f;

        // Labels
        GUI.Label(
            new Rect(startX, startY, 80f, 16f),
            "Left",
            EditorStyles.miniBoldLabel
        );

        GUI.Label(
            new Rect(startX + singleHandWidth + 20f, startY, 80f, 16f),
            "Right",
            EditorStyles.miniBoldLabel
        );

        startY += 18f;

        // LEFT HAND
        DrawHand(controller,
            ref controller.leftIndex,
            ref controller.leftMiddle,
            ref controller.leftRing,
            ref controller.leftLittle,
            ref controller.leftFist,
            false,
            startX,
            startY,
            sliderWidth,
            spacing
        );

        // RIGHT HAND
        DrawHand(controller,
            ref controller.rightIndex,
            ref controller.rightMiddle,
            ref controller.rightRing,
            ref controller.rightLittle,
            ref controller.rightFist,
            true,
            startX + singleHandWidth + 20f,
            startY,
            sliderWidth,
            spacing
        );

        Handles.EndGUI();
    }

    static void DrawHand(
        HumanoidFingerController controller,
        ref float index,
        ref float middle,
        ref float ring,
        ref float little,
        ref float fist,
        bool isRight,
        float startX,
        float startY,
        float sliderWidth,
        float spacing)
    {
        float littleHeight = 60f * SCALE;
        float ringHeight = 75f * SCALE;
        float indexHeight = 90f * SCALE;
        float middleHeight = 105f * SCALE;
        float fistHeight = 85f * SCALE;

        float baseY = startY + 105f * SCALE;
        float x = startX;

        // White for fingers
        GUI.color = Color.white;

        if (isRight)
        {
            HandleSlider(controller, ref index, x, baseY, indexHeight, sliderWidth, "rightIndex");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref middle, x, baseY, middleHeight, sliderWidth, "rightMiddle");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref ring, x, baseY, ringHeight, sliderWidth, "rightRing");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref little, x, baseY, littleHeight, sliderWidth, "rightLittle");
        }
        else
        {
            HandleSlider(controller, ref little, x, baseY, littleHeight, sliderWidth, "leftLittle");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref ring, x, baseY, ringHeight, sliderWidth, "leftRing");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref middle, x, baseY, middleHeight, sliderWidth, "leftMiddle");
            x += sliderWidth + spacing;

            HandleSlider(controller, ref index, x, baseY, indexHeight, sliderWidth, "leftIndex");
        }

        // Fist slider = blue
        x += sliderWidth + 10f;
        GUI.color = new Color(0.2f, 0.6f, 1f, 1f);

        HandleSlider(controller,
            ref fist,
            x,
            baseY,
            fistHeight,
            sliderWidth,
            isRight ? "rightFist" : "leftFist"
        );

        GUI.color = Color.white;
    }

    static void HandleSlider(
        HumanoidFingerController controller,
        ref float value,
        float x,
        float baseY,
        float height,
        float width,
        string propertyName)
    {
        Rect r = new Rect(
            x,
            baseY - height,
            width,
            height
        );

        EditorGUI.BeginChangeCheck();

        float newVal = GUI.VerticalSlider(
            r,
            value,
            1f,
            0f
        );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Finger Change");

            value = newVal;

            EditorUtility.SetDirty(controller);
            SceneView.RepaintAll();
        }
    }
}