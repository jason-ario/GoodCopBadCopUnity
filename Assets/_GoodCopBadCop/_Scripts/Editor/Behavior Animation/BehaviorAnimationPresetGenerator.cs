#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using GoodCopBadCop.SuspectBehaviorAnimation;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor.BehaviorAnimation
{
    public static class BehaviorAnimationPresetGenerator
    {
        private const string MixamoRawPath = "Assets/_GoodCopBadCop/_Animations/Anomalies/Behavior/Mixamo Raw";
        private const string PresetPath = "Assets/_GoodCopBadCop/_Animations/Anomalies/Behavior/Presets";
        private const string BehaviorAudioPath = "Assets/_GoodCopBadCop/_Audio/Anomalies/Behavior";
        private const string LaughingMaleAudioPath = BehaviorAudioPath + "/Laughing_Male_Mischievous.mp3";
        private const string LaughingFemaleAudioPath = BehaviorAudioPath + "/Laughing_Female_Witch.mp3";

        [MenuItem("GoodCopBadCop/Anomalies/Generate Behavior Animation Presets")]
        public static void Generate()
        {
            Directory.CreateDirectory(PresetPath);

            AssetDatabase.ImportAsset(MixamoRawPath, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            if (Directory.Exists(BehaviorAudioPath))
                AssetDatabase.ImportAsset(BehaviorAudioPath, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            SetClipLooping("Dizzy_Default Idle.fbx", true);

            CreatePreset(
                "Paranoid Behavior.asset",
                priority: 20,
                playbackSpeed: 0.75f,
                randomize: true,
                useFirstClipAsContinuousBase: false,
                playContinuously: false,
                blendInSeconds: 0.45f,
                blendOutSeconds: 0.55f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 2.5f,
                maxPauseSeconds: 5.5f,
                "Paranoid_Nervously Look Around.fbx",
                "Paranoid_Look Around Nervously.fbx",
                "Paranoid_Look Over Shoulder.fbx");

            CreatePreset(
                "Fearful Behavior.asset",
                priority: 20,
                playbackSpeed: 1f,
                randomize: false,
                useFirstClipAsContinuousBase: false,
                playContinuously: false,
                blendInSeconds: 0.45f,
                blendOutSeconds: 0.55f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 2f,
                maxPauseSeconds: 4f,
                "Fearful_Terrified Standing.fbx");

            CreatePreset(
                "Laughing Behavior.asset",
                priority: 20,
                playbackSpeed: 1f,
                randomize: false,
                useFirstClipAsContinuousBase: false,
                playContinuously: false,
                blendInSeconds: 0.35f,
                blendOutSeconds: 0.55f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 3f,
                maxPauseSeconds: 7f,
                "Laughing_Standing Laughing.fbx");
            ConfigurePresetAudio(
                "Laughing Behavior.asset",
                LaughingMaleAudioPath,
                LaughingFemaleAudioPath,
                startDelaySeconds: 0.45f,
                repeatDelaySeconds: 3f,
                repeatCount: 1);

            CreatePreset(
                "Violent Behavior.asset",
                priority: 20,
                playbackSpeed: 1f,
                randomize: true,
                useFirstClipAsContinuousBase: false,
                playContinuously: false,
                blendInSeconds: 0.3f,
                blendOutSeconds: 0.45f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 1.2f,
                maxPauseSeconds: 3f,
                "Violent_Ready Idle.fbx",
                "Violent_Cheering.fbx",
                "Violent_Shake Fist.fbx",
                "Violent_Angry Forward Gesture.fbx",
                "Violent_Angry Forward Shoulders.fbx",
                "Violent_Angry Point.fbx");

            CreatePreset(
                "Dizzy Behavior.asset",
                priority: 20,
                playbackSpeed: 0.37f,
                randomize: true,
                useFirstClipAsContinuousBase: true,
                playContinuously: true,
                blendInSeconds: 0.55f,
                blendOutSeconds: 0.55f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 3.5f,
                maxPauseSeconds: 7f,
                "Dizzy_Default Idle.fbx",
                "Dizzy_Drunk Idle.fbx",
                "Dizzy_Drunk Idle Hiccup.fbx",
                "Dizzy_Drunk Idle Stumble.fbx",
                "Dizzy_Running Tired.fbx");

            CreatePreset(
                "Distracted Behavior.asset",
                priority: 20,
                playbackSpeed: 0.45f,
                randomize: true,
                useFirstClipAsContinuousBase: false,
                playContinuously: true,
                blendInSeconds: 0.55f,
                blendOutSeconds: 0.55f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 0f,
                maxPauseSeconds: 0f,
                "Distracted_Looking Around Idle Stand.fbx",
                "Distracted_Unarmed Idle Looking 1.fbx",
                "Distracted_Unarmed Idle Looking 2.fbx");

            CreatePreset(
                "Hyperactive Behavior.asset",
                priority: 20,
                playbackSpeed: 2f,
                randomize: true,
                useFirstClipAsContinuousBase: false,
                playContinuously: false,
                blendInSeconds: 0.35f,
                blendOutSeconds: 0.45f,
                maxPlaySeconds: 0f,
                minPauseSeconds: 0.4f,
                maxPauseSeconds: 1.2f,
                "Hyperactive_Neck Stretching.fbx",
                "Hyperactive_Arm Stretching.fbx",
                "Hyperactive_Batter On Deck.fbx");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BehaviorAnimationPresetGenerator] Behavior animation presets generated.");
        }

        private static void CreatePreset(
            string assetName,
            int priority,
            float playbackSpeed,
            bool randomize,
            bool useFirstClipAsContinuousBase,
            bool playContinuously,
            float blendInSeconds,
            float blendOutSeconds,
            float maxPlaySeconds,
            float minPauseSeconds,
            float maxPauseSeconds,
            params string[] fbxNames)
        {
            string assetPath = $"{PresetPath}/{assetName}";
            BehaviorAnimationPreset preset = AssetDatabase.LoadAssetAtPath<BehaviorAnimationPreset>(assetPath);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<BehaviorAnimationPreset>();
                AssetDatabase.CreateAsset(preset, assetPath);
            }

            var clips = new List<AnimationClip>();
            foreach (string fbxName in fbxNames)
            {
                string fbxPath = $"{MixamoRawPath}/{fbxName}";
                AnimationClip clip = FindMainClip(fbxPath);
                if (clip != null)
                    clips.Add(clip);
                else
                    Debug.LogWarning($"[BehaviorAnimationPresetGenerator] Animation clip not found in {fbxPath}.");
            }

            SerializedObject serialized = new(preset);
            SetClips(serialized.FindProperty("clips"), clips);
            serialized.FindProperty("priority").intValue = priority;
            serialized.FindProperty("playbackSpeed").floatValue = playbackSpeed;
            serialized.FindProperty("randomizeClip").boolValue = randomize;
            serialized.FindProperty("useFirstClipAsContinuousBase").boolValue = useFirstClipAsContinuousBase;
            serialized.FindProperty("playContinuously").boolValue = playContinuously;
            serialized.FindProperty("blendInSeconds").floatValue = blendInSeconds;
            serialized.FindProperty("blendOutSeconds").floatValue = blendOutSeconds;
            serialized.FindProperty("maxPlaySeconds").floatValue = maxPlaySeconds;
            serialized.FindProperty("minPauseSeconds").floatValue = minPauseSeconds;
            serialized.FindProperty("maxPauseSeconds").floatValue = maxPauseSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(preset);
        }

        private static void ConfigurePresetAudio(
            string assetName,
            string maleAudioPath,
            string femaleAudioPath,
            float startDelaySeconds,
            float repeatDelaySeconds,
            int repeatCount)
        {
            string assetPath = $"{PresetPath}/{assetName}";
            BehaviorAnimationPreset preset = AssetDatabase.LoadAssetAtPath<BehaviorAnimationPreset>(assetPath);
            if (preset == null)
            {
                Debug.LogWarning($"[BehaviorAnimationPresetGenerator] Preset not found at {assetPath}.");
                return;
            }

            SerializedObject serialized = new(preset);
            serialized.FindProperty("maleAudioClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(maleAudioPath);
            serialized.FindProperty("femaleAudioClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(femaleAudioPath);
            serialized.FindProperty("audioVolume").floatValue = 1f;
            serialized.FindProperty("audioStartDelaySeconds").floatValue = startDelaySeconds;
            serialized.FindProperty("audioRepeatDelaySeconds").floatValue = repeatDelaySeconds;
            serialized.FindProperty("audioRepeatCount").intValue = repeatCount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(preset);
        }

        private static AnimationClip FindMainClip(string path)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    return clip;
            }

            return null;
        }

        private static void SetClipLooping(string fbxName, bool loop)
        {
            string fbxPath = $"{MixamoRawPath}/{fbxName}";
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter importer)
                return;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            if (clips == null || clips.Length == 0)
                return;

            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                clip.loop = loop;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clips[i] = clip;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static void SetClips(SerializedProperty clipsProperty, IReadOnlyList<AnimationClip> clips)
        {
            clipsProperty.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
                clipsProperty.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
#endif
