using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor
{
    /// <summary>
    /// Editor-only settings that control how the game looks while working in Edit Mode.
    /// Changes are applied non-destructively by <see cref="EditorViewSettingsApplier"/>:
    /// everything is restored before Play Mode, scene saves and domain reloads, so runtime
    /// and builds always use the authored scene state.
    /// </summary>
    [CreateAssetMenu(fileName = "EditorViewSettings", menuName = "Good Cop Bad Cop/Editor/Editor View Settings")]
    public class EditorViewSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/_GoodCopBadCop/_Data/Utility/EditorViewSettings.asset";

        [Tooltip("Master switch. When off, the editor shows the scene exactly as authored.")]
        public bool applyInEditMode = true;

        [Tooltip("Disable Volumetric Fog 2 volumes while in Edit Mode. They are re-enabled automatically in Play Mode.")]
        public bool hideVolumetricFog = true;

        public bool ShouldHideVolumetricFog => applyInEditMode && hideVolumetricFog;

        private static EditorViewSettings _instance;

        public static EditorViewSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = AssetDatabase.LoadAssetAtPath<EditorViewSettings>(DefaultAssetPath);
                if (_instance != null) return _instance;

                var guids = AssetDatabase.FindAssets("t:" + nameof(EditorViewSettings));
                if (guids.Length > 0)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<EditorViewSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (_instance != null) return _instance;
                }

                _instance = CreateInstance<EditorViewSettings>();
                AssetDatabase.CreateAsset(_instance, DefaultAssetPath);
                AssetDatabase.SaveAssets();
                return _instance;
            }
        }

        private void OnValidate()
        {
            EditorViewSettingsApplier.RequestRefresh();
        }
    }
}
