using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoodCopBadCop.SuspectBehaviorAnimation;
using GoodCopBadCop.SuspectPaperwork;
using GoodCopBadCop.XRay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.Editor
{
    public sealed class AnomalyPreviewWindow : EditorWindow
    {
        private readonly struct ActiveState
        {
            public readonly GameObject GameObject;
            public readonly bool ActiveSelf;

            public ActiveState(GameObject gameObject, bool activeSelf)
            {
                GameObject = gameObject;
                ActiveSelf = activeSelf;
            }
        }

        private readonly struct RendererState
        {
            public readonly Renderer Renderer;
            public readonly Material[] SharedMaterials;
            public readonly MaterialPropertyBlock PropertyBlock;

            public RendererState(Renderer renderer, Material[] sharedMaterials, MaterialPropertyBlock propertyBlock)
            {
                Renderer = renderer;
                SharedMaterials = sharedMaterials;
                PropertyBlock = propertyBlock;
            }
        }

        private readonly struct SuspectOption
        {
            public readonly string AssetPath;
            public readonly global::SuspectData Data;
            public readonly global::SuspectCharacter CharacterPrefab;
            public readonly string Label;
            public readonly int AnomalyCount;

            public SuspectOption(string assetPath, global::SuspectData data, global::SuspectCharacter characterPrefab, string label, int anomalyCount)
            {
                AssetPath = assetPath;
                Data = data;
                CharacterPrefab = characterPrefab;
                Label = label;
                AnomalyCount = anomalyCount;
            }

            public bool CanSpawn => Data != null && CharacterPrefab != null;
        }

        private const string SuspectDataSearchRoot = "Assets/_GoodCopBadCop/_Data/Suspects";
        private const string SandboxScenePath = "Assets/_GoodCopBadCop/_Scenes/AnomalyPreviewSandbox.unity";
        private const string PreviewRootPrefix = "[Anomaly Preview]";
        private const string PreviewDocumentPrefix = "[Anomaly Preview Document]";
        private const string PreviewDocumentRootName = "[Anomaly Preview Documents]";
        private const string SessionPreviewPrefabPathKey = "GoodCopBadCop.AnomalyPreview.PrefabPath";
        private const string SessionPendingPreviewKey = "GoodCopBadCop.AnomalyPreview.Pending";
        private const float NormalBodyTemperature = 36.5f;
        private const float NormalBodyTemperatureJitter = 0.3f;
        private const float BaseRoomTemperature = 22f;
        private static readonly string[] DocumentPrefabPaths =
        {
            "Assets/_GoodCopBadCop/_Prefabs/Equipment/Pickups/ID card.prefab",
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Application Paper.prefab",
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Entry Permit.prefab",
            "Assets/_GoodCopBadCop/_Prefabs/Interactables/Documents/Exam Pages/Documentation Exam page.prefab"
        };

        private GameObject targetRoot;
        private GameObject previewRoot;
        private GameObject previewDocumentRoot;
        private readonly SuspectPaperworkModel paperworkModel = new();
        private ISuspectPaperworkService paperworkService;
        private readonly List<Anomaly> anomalies = new();
        private readonly List<ActiveState> activeStates = new();
        private readonly List<RendererState> rendererStates = new();
        private readonly List<GameObject> previewDocuments = new();
        private readonly List<SuspectOption> suspectOptions = new();
        private readonly HashSet<Anomaly> initializedAnomalies = new();
        private string[] suspectOptionLabels = Array.Empty<string>();
        private int selectedSuspectIndex = -1;
        private Vector2 windowScrollPosition;
        private Vector2 scrollPosition;
        private Animator previewAnimator;
        private SuspectBehaviorAnimationAdapter previewAnimationAdapter;
        private string status = "Select a suspect prefab or scene suspect.";

        static AnomalyPreviewWindow()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                foreach (AnomalyPreviewWindow window in Resources.FindObjectsOfTypeAll<AnomalyPreviewWindow>())
                    window.ClearPreviewInstance();

                SessionState.SetBool(SessionPendingPreviewKey, false);
                return;
            }

            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionPendingPreviewKey, false))
                GetWindow<AnomalyPreviewWindow>().RestoreSandboxPreviewFromSession();
        }

        [MenuItem(EditorConstants.AnomalyPreviewMenuPath, false, EditorConstants.RootMenuPriority + 2)]
        private static void Open()
        {
            AnomalyPreviewWindow window = GetWindow<AnomalyPreviewWindow>();
            window.titleContent = new GUIContent("Anomaly Preview");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged -= Repaint;
            Selection.selectionChanged += Repaint;
            RefreshSuspects();

            if (EditorApplication.isPlaying && IsSandboxSceneActive())
            {
                if (SessionState.GetBool(SessionPendingPreviewKey, false))
                    EditorApplication.delayCall += RestoreSandboxPreviewFromSession;
                else
                    EditorApplication.delayCall += RecoverSandboxPreviewIfPresent;
            }
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnDestroy()
        {
            Selection.selectionChanged -= Repaint;
            ClearPreviewInstance();
        }

        private void OnInspectorUpdate()
        {
            if (targetRoot != null)
                Repaint();
        }

        private void OnGUI()
        {
            float anomalyPanelWidth = Mathf.Clamp(position.width * 0.45f, 360f, 520f);
            using (new EditorGUILayout.HorizontalScope())
            {
                windowScrollPosition = EditorGUILayout.BeginScrollView(windowScrollPosition, GUILayout.ExpandWidth(true));
                DrawSourceControls();
                EditorGUILayout.Space(8f);
                DrawAnimationPreviewControls();
                EditorGUILayout.Space(8f);
                DrawXRayCursorScanControls();
                EditorGUILayout.Space(8f);
                DrawVitalsReadouts();
                EditorGUILayout.Space(8f);
                DrawClearPreviewControls();
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(8f);
                DrawAnomalyList(GUILayout.Width(anomalyPanelWidth), GUILayout.ExpandHeight(true));
            }
        }

        private void DrawClearPreviewControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Color previousColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.25f, 0.22f);

                using (new EditorGUI.DisabledScope(previewRoot == null))
                {
                    if (GUILayout.Button("Clear Preview", GUILayout.Height(30f)))
                        ClearPreviewInstance();
                }

                GUI.backgroundColor = previousColor;
            }
        }

        private void DrawSourceControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
                DrawSuspectDataPicker();
            }
        }

        private void DrawSuspectDataPicker()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Suspect", GUILayout.Width(72f));

                using (new EditorGUI.DisabledScope(suspectOptionLabels.Length == 0))
                {
                    selectedSuspectIndex = EditorGUILayout.Popup(selectedSuspectIndex, suspectOptionLabels);
                }
            }

            SuspectOption selected = GetSelectedSuspectOption();
            if (selected.Data != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Data", selected.Data, typeof(global::SuspectData), false);

                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Prefab", selected.CharacterPrefab, typeof(global::SuspectCharacter), false);

                EditorGUILayout.LabelField("Anomalies on prefab", selected.AnomalyCount.ToString());
            }
            else if (suspectOptionLabels.Length == 0)
            {
                EditorGUILayout.HelpBox($"No SuspectData assets found under {SuspectDataSearchRoot}.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!selected.CanSpawn))
            {
                if (GUILayout.Button("Spawn", GUILayout.Height(30f)))
                    SpawnSelectedSuspect();
            }

            if (selected.Data != null && selected.CharacterPrefab == null)
                EditorGUILayout.HelpBox("Selected SuspectData has no CharacterPrefab assigned.", MessageType.Warning);
        }



        private void DrawAnimationPreviewControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);

                if (targetRoot == null)
                {
                    EditorGUILayout.LabelField("Spawn a preview target to inspect its behavior animation state.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                RefreshAnimationPreviewIfNeeded();

                if (previewAnimator == null)
                {
                    EditorGUILayout.LabelField("No Animator found under current target.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Animator", previewAnimator, typeof(Animator), true);
                    EditorGUILayout.ObjectField("Controller", previewAnimator.runtimeAnimatorController, typeof(RuntimeAnimatorController), false);
                    EditorGUILayout.ObjectField("Adapter", previewAnimationAdapter, typeof(SuspectBehaviorAnimationAdapter), true);
                    EditorGUILayout.ObjectField("Preset", previewAnimationAdapter != null ? previewAnimationAdapter.CurrentPreset : null, typeof(BehaviorAnimationPreset), false);
                    EditorGUILayout.ObjectField("Current Clip", previewAnimationAdapter != null ? previewAnimationAdapter.CurrentClip : null, typeof(AnimationClip), false);
                }

                if (previewAnimationAdapter == null)
                {
                    EditorGUILayout.LabelField("No behavior animation adapter found under current target.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                string state = previewAnimationAdapter.CurrentClip != null
                    ? "Playing"
                    : previewAnimationAdapter.IsInPause ? "Pause" : "Idle";
                EditorGUILayout.LabelField("State", state);
            }
        }

        private void DrawVitalsReadouts()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Numeric Readouts", EditorStyles.boldLabel);

                if (targetRoot == null)
                {
                    EditorGUILayout.LabelField("Spawn a preview target to inspect scanner values.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                global::SuspectCharacter suspect = GetPreviewSuspect();
                if (suspect == null)
                {
                    EditorGUILayout.LabelField("No SuspectCharacter found under current target.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                float roomOffset = GetActiveRoomTemperatureOffset();
                float roomTemperature = BaseRoomTemperature + roomOffset;
                global::HighTemperatureAnomaly temperatureAnomaly = GetFirstAnomaly<global::HighTemperatureAnomaly>();
                bool hasActiveTemperatureAnomaly = temperatureAnomaly != null && temperatureAnomaly.IsActive;
                float bodyBaseTemperature = hasActiveTemperatureAnomaly
                    ? temperatureAnomaly.ElevatedTemperature
                    : NormalBodyTemperature;
                float bodyJitter = hasActiveTemperatureAnomaly
                    ? temperatureAnomaly.JitterRange
                    : NormalBodyTemperatureJitter;
                float bodyTemperature = bodyBaseTemperature + roomOffset;

                DrawReadoutRow("Room Temp", FormatTemperature(roomTemperature), FormatOffsetNote(roomOffset));
                DrawReadoutRow("Body Temp", FormatTemperature(bodyTemperature), FormatJitterNote(bodyJitter, hasActiveTemperatureAnomaly));
                DrawReadoutRow("Heart Rate", $"{suspect.heartRateBpm} bpm", FormatHeartRateNote());
                DrawReadoutRow("Radiation", suspect.radiationAmount.ToString(), FormatRadiationNote());
            }
        }

        private void DrawXRayCursorScanControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("X-Ray Modes", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "In the sandbox Game view, hover the suspect to reveal a square X-ray crop under the cursor.",
                    EditorStyles.wordWrappedMiniLabel);

                bool canUseScanner = EditorApplication.isPlaying && IsSandboxSceneActive() && GetPreviewSuspect() != null && Camera.main != null;
                XRayCursorScannerPreview scanner = Camera.main != null
                    ? Camera.main.GetComponent<XRayCursorScannerPreview>()
                    : null;
                XRayFullPreview fullPreview = Camera.main != null
                    ? Camera.main.GetComponent<XRayFullPreview>()
                    : null;

                using (new EditorGUI.DisabledScope(!canUseScanner))
                {
                    string label = scanner == null ? "Enable Cursor X-Ray" : "Disable Cursor X-Ray";
                    if (GUILayout.Button(label, GUILayout.Height(26f)))
                        ToggleXRayCursorScanner(scanner, fullPreview);

                    string fullLabel = fullPreview == null ? "Enable Full X-Ray" : "Disable Full X-Ray";
                    if (GUILayout.Button(fullLabel, GUILayout.Height(26f)))
                        ToggleFullXRay(fullPreview, scanner);
                }

                if (!EditorApplication.isPlaying || !IsSandboxSceneActive())
                    EditorGUILayout.LabelField("Spawn the selected suspect into the Play Mode sandbox first.", EditorStyles.miniLabel);
                else if (GetPreviewSuspect() == null)
                    EditorGUILayout.LabelField("No preview suspect is available.", EditorStyles.miniLabel);
            }
        }

        private void ToggleXRayCursorScanner(XRayCursorScannerPreview scanner, XRayFullPreview fullPreview)
        {
            if (scanner != null)
            {
                Destroy(scanner);
                status = "Cursor X-ray scanner disabled.";
                return;
            }

            global::SuspectCharacter suspect = GetPreviewSuspect();
            Camera camera = Camera.main;
            if (suspect == null || camera == null)
            {
                status = "Cursor X-ray scanner needs an active preview suspect and Main Camera.";
                return;
            }

            XRayAnatomyView anatomyView = suspect.GetComponent<XRayAnatomyView>();
            if (anatomyView == null)
                anatomyView = suspect.gameObject.AddComponent<XRayAnatomyView>();

            if (fullPreview != null)
                Destroy(fullPreview);

            scanner = camera.gameObject.AddComponent<XRayCursorScannerPreview>();
            scanner.Configure(camera, suspect.gameObject, anatomyView);
            status = "Cursor X-ray scanner enabled. Hover the suspect in the Game view.";
        }

        private void ToggleFullXRay(XRayFullPreview fullPreview, XRayCursorScannerPreview scanner)
        {
            if (fullPreview != null)
            {
                Destroy(fullPreview);
                status = "Full X-ray disabled.";
                return;
            }

            global::SuspectCharacter suspect = GetPreviewSuspect();
            Camera camera = Camera.main;
            if (suspect == null || camera == null)
            {
                status = "Full X-ray needs an active preview suspect and Main Camera.";
                return;
            }

            if (scanner != null)
                Destroy(scanner);

            XRayAnatomyView anatomyView = suspect.GetComponent<XRayAnatomyView>();
            if (anatomyView == null)
                anatomyView = suspect.gameObject.AddComponent<XRayAnatomyView>();

            fullPreview = camera.gameObject.AddComponent<XRayFullPreview>();
            fullPreview.Configure(anatomyView);
            status = "Full X-ray enabled for the entire suspect.";
        }

        private void DrawReadoutRow(string label, string value, string note)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(96f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(72f));
                EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
            }
        }

        private float GetActiveRoomTemperatureOffset()
        {
            float offset = 0f;
            foreach (global::RoomTemperatureDropAnomaly anomaly in anomalies.OfType<global::RoomTemperatureDropAnomaly>())
            {
                if (!IsAnomalyActive(anomaly))
                    continue;

                if (TryReadSerializedFloat(anomaly, "temperatureOffset", out float temperatureOffset))
                    offset += temperatureOffset;
            }

            return offset;
        }

        private T GetFirstAnomaly<T>() where T : Anomaly
        {
            return anomalies.OfType<T>().FirstOrDefault();
        }

        private string FormatRadiationNote()
        {
            return IsAnomalyTypeActive<global::HighRadiationAnomaly>()
                ? "current SuspectCharacter radiationAmount"
                : "current baseline radiationAmount";
        }

        private string FormatHeartRateNote()
        {
            return IsAnomalyTypeActive<global::HeartRateAnomaly>()
                ? "current SuspectCharacter heartRateBpm"
                : "current baseline heartRateBpm";
        }

        private static string FormatJitterNote(float jitter, bool isAnomalyTemperature)
        {
            string source = isAnomalyTemperature ? "anomaly target" : "normal target";
            return $"{source}, jitter +/-{jitter:0.#}C";
        }

        private static string FormatOffsetNote(float offset)
        {
            if (Mathf.Approximately(offset, 0f))
                return $"base {BaseRoomTemperature:0.#}C";

            return $"base {BaseRoomTemperature:0.#}C, active offset {FormatSignedTemperature(offset)}";
        }

        private static string FormatTemperature(float value)
        {
            return $"{value:0.#}C";
        }

        private static string FormatSignedTemperature(float value)
        {
            return $"{value:+0.#;-0.#;0}C";
        }

        private static bool TryReadSerializedFloat(UnityEngine.Object target, string propertyName, out float value)
        {
            value = 0f;
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                return false;

            value = property.floatValue;
            return true;
        }

        private void DrawAnomalyList(params GUILayoutOption[] layoutOptions)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, layoutOptions))
            {
                EditorGUILayout.LabelField($"Anomalies ({anomalies.Count})", EditorStyles.boldLabel);

                if (targetRoot == null)
                {
                    EditorGUILayout.LabelField("No preview target.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                if (anomalies.Count == 0)
                {
                    EditorGUILayout.LabelField("No Anomaly components found under target.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                string currentCategory = null;

                foreach (Anomaly anomaly in anomalies.ToArray())
                {
                    if (anomaly == null)
                        continue;

                    string category = GetCategoryLabel(anomaly);
                    if (category != currentCategory)
                    {
                        currentCategory = category;
                        EditorGUILayout.Space(6f);
                        EditorGUILayout.LabelField(category, EditorStyles.boldLabel);
                    }

                    DrawAnomalyRow(anomaly);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAnomalyRow(Anomaly anomaly)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                bool isActive = IsAnomalyActive(anomaly);

                EditorGUILayout.LabelField(anomaly.GetType().Name, GUILayout.MinWidth(96f), GUILayout.ExpandWidth(true));
                DrawAnomalyStatus(isActive);

                using (new EditorGUI.DisabledScope(!isActive))
                {
                    if (GUILayout.Button("Find", GUILayout.Width(50f)))
                        Find(anomaly.gameObject);
                }

                string actionLabel = isActive ? "Deactivate" : "Activate";
                if (GUILayout.Button(actionLabel, GUILayout.Width(92f)))
                {
                    if (isActive)
                        DeactivateAnomaly(anomaly);
                    else
                        ActivateAnomaly(anomaly, resetFirst: false);
                }
            }
        }

        private void DrawAnomalyStatus(bool isActive)
        {
            Color previousColor = GUI.contentColor;
            GUI.contentColor = isActive ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.65f, 0.65f, 0.65f);
            EditorGUILayout.LabelField(isActive ? "Active" : "Inactive", EditorStyles.miniBoldLabel, GUILayout.Width(58f));
            GUI.contentColor = previousColor;
        }

        private void RefreshSuspects()
        {
            suspectOptions.Clear();

            string[] guids = AssetDatabase.FindAssets("t:SuspectData", new[] { SuspectDataSearchRoot });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                global::SuspectData data = AssetDatabase.LoadAssetAtPath<global::SuspectData>(assetPath);
                if (data == null)
                    continue;

                global::SuspectCharacter characterPrefab = data.CharacterPrefab;
                int anomalyCount = characterPrefab != null
                    ? characterPrefab.GetComponentsInChildren<Anomaly>(true).Length
                    : 0;

                suspectOptions.Add(new SuspectOption(
                    assetPath,
                    data,
                    characterPrefab,
                    BuildSuspectOptionLabel(data, assetPath, characterPrefab, anomalyCount),
                    anomalyCount));
            }

            suspectOptions.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
            suspectOptionLabels = suspectOptions.Select(option => option.Label).ToArray();

            if (suspectOptions.Count == 0)
            {
                selectedSuspectIndex = -1;
                status = "No SuspectData assets found.";
                return;
            }

            if (selectedSuspectIndex < 0 || selectedSuspectIndex >= suspectOptions.Count)
                selectedSuspectIndex = suspectOptions.FindIndex(option => option.CanSpawn);

            if (selectedSuspectIndex < 0)
                selectedSuspectIndex = 0;

            status = $"Loaded {suspectOptions.Count} suspect data asset(s).";
            Repaint();
        }

        private static string BuildSuspectOptionLabel(global::SuspectData data, string assetPath, global::SuspectCharacter characterPrefab, int anomalyCount)
        {
            string displayName = BuildSuspectDisplayName(data);
            string suffix = characterPrefab != null ? $" ({anomalyCount} anomalies)" : " (missing prefab)";
            return $"{displayName} - {System.IO.Path.GetFileNameWithoutExtension(assetPath)}{suffix}";
        }

        private static string BuildSuspectDisplayName(global::SuspectData data)
        {
            if (data == null)
                return "Missing SuspectData";

            if (!string.IsNullOrWhiteSpace(data.Nickname))
                return data.Nickname;

            string fullName = $"{data.FirstName} {data.LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? data.name : fullName;
        }

        private SuspectOption GetSelectedSuspectOption()
        {
            if (selectedSuspectIndex < 0 || selectedSuspectIndex >= suspectOptions.Count)
                return default;

            return suspectOptions[selectedSuspectIndex];
        }

        private void SpawnPreviewDocuments()
        {
            if (targetRoot == null)
            {
                status = "Spawn a preview target before generating documents.";
                return;
            }

            global::SuspectData data = GetPreviewSuspectData();
            if (data == null)
            {
                status = "Current target has no SuspectData for document preview.";
                return;
            }

            ClearPreviewDocuments();
            previewDocumentRoot = new GameObject(PreviewDocumentRootName);

            for (int i = 0; i < DocumentPrefabPaths.Length; i++)
            {
                string prefabPath = DocumentPrefabPaths[i];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[AnomalyPreviewWindow] Document prefab not found: {prefabPath}");
                    continue;
                }

                GameObject document = EditorApplication.isPlaying
                    ? Instantiate(prefab)
                    : PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (document == null)
                    document = Instantiate(prefab);

                document.name = $"{PreviewDocumentPrefix} {prefab.name}";
                NormalizePreviewDocumentScale(document, prefabPath);
                PositionPreviewDocument(document, prefabPath, previewDocuments.Count);
                SetHideFlagsRecursive(document, HideFlags.None);

                previewDocuments.Add(document);
            }

            ApplyPreviewDocumentState();
            PositionSandboxCamera(targetRoot);
            SceneView.RepaintAll();
            status = $"Generated {previewDocuments.Count} preview document(s).";
        }

        private void ClearPreviewDocuments()
        {
            HashSet<GameObject> rootsToDestroy = new HashSet<GameObject>();
            HashSet<GameObject> documentsToDestroy = new HashSet<GameObject>(previewDocuments.Where(document => document != null));

            if (previewDocumentRoot != null)
                rootsToDestroy.Add(previewDocumentRoot);

            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || EditorUtility.IsPersistent(gameObject) || !gameObject.scene.IsValid())
                    continue;

                if (string.Equals(gameObject.name, PreviewDocumentRootName, StringComparison.Ordinal))
                    rootsToDestroy.Add(gameObject);
                else if (gameObject.name.StartsWith(PreviewDocumentPrefix, StringComparison.Ordinal))
                    documentsToDestroy.Add(gameObject);
            }

            foreach (GameObject root in rootsToDestroy)
                DestroyPreviewObject(root);

            foreach (GameObject document in documentsToDestroy)
            {
                if (document != null && document.transform.parent == null)
                    DestroyPreviewObject(document);
            }

            previewDocumentRoot = null;
            previewDocuments.Clear();
        }

        private void ApplyPreviewDocumentState()
        {
            if (previewDocuments.Count == 0)
                return;

            global::SuspectData data = GetPreviewSuspectData();
            SuspectPaperworkState paperworkState = BuildPreviewPaperworkState(data);

            foreach (GameObject document in previewDocuments.ToArray())
            {
                if (document == null)
                    continue;

                bool isExamPage = IsExamPageDocument(document);
                document.SetActive(isExamPage || paperworkState.DocumentsVisible);
                if (!document.activeSelf)
                    continue;

                PopulatePreviewDocument(document, data, paperworkState);
            }
        }

        private void PopulatePreviewDocument(GameObject document, global::SuspectData data, SuspectPaperworkState paperworkState)
        {
            if (document == null || data == null)
                return;

            foreach (MonoBehaviour component in document.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                if (component is IDCard idCard)
                    idCard.ApplyPreviewState(paperworkState);
                else if (component is ApplicationLetter applicationLetter)
                    applicationLetter.ApplyPreviewState(paperworkState, data);
                else if (component is EntryPermit entryPermit)
                    entryPermit.ApplyPreviewState(paperworkState);
                else if (component is ExamPage examPage)
                    ApplyPreviewExamPageState(examPage);
            }
        }

        private void ApplyPreviewExamPageState(ExamPage examPage)
        {
            if (examPage == null || examPage.ChecklistItems == null)
                return;

            HashSet<string> activeAnomalyTypeNames = anomalies
                .Where(IsAnomalyActive)
                .Select(anomaly => anomaly.GetType().Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (ChecklistItem item in examPage.ChecklistItems)
            {
                if (item == null)
                    continue;

                item.ApplyLockState(locked: false);
                item.SetInteractable(false);
                SetPreviewChecklistItemChecked(item, activeAnomalyTypeNames.Contains(item.AnomalyTypeName));
            }
        }

        private static void SetPreviewChecklistItemChecked(ChecklistItem item, bool isChecked)
        {
            ChecklistVisual visual = item.GetComponent<ChecklistVisual>();
            if (visual != null)
            {
                visual.SetVisible(true);
                visual.SetChecked(isChecked);
            }

            foreach (Checkbox checkbox in item.GetComponentsInChildren<Checkbox>(true))
            {
                FieldInfo checkmarkField = FindField(typeof(Checkbox), "checkmark");
                if (checkmarkField?.GetValue(checkbox) is GameObject checkmark)
                    checkmark.SetActive(isChecked);
            }
        }

        private static bool IsExamPageDocument(GameObject document)
            => document != null && document.GetComponentInChildren<ExamPage>(true) != null;

        private SuspectPaperworkState BuildPreviewPaperworkState(global::SuspectData data)
        {
            paperworkService ??= new SuspectPaperworkService(paperworkModel);

            IEnumerable<string> activeAnomalyTypeNames = anomalies
                .Where(IsAnomalyActive)
                .Select(anomaly => anomaly.GetType().Name);

            global::SuspectCharacter previewSuspect = GetPreviewSuspect();
            Texture idPhoto = previewSuspect != null && previewSuspect.IDPhoto != null
                ? previewSuspect.IDPhoto
                : data != null ? data.IDPhoto : null;

            int currentDay = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 1;
            IEnumerable<Texture> idPhotoPool = suspectOptions
                .Select(option => option.Data != null ? option.Data.IDPhoto : null)
                .Where(photo => photo != null);

            return paperworkService.BuildForPreview(data, idPhoto, activeAnomalyTypeNames, currentDay, selectedSuspectIndex, idPhotoPool);
        }

        private global::SuspectData GetPreviewSuspectData()
        {
            global::SuspectCharacter suspect = GetPreviewSuspect();
            if (suspect != null && suspect.Data != null)
                return suspect.Data;

            return GetSelectedSuspectOption().Data;
        }

        private global::SuspectCharacter GetPreviewSuspect()
        {
            return targetRoot != null ? targetRoot.GetComponentInChildren<global::SuspectCharacter>(true) : null;
        }

        private void NormalizePreviewDocumentScale(GameObject document, string prefabPath)
        {
            Bounds bounds = CalculateRendererBounds(document);
            float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxSize <= 0.001f)
                return;

            float targetSize = prefabPath.Contains("Application Paper") || prefabPath.Contains("Exam page") ? 1.35f : 1.08f;
            float scaleMultiplier = targetSize / maxSize;
            document.transform.localScale *= scaleMultiplier;
        }

        private void PositionPreviewDocument(GameObject document, string prefabPath, int index)
        {
            if (document == null)
                return;

            document.transform.SetPositionAndRotation(
                GetDocumentPreviewPosition(index),
                GetDocumentPreviewRotation(prefabPath));
        }

        private static Vector3 GetDocumentPreviewPosition(int index)
        {
            switch (index)
            {
                case 0:
                    return new Vector3(1.05f, 2.34f, -4.576f);
                case 1:
                    return new Vector3(2.066f, 1.894f, -5.597f);
                case 2:
                    return new Vector3(1.172f, 1.405f, -9.492f);
                case 3:
                    return new Vector3(0.975f, 1.952f, -5.551678f);
                default:
                    return new Vector3(1.05f, 2.34f - index * 0.6f, -0.52f);
            }
        }

        private static Quaternion GetDocumentPreviewRotation(string prefabPath)
        {
            if (prefabPath.Contains("Documentation Exam page"))
                return Quaternion.Euler(0f, 180f, 90f);

            if (prefabPath.Contains("Entry Permit"))
                return Quaternion.Euler(272.5217f, 270.7207f, 268.3602f);

            return Quaternion.Euler(270f, 0f, 0f);
        }

        private void RefreshAnimationPreviewIfNeeded()
        {
            Animator animator = targetRoot != null ? targetRoot.GetComponentInChildren<Animator>(true) : null;
            SuspectBehaviorAnimationAdapter adapter = targetRoot != null
                ? targetRoot.GetComponentInChildren<SuspectBehaviorAnimationAdapter>(true)
                : null;

            if (animator == previewAnimator && adapter == previewAnimationAdapter)
                return;

            previewAnimator = animator;
            previewAnimationAdapter = adapter;
        }

        private bool IsAnomalyActive(Anomaly anomaly)
        {
            AnomalyController controller = GetActiveAnomalyController();
            return anomaly != null && controller != null && controller.activeAnomalies.Contains(anomaly);
        }

        private bool IsAnomalyTypeActive<T>() where T : Anomaly
        {
            return anomalies.Any(anomaly => anomaly is T && IsAnomalyActive(anomaly));
        }

        private AnomalyController GetActiveAnomalyController()
        {
            return targetRoot != null ? targetRoot.GetComponentInChildren<AnomalyController>(true) : null;
        }

        private void SetAnomalyActiveState(Anomaly anomaly, bool active)
        {
            AnomalyController controller = GetActiveAnomalyController();
            if (controller == null || anomaly == null)
                return;

            if (active)
            {
                if (!controller.activeAnomalies.Contains(anomaly))
                    controller.activeAnomalies.Add(anomaly);
            }
            else
            {
                controller.activeAnomalies.Remove(anomaly);
            }
        }

        private void StartPlaymodePreview(GameObject prefab)
        {
            if (prefab == null)
            {
                status = "Selected SuspectData has no CharacterPrefab assigned.";
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                status = "Selected SuspectData must reference a project prefab asset.";
                return;
            }

            SessionState.SetString(SessionPreviewPrefabPathKey, prefabPath);
            SessionState.SetBool(SessionPendingPreviewKey, true);

            if (EditorApplication.isPlaying)
            {
                if (!IsSandboxSceneActive())
                {
                    status = "Stop Play Mode first. Sandbox scene can only be opened before entering Play Mode.";
                    return;
                }

                RestoreSandboxPreviewFromSession();
                return;
            }

            if (!EnsureSandboxSceneIsOpen())
                return;

            status = "Entering Play Mode sandbox...";
            EditorApplication.isPlaying = true;
        }

        private void RestoreSandboxPreviewFromSession()
        {
            string prefabPath = SessionState.GetString(SessionPreviewPrefabPathKey, string.Empty);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                status = "Could not load pending sandbox suspect prefab.";
                return;
            }

            ClearPreviewInstance();
            SpawnPreviewInstance(prefab, prefab.name);
            SpawnPreviewDocuments();
            SessionState.SetBool(SessionPendingPreviewKey, false);
            status = $"Sandbox preview running: {prefab.name}.";
        }

        private void RecoverSandboxPreviewIfPresent()
        {
            if (!EditorApplication.isPlaying || !IsSandboxSceneActive() || targetRoot != null)
                return;

            GameObject existingPreview = FindExistingPreviewRoot();
            if (existingPreview == null)
                return;

            SetTarget(existingPreview, isPreviewInstance: true);
            RefreshPreviewDocumentReferences();

            if (previewDocuments.Count == 0)
                SpawnPreviewDocuments();
            else
            {
                ApplyPreviewDocumentState();
                PositionSandboxCamera(existingPreview);
            }

            Selection.activeGameObject = existingPreview;
            status = $"Recovered sandbox preview: {existingPreview.name}.";
        }

        private static GameObject FindExistingPreviewRoot()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root != null && root.name.StartsWith(PreviewRootPrefix, StringComparison.Ordinal))
                    return root;
            }

            return null;
        }

        private void RefreshPreviewDocumentReferences()
        {
            previewDocuments.Clear();
            previewDocumentRoot = null;

            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || EditorUtility.IsPersistent(gameObject) || !gameObject.scene.IsValid())
                    continue;

                if (string.Equals(gameObject.name, PreviewDocumentRootName, StringComparison.Ordinal))
                {
                    previewDocumentRoot = gameObject;
                    continue;
                }

                if (gameObject.name.StartsWith(PreviewDocumentPrefix, StringComparison.Ordinal))
                    previewDocuments.Add(gameObject);
            }

            if (previewDocumentRoot != null)
            {
                previewDocuments.Clear();
                foreach (Transform child in previewDocumentRoot.transform)
                    previewDocuments.Add(child.gameObject);
            }
        }

        private bool EnsureSandboxSceneIsOpen()
        {
            if (IsSandboxSceneActive())
            {
                EnsureSandboxSceneLayout();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                status = "Sandbox preview cancelled because current scene changes were not handled.";
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath) == null && !CreateSandboxScene())
                return false;

            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            EnsureSandboxSceneLayout();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return true;
        }

        private bool CreateSandboxScene()
        {
            string directory = System.IO.Path.GetDirectoryName(SandboxScenePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureSandboxEnvironment();

            if (!EditorSceneManager.SaveScene(scene, SandboxScenePath))
            {
                status = "Failed to save anomaly preview sandbox scene.";
                return false;
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static void ConfigureSandboxEnvironment()
        {
            EnsureSandboxSceneLayout();
        }

        private static void EnsureSandboxSceneLayout()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.fog = false;

            GameObject stand = GameObject.Find("Anomaly Preview Stand");
            if (stand != null)
                UnityEngine.Object.DestroyImmediate(stand);

            GameObject cameraObject = GetOrCreateSandboxObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            if (camera == null)
                camera = cameraObject.AddComponent<Camera>();

            EnsureSandboxAudioListener(cameraObject);

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.2f, 0.22f);
            camera.fieldOfView = 21.2f;
            cameraObject.transform.position = new Vector3(0f, 1.35f, -4.0f);
            cameraObject.transform.rotation = Quaternion.Euler(10f, 0f, 0f);

            GameObject lightObject = GetOrCreateSandboxObject("Key Light");
            Light light = lightObject.GetComponent<Light>();
            if (light == null)
                light = lightObject.AddComponent<Light>();

            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -25f, 0f);

            GameObject fillObject = GetOrCreateSandboxObject("Fill Light");
            Light fill = fillObject.GetComponent<Light>();
            if (fill == null)
                fill = fillObject.AddComponent<Light>();

            fill.type = LightType.Point;
            fill.intensity = 1.2f;
            fill.range = 5f;
            fillObject.transform.position = new Vector3(-1.6f, 1.7f, -2.2f);

            GameObject floor = GameObject.Find("Preview Floor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Preview Floor";
            }

            floor.transform.position = Vector3.zero;
            floor.transform.rotation = Quaternion.identity;
            floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f);

            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial == null)
            {
                Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = new Color(0.34f, 0.36f, 0.37f);
                renderer.sharedMaterial = material;
            }
        }

        private static GameObject GetOrCreateSandboxObject(string objectName)
        {
            GameObject gameObject = GameObject.Find(objectName);
            return gameObject != null ? gameObject : new GameObject(objectName);
        }

        private static bool IsSandboxSceneActive()
        {
            return string.Equals(SceneManager.GetActiveScene().path, SandboxScenePath, StringComparison.OrdinalIgnoreCase);
        }

        private void SpawnSelectedSuspect()
        {
            SuspectOption selected = GetSelectedSuspectOption();
            if (!selected.CanSpawn)
            {
                status = "Selected suspect cannot be spawned because CharacterPrefab is missing.";
                return;
            }

            StartPlaymodePreview(selected.CharacterPrefab.gameObject);
        }

        private void SpawnPreviewInstance(GameObject prefab, string displayName = null)
        {
            if (prefab == null)
            {
                status = "Selected SuspectData has no CharacterPrefab assigned.";
                return;
            }

            ClearPreviewInstance();

            GameObject instance = EditorApplication.isPlaying
                ? Instantiate(prefab)
                : PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (instance == null)
                instance = Instantiate(prefab);

            instance.name = $"{PreviewRootPrefix} {displayName ?? prefab.name}";
            instance.transform.position = GetPreviewSpawnPosition();
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            SetHideFlagsRecursive(instance, HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild);

            SetTarget(instance, isPreviewInstance: true);
            PositionSandboxCamera(instance);
            Selection.activeGameObject = instance;
            FrameTarget();
            status = $"Spawned sandbox preview: {instance.name}.";
        }

        private void SetTarget(GameObject root, bool isPreviewInstance)
        {
            targetRoot = root;
            previewRoot = isPreviewInstance ? root : null;
            initializedAnomalies.Clear();
            CaptureBaseline();
            RefreshTarget();
            RefreshAnimationPreviewIfNeeded();
        }

        private void RefreshTarget()
        {
            anomalies.Clear();

            if (targetRoot == null)
            {
                status = "No target.";
                return;
            }

            anomalies.AddRange(targetRoot.GetComponentsInChildren<Anomaly>(true)
                .OrderBy(GetCategorySortOrder)
                .ThenBy(anomaly => anomaly.GetType().Name));

            status = $"Found {anomalies.Count} anomaly component(s) under {targetRoot.name}.";
            Repaint();
        }


        private void ActivateAnomaly(Anomaly anomaly, bool resetFirst)
        {
            if (anomaly == null)
                return;

            if (resetFirst)
                ResetTarget();

            if (anomaly is AnimatedBehaviorAnomaly)
                DeactivateOtherActiveAnimatedBehaviorAnomalies(anomaly);

            EnsureAnomalyInitialized(anomaly);

            try
            {
                anomaly.ActivateAnomaly();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AnomalyPreviewWindow] '{anomaly.GetType().Name}' activation threw in editor preview: {exception.Message}", anomaly);
            }

            SetAnomalyActiveState(anomaly, true);
            ApplyImmediateEditorPreview(anomaly);
            if (anomaly is DocumentationAnomaly)
                ApplyPreviewDocumentState();
            EditorUtility.SetDirty(anomaly);
            SceneView.RepaintAll();
            status = $"Activated {anomaly.GetType().Name}.";
        }

        private void DeactivateOtherActiveAnimatedBehaviorAnomalies(Anomaly selectedAnomaly)
        {
            foreach (Anomaly anomaly in anomalies.ToArray())
            {
                if (anomaly == null
                    || ReferenceEquals(anomaly, selectedAnomaly)
                    || !(anomaly is AnimatedBehaviorAnomaly)
                    || !IsAnomalyActive(anomaly))
                {
                    continue;
                }

                DeactivateAnomaly(anomaly);
            }
        }

        private void DeactivateAnomaly(Anomaly anomaly)
        {
            if (anomaly == null)
                return;

            EnsureAnomalyInitialized(anomaly);

            try
            {
                anomaly.DeactivateAnomaly();
                anomaly.InitializeDisabled();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AnomalyPreviewWindow] '{anomaly.GetType().Name}' deactivation threw in editor preview: {exception.Message}", anomaly);
            }

            SetAnomalyActiveState(anomaly, false);
            RestoreRendererBaseline();
            if (anomaly is DocumentationAnomaly)
                ApplyPreviewDocumentState();
            SceneView.RepaintAll();
            status = $"Deactivated {anomaly.GetType().Name}.";
        }

        private void ResetTarget()
        {
            if (targetRoot == null)
                return;

            foreach (Anomaly anomaly in anomalies.ToArray())
            {
                if (anomaly == null)
                    continue;

                EnsureAnomalyInitialized(anomaly);

                try
                {
                    anomaly.DeactivateAnomaly();
                    anomaly.InitializeDisabled();
                    SetAnomalyActiveState(anomaly, false);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[AnomalyPreviewWindow] '{anomaly.GetType().Name}' reset threw in editor preview: {exception.Message}", anomaly);
                }
            }

            RestoreActiveBaseline();
            RestoreRendererBaseline();
            ApplyPreviewDocumentState();
            SceneView.RepaintAll();
            status = $"Reset {targetRoot.name}.";
        }

        private void ClearPreviewInstance()
        {
            XRayCursorScannerPreview scanner = Camera.main != null
                ? Camera.main.GetComponent<XRayCursorScannerPreview>()
                : null;
            if (scanner != null)
                Destroy(scanner);

            XRayFullPreview fullPreview = Camera.main != null
                ? Camera.main.GetComponent<XRayFullPreview>()
                : null;
            if (fullPreview != null)
                Destroy(fullPreview);

            ClearPreviewDocuments();

            DestroyPreviewObject(previewRoot);

            previewRoot = null;
            targetRoot = null;
            previewAnimator = null;
            previewAnimationAdapter = null;
            anomalies.Clear();
            activeStates.Clear();
            rendererStates.Clear();
            initializedAnomalies.Clear();
            status = "Preview cleared.";
        }

        private void CaptureBaseline()
        {
            activeStates.Clear();
            rendererStates.Clear();

            if (targetRoot == null)
                return;

            foreach (Transform child in targetRoot.GetComponentsInChildren<Transform>(true))
                activeStates.Add(new ActiveState(child.gameObject, child.gameObject.activeSelf));

            foreach (Renderer renderer in targetRoot.GetComponentsInChildren<Renderer>(true))
            {
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                rendererStates.Add(new RendererState(renderer, renderer.sharedMaterials.ToArray(), propertyBlock));
            }
        }

        private void RestoreActiveBaseline()
        {
            foreach (ActiveState state in activeStates)
            {
                if (state.GameObject != null)
                    state.GameObject.SetActive(state.ActiveSelf);
            }
        }

        private void RestoreRendererBaseline()
        {
            foreach (RendererState state in rendererStates)
            {
                if (state.Renderer == null)
                    continue;

                state.Renderer.sharedMaterials = state.SharedMaterials;
                state.Renderer.SetPropertyBlock(state.PropertyBlock);
            }
        }

        private void EnsureAnomalyInitialized(Anomaly anomaly)
        {
            if (EditorApplication.isPlaying)
                return;

            if (!initializedAnomalies.Add(anomaly))
                return;

            InvokeLifecycleMethod(anomaly, "Awake");
            InvokeLifecycleMethod(anomaly, "OnEnable");
        }

        private void ApplyImmediateEditorPreview(Anomaly anomaly)
        {
            if (anomaly is BlackEyesAnomaly)
            {
                Renderer headRenderer = GetFieldValue<Renderer>(anomaly, "headRenderer");
                ApplyRendererFloatPreview(headRenderer, "TCP2_BLACK_EYES", "_BlackEyesStrength", 1f, "_UseBlackEyes", 1f);
            }
            else if (anomaly is LesionAnomaly)
            {
                Renderer[] renderers = GetFieldValue<Renderer[]>(anomaly, "renderers");
                ApplyRendererFloatPreview(renderers, "TCP2_LESION", "_LesionStrength", 1f, null, 0f);
            }
            else if (anomaly is BlueVeinsAnomaly)
            {
                SetFieldValue(anomaly, "_previewInEditor", true);
                InvokeLifecycleMethod(anomaly, "OnValidate");
            }
        }

        private static void ApplyRendererFloatPreview(Renderer renderer, string keyword, string floatName, float value, string secondaryFloatName, float secondaryValue)
        {
            if (renderer == null)
                return;

            ApplyRendererFloatPreview(new[] { renderer }, keyword, floatName, value, secondaryFloatName, secondaryValue);
        }

        private static void ApplyRendererFloatPreview(Renderer[] renderers, string keyword, string floatName, float value, string secondaryFloatName, float secondaryValue)
        {
            if (renderers == null)
                return;

            int floatId = Shader.PropertyToID(floatName);
            int secondaryFloatId = string.IsNullOrEmpty(secondaryFloatName) ? 0 : Shader.PropertyToID(secondaryFloatName);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(floatId, value);
                renderer.SetPropertyBlock(propertyBlock);

                foreach (Material material in renderer.materials)
                {
                    if (material == null)
                        continue;

                    material.EnableKeyword(keyword);
                    if (!string.IsNullOrEmpty(secondaryFloatName))
                        material.SetFloat(secondaryFloatId, secondaryValue);
                }
            }
        }

        private void FrameTarget()
        {
            if (targetRoot == null)
                return;

            Selection.activeGameObject = targetRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void Find(UnityEngine.Object target)
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private static Vector3 GetPreviewSpawnPosition()
        {
            if (IsSandboxSceneActive())
                return new Vector3(-0.58f, 0.16f, -2.42f);

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return Vector3.zero;

            return sceneView.pivot;
        }

        private static void DestroyPreviewObject(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        private void PositionSandboxCamera(GameObject target)
        {
            if (target == null || !IsSandboxSceneActive())
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            EnsureSandboxAudioListener(camera.gameObject);
            camera.fieldOfView = 21.2f;
            camera.transform.SetPositionAndRotation(
                new Vector3(0.760934f, 1.620042f, -11.05713f),
                Quaternion.Euler(0.679511f, 0f, 0f));
        }

        private static void EnsureSandboxAudioListener(GameObject cameraObject)
        {
            if (cameraObject != null && cameraObject.GetComponent<AudioListener>() == null)
                cameraObject.AddComponent<AudioListener>();
        }

        private Bounds CalculatePreviewContentBounds(GameObject target)
        {
            Bounds bounds = CalculateRendererBounds(target);
            bool hasBounds = bounds.size != Vector3.zero;

            foreach (GameObject document in previewDocuments)
            {
                if (document == null || !document.activeInHierarchy)
                    continue;

                Bounds documentBounds = CalculateRendererBounds(document);
                if (documentBounds.size == Vector3.zero)
                    continue;

                if (!hasBounds)
                {
                    bounds = documentBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(documentBounds);
                }
            }

            return hasBounds ? bounds : new Bounds(target.transform.position, Vector3.zero);
        }

        private static Bounds CalculateRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? bounds : new Bounds(target.transform.position, Vector3.zero);
        }

        private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.hideFlags = hideFlags;
        }

        private static string GetCategoryLabel(Anomaly anomaly)
        {
            if (anomaly is DocumentationAnomaly) return "Documentation";
            if (anomaly is VitalsAnomaly) return "Vitals";
            if (anomaly is BehaviorAnomaly) return "Behavior";
            if (anomaly is PhysicalAnomaly) return "Physical";
            if (anomaly is SupernaturalAnomaly) return "Supernatural";
            return "Other";
        }

        private static int GetCategorySortOrder(Anomaly anomaly)
        {
            if (anomaly is DocumentationAnomaly) return 0;
            if (anomaly is VitalsAnomaly) return 1;
            if (anomaly is BehaviorAnomaly) return 2;
            if (anomaly is PhysicalAnomaly) return 3;
            if (anomaly is SupernaturalAnomaly) return 4;
            return 5;
        }

        private static void InvokeLifecycleMethod(object target, string methodName)
        {
            MethodInfo method = FindMethod(target.GetType(), methodName);
            if (method == null || method.GetParameters().Length != 0)
                return;

            try
            {
                method.Invoke(target, null);
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogWarning($"[AnomalyPreviewWindow] {target.GetType().Name}.{methodName} failed: {exception.InnerException?.Message ?? exception.Message}", target as UnityEngine.Object);
            }
        }

        private static MethodInfo FindMethod(Type type, string methodName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static T GetFieldValue<T>(object target, string fieldName)
        {
            FieldInfo field = FindField(target.GetType(), fieldName);
            if (field == null)
                return default;

            object value = field.GetValue(target);
            return value is T typedValue ? typedValue : default;
        }

        private static void SetFieldValue(object target, string fieldName, object value)
        {
            FieldInfo field = FindField(target.GetType(), fieldName);
            field?.SetValue(target, value);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }
    }
}
