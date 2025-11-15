// 
// Copyright (c) 2023 Off The Beaten Track UG
// All rights reserved.
// 
// Maintainer: Jens Bahr
// 

using UnityEditor;
using Sparrow.Utilities;

namespace Sparrow.VolumetricLight.Editor
{
    /*
     * formatting volumetric light profiles
     */
    [CustomEditor(typeof(VolumetricLightProfile))]
    public class VolumetricLightProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorUtils.DrawLogoHeader("Volumetric Light Profile", "volumetric_light_logo", "https://offthebeatentrack.games/docs/light-profiles/", true, true, VolumetricLight.hexColor, VolumetricLight.version, VolumetricLight.assetID);
            VolumetricLightProfile profile = target as VolumetricLightProfile;
            var changes = LightSettingsEditor.DrawEditor(profile.settings);
            if (changes) EditorUtility.SetDirty(profile);
        }
    }
}