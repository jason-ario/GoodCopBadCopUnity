using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Pinwheel.Beam;

namespace Pinwheel.BeamEditor
{
    [CustomEditor(typeof(VolumetricFog))]
    public class VolumetricFogInspector : Editor
    {
        private VolumetricFog instance;

        private void OnEnable()
        {
            instance = target as VolumetricFog;
            SceneView.duringSceneGui += DuringSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
        }

        private class UI
        {
            public static readonly GUIContent COLOR = new GUIContent("Color", "");
            public static readonly GUIContent DENSITY = new GUIContent("Density", "");
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            Color color = EditorGUILayout.ColorField(UI.COLOR, instance.color, true, false, false);
            float density = EditorGUILayout.FloatField(UI.DENSITY, instance.density);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(instance, $"Modify {instance.name}");
                EditorUtility.SetDirty(instance);
                instance.color = color;
                instance.density = density;
            }

            EditorGUILayout.Space();
            EditorUtils.DrawExternalLinks();
        }

        private void DuringSceneGui(SceneView sv)
        {
            Color handleColor = Color.yellow;
            Color handleColorFaded = handleColor;
            handleColorFaded.a = 0.2f;

            Handles.matrix = instance.transform.localToWorldMatrix;
            Handles.color = handleColor;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.color = handleColorFaded;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.matrix = Matrix4x4.identity;
        }
    }
}
