using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Pinwheel.Beam;

namespace Pinwheel.BeamEditor
{
    public static class EditorMenus
    {
        [MenuItem("GameObject/Effects/BEAM - Local Fog Volume")]
        public static void CreateLocalFogVolume(MenuCommand menuCmd)
        {
            GameObject g = new GameObject("Fog Volume");
            if (menuCmd != null)
                GameObjectUtility.SetParentAndAlign(g, menuCmd.context as GameObject);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one*10;

            VolumetricFog fog = g.AddComponent<VolumetricFog>();
            Undo.RegisterCreatedObjectUndo(g, "Creating Fog Volume");
            Selection.activeGameObject = g;
        }
    }
}
