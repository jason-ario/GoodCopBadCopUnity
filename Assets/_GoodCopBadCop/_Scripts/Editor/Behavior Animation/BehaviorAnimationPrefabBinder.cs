#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GoodCopBadCop.SuspectBehaviorAnimation;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor.BehaviorAnimation
{
    public static class BehaviorAnimationPrefabBinder
    {
        private const string RootSuspectPrefabPath = "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs/Root Prefab/Suspect.prefab";
        private const string SandroPrefabPath = "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs/Suspect_Sandro.prefab";
        private const string PresetPath = "Assets/_GoodCopBadCop/_Animations/Anomalies/Behavior/Presets";
        private const string ContainerName = "Behavior Anomalies";

        private static readonly (Type AnomalyType, string PresetName)[] Bindings =
        {
            (typeof(ViolentAnomaly), "Violent Behavior.asset"),
            (typeof(DizzyAnomaly), "Dizzy Behavior.asset"),
            (typeof(LaughingAnomaly), "Laughing Behavior.asset"),
            (typeof(FearfulAnomaly), "Fearful Behavior.asset"),
            (typeof(ParanoidAnomaly), "Paranoid Behavior.asset"),
            (typeof(DistractedAnomaly), "Distracted Behavior.asset"),
            (typeof(HyperactiveAnomaly), "Hyperactive Behavior.asset"),
        };

        [MenuItem("GoodCopBadCop/Anomalies/Bind Behavior Animation Prefabs")]
        public static void Bind()
        {
            BindPrefab(RootSuspectPrefabPath);
            BindPrefab(SandroPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BehaviorAnimationPrefabBinder] Behavior animation anomalies bound to suspect prefabs.");
        }

        private static void BindPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                SuspectCharacter suspect = root.GetComponentInChildren<SuspectCharacter>(true);
                AnomalyController controller = root.GetComponentInChildren<AnomalyController>(true);
                if (suspect == null || controller == null)
                {
                    Debug.LogWarning($"[BehaviorAnimationPrefabBinder] Missing SuspectCharacter or AnomalyController in {prefabPath}.");
                    return;
                }

                SuspectBehaviorAnimationAdapter adapter = root.GetComponentInChildren<SuspectBehaviorAnimationAdapter>(true);
                if (adapter == null)
                    adapter = suspect.gameObject.AddComponent<SuspectBehaviorAnimationAdapter>();

                SetObjectReference(adapter, "animator", suspect.animator);

                Transform container = root.transform.Find(ContainerName);
                if (container == null)
                {
                    var containerObject = new GameObject(ContainerName);
                    containerObject.transform.SetParent(root.transform, false);
                    container = containerObject.transform;
                }

                var behaviorAnomalies = new List<BehaviorAnomaly>();
                foreach ((Type anomalyType, string presetName) in Bindings)
                {
                    BehaviorAnomaly anomaly = GetOrAddAnomaly(container.gameObject, anomalyType);
                    behaviorAnomalies.Add(anomaly);

                    if (anomaly is AnimatedBehaviorAnomaly animated)
                    {
                        SetObjectReference(animated, "animationAdapter", adapter);
                        SetObjectReference(animated, "animationPreset", LoadPreset(presetName));
                    }
                }

                SerializedObject serializedController = new(controller);
                SerializedProperty behaviorPool = serializedController.FindProperty("_behaviorAnomalies");
                behaviorPool.arraySize = behaviorAnomalies.Count;
                for (int i = 0; i < behaviorAnomalies.Count; i++)
                    behaviorPool.GetArrayElementAtIndex(i).objectReferenceValue = behaviorAnomalies[i];

                serializedController.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static BehaviorAnomaly GetOrAddAnomaly(GameObject target, Type anomalyType)
        {
            Component existing = target.GetComponent(anomalyType);
            if (existing != null)
                return (BehaviorAnomaly)existing;

            return (BehaviorAnomaly)target.AddComponent(anomalyType);
        }

        private static BehaviorAnimationPreset LoadPreset(string presetName)
        {
            return AssetDatabase.LoadAssetAtPath<BehaviorAnimationPreset>($"{PresetPath}/{presetName}");
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[BehaviorAnimationPrefabBinder] Property '{propertyName}' not found on {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
#endif
