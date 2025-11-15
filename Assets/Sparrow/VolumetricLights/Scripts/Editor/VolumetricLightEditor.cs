// 
// Copyright (c) 2023 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
// 

using UnityEditor;
using UnityEngine;
using Sparrow.Utilities;

namespace Sparrow.VolumetricLight.Editor
{
    /*
     * Exposes settings of volumetric light and its profile
     */
    [CustomEditor(typeof(VolumetricLight)), CanEditMultipleObjects]
    public class VolumetricLightEditor : UnityEditor.Editor
    {
        private void SpotlightProperties()
        {
            EditorGUILayout.LabelField("Spotlight Settings", EditorStyles.boldLabel);

            bool overrideSpot = serializedObject.ToggleFoldout("overrideSpotAngle", "Override Spot Angle");
            EditorUtils.Space();
            EditorGUI.indentLevel++;
            if (overrideSpot)
            {
                SerializedProperty newSpotAngle = serializedObject.FindProperty("newSpotAngle");
                EditorGUILayout.PropertyField(newSpotAngle, new GUIContent("Spot Angle"));
            }
            else
            {
                SerializedProperty spotAngleWeight = serializedObject.FindProperty("spotAngleWeight");
                EditorGUILayout.PropertyField(spotAngleWeight, new GUIContent("Angle Inner/Outer"));
            }

            EditorGUI.indentLevel--;
            EditorUtils.Space();
        }

        public static bool HasShaderGraph(VolumetricLight light)
        {
            foreach (Renderer renderer in light.GetComponentsInChildren<Renderer>())
            {
                if (renderer != null)
                {
                    Material mat = renderer.sharedMaterial;
                    if (mat != null && mat.shader != null)
                    {
                        if (mat.shader.name == "Hidden/InternalErrorShader")
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public override void OnInspectorGUI()
        {
            VolumetricLight volumetricLight = target as VolumetricLight;
            serializedObject.Update();
            if (volumetricLight == null) return;

            EditorUtils.DrawLogoHeader("Volumetric Light", "volumetric_light_logo", "https://offthebeatentrack.games/docs/using-the-package-2/", true, colorHex: VolumetricLight.hexColor, feedbackVersion: VolumetricLight.version);

            if(!HasShaderGraph(volumetricLight))
                EditorGUILayout.HelpBox("Shader Graph is not currently installed in this project. Please install it to use this feature.", MessageType.Error);

            switch (volumetricLight.lightSource.type)
            {
                case LightType.Spot:
                    SpotlightProperties();
                    EditorUtils.Separator();
                    break;
                case LightType.Directional:
                    EditorGUILayout.HelpBox("Volumetric lights can not be used for directional lights.", MessageType.Warning);
                    if (GUILayout.Button("Fix now!")) volumetricLight.lightSource.type = LightType.Spot;
                    EditorUtils.Separator();
                    break;
            }


            EditorGUILayout.HelpBox("Assign a profile to share options between multiple lights.", MessageType .Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorUtils.DrawPropertyEditor(serializedObject, "m_LightProfile");

                if (GUILayout.Button("New"))
                {
                    VolumetricLightProfile profile =
                        VolumetricLightProfile.CreateProfile(volumetricLight.gameObject.scene, volumetricLight.name);

                    profile.name = volumetricLight.gameObject.name + "Profile";
                    profile.settings = new LightSettings(volumetricLight.localSettings);
                    volumetricLight.lightProfile = profile;
                    EditorUtility.SetDirty(volumetricLight);
                }
            }

            EditorUtils.DrawPropertyEditor(serializedObject, "m_ScaleWithObjectScale");

            serializedObject.ApplyModifiedProperties();

            EditorUtils.Separator();

            if (volumetricLight.lightProfile == null)
            {
                var changes = LightSettingsEditor.DrawEditor(volumetricLight.localSettings);
                if (changes) EditorUtility.SetDirty(volumetricLight);
            } else
            {
                var changes = LightSettingsEditor.DrawEditor(volumetricLight.lightProfile.settings);
                if (changes) EditorUtility.SetDirty(volumetricLight.lightProfile);
            }

            EditorUtils.Separator();

            if (GUILayout.Button("Toggle Volume visibility"))
            {
                if ((volumetricLight.Volume.gameObject.hideFlags & (HideFlags.HideInHierarchy | HideFlags.HideInInspector)) == (HideFlags.HideInHierarchy | HideFlags.HideInInspector))
                {
                    volumetricLight.Volume.gameObject.hideFlags &= ~(HideFlags.HideInHierarchy | HideFlags.HideInInspector);
                }
                else
                {
                    volumetricLight.Volume.gameObject.hideFlags |= HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                }

            }
            if (GUILayout.Button("Recalculate LightVolume"))
            {
                volumetricLight.AddLightVolume();
            }
            

            serializedObject.ApplyModifiedProperties();
        }
    }
}