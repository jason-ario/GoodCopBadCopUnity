using UnityEngine;
using UnityEditor;

namespace LuxURPEssentials
{
    [CustomEditor(typeof(ApplyProceduralTextureProperties))]
    public class ApplyProceduralTexturePropertiesEditor : Editor {
        public override void OnInspectorGUI() {
        	DrawDefaultInspector();

        	ApplyProceduralTextureProperties script = (ApplyProceduralTextureProperties)target;

        	if(GUILayout.Button("Apply")) {
        		script.SyncMatWithProceduralTextureAsset();
        	}
        }
    }
}