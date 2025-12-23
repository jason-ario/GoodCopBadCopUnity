#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif
using UnityEngine;

namespace VolumetricFogAndMist2 {

    [ExecuteAlways]
    public class FogTransparentObject : MonoBehaviour {

        public VolumetricFog fogVolume;

        Renderer thisRenderer;
        Material mat;

        void OnEnable () {
            CheckSettings();
#if UNITY_EDITOR
            // workaround for volumetric effect disappearing when saving the scene
            if (!Application.isPlaying) {
                EditorSceneManager.sceneSaving += OnSceneSaving;
                EditorApplication.update += OnEditorUpdate;
            }
#endif            
        }

        void OnDisable () {
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                EditorSceneManager.sceneSaving -= OnSceneSaving;
                EditorApplication.update -= OnEditorUpdate;
            }
#endif
            if (fogVolume != null) {
                fogVolume.UnregisterFogMat(mat);
            }
        }

        void OnSceneSaving (UnityEngine.SceneManagement.Scene scene, string path) {
            CheckSettings();
        }


#if UNITY_EDITOR
        void OnEditorUpdate () {
            // check if fog density is lost due to saving scene (control + s) which resets the fog uniforms
            if (mat == null) return;

            if (!mat.HasProperty(ShaderParams.Density)) {
                CheckSettings();
            }
        }
#endif

        void OnValidate () {
            CheckSettings();
        }

        void CheckSettings () {
            if (thisRenderer == null) {
                thisRenderer = GetComponent<Renderer>();
                if (thisRenderer == null) return;
            }

            mat = thisRenderer.sharedMaterial;
            if (mat == null) return;

            if (fogVolume == null) {
                if (VolumetricFog.volumetricFogs.Count > 0) {
                    fogVolume = VolumetricFog.volumetricFogs[0];
                }
                if (fogVolume == null) return;
            }

            fogVolume.RegisterFogMat(thisRenderer.sharedMaterial);
            fogVolume.UpdateMaterialProperties();
        }
    }
}
