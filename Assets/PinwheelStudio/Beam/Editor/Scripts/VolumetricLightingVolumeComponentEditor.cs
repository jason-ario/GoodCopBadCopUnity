using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using System.Collections.Generic;
using Pinwheel.Beam;

namespace Pinwheel.BeamEditor
{
    [CustomEditor(typeof(VolumetricLightingVolumeComponent))]
    public class VolumetricLightingVolumeComponentEditor : VolumeComponentEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorUtils.DrawExternalLinks();
        }
    }
}