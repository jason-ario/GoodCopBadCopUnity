using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using System.Collections.Generic;
using Pinwheel.Beam;

namespace Pinwheel.BeamEditor
{
    [CustomEditor(typeof(VolumetricLightingRendererFeature))]
    public class VolumetricLightingRendererFeatureEditor : Editor
    {

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorUtils.DrawExternalLinks();
        }
    }
}