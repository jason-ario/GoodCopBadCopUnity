
using UnityEngine;
using UnityEditor;

namespace SpringBonesTool
{
    [CustomEditor(typeof(SpringBones))]
    public class SpringBonesEditor : Editor
    {
        private readonly string[] axisNames = new string[3] { "X", "Y", "Z" };

        private SerializedProperty axis;
        private SerializedProperty bones;

        private SerializedProperty length;
        private SerializedProperty rigid;
        private SerializedProperty damping;
        private SerializedProperty impact;

        private void OnEnable()
        {
            axis = serializedObject.FindProperty("axis");
            bones = serializedObject.FindProperty("bones");

            length = serializedObject.FindProperty("length");
            rigid = serializedObject.FindProperty("rigid");
            damping = serializedObject.FindProperty("damping");
            impact = serializedObject.FindProperty("impact");
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();

            serializedObject.Update();

            GUILayout.Space(8);

            if (Application.isPlaying)
                GUI.enabled = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Axis");
            axis.intValue = GUILayout.Toolbar(axis.intValue, axisNames);
            GUILayout.EndHorizontal();

            GUI.enabled = true;

            EditorGUILayout.PropertyField(length);
            EditorGUILayout.PropertyField(rigid);
            EditorGUILayout.PropertyField(damping);
            EditorGUILayout.PropertyField(impact);

            if (Application.isPlaying)
                GUI.enabled = false;

            EditorGUILayout.PropertyField(bones, true);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
