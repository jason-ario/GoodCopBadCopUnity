using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PipeSystem.Editor
{
    public class PipePlacementWindow : EditorWindow
    {
        private List<PipePieceDefinition> catalog = new List<PipePieceDefinition>();
        private int selectedIndex = -1;
        private PipePieceDefinition activePiece => (selectedIndex >= 0 && selectedIndex < catalog.Count) ? catalog[selectedIndex] : null;

        private GameObject ghostInstance;
        private PipeSocket targetSceneSocket;
        private PipeSocket activeGhostSocket;
        private float currentRollAngle = 0f;
        private int activeSocketIndex = 0; // Tracks active connecting branch for TAB cycling

        [Header("Settings")]
        private bool isToolActive = false;
        private bool showOpenSockets = true;
        private float snapRadius = 1.5f;
        
        [Header("PS3 Aesthetic Controls")]
        private bool enablePS3Quantize = false;
        private float quantizeStep = 0.05f; // Coarse grid precision

        [MenuItem("Tools/Pipe System/Scene Placement Tool")]
        public static void ShowWindow()
        {
            GetWindow<PipePlacementWindow>("Pipe Placement");
        }

        private void OnEnable()
        {
            RefreshCatalog();
            // Safety unsubscribe prevents double-firing if Unity recompiles while open
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            DestroyGhost();
        }

        private void RefreshCatalog()
        {
            catalog.Clear();
            string[] guids = AssetDatabase.FindAssets("t:PipePieceDefinition");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PipePieceDefinition def = AssetDatabase.LoadAssetAtPath<PipePieceDefinition>(path);
                if (def != null) catalog.Add(def);
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Level Dressing Toolbar (Tool 4b)", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            isToolActive = GUILayout.Toggle(isToolActive, isToolActive ? "TOOL ACTIVE (Press ESC to Exit)" : "Activate Placement Tool", "Button", GUILayout.Height(35));
            if (EditorGUI.EndChangeCheck())
            {
                if (!isToolActive) DestroyGhost();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            showOpenSockets = EditorGUILayout.Toggle("Highlight Open Sockets", showOpenSockets);
            snapRadius = EditorGUILayout.Slider("Snap Distance", snapRadius, 0.5f, 5f);

            EditorGUILayout.Space();
            GUILayout.Label("PS3-Era Rendering Constraints", EditorStyles.boldLabel);
            enablePS3Quantize = EditorGUILayout.Toggle("Quantize Transforms (Jitter)", enablePS3Quantize);
            if (enablePS3Quantize)
            {
                quantizeStep = EditorGUILayout.FloatField("Precision Step", quantizeStep);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Catalog")) RefreshCatalog();

            EditorGUILayout.Space();
            GUILayout.Label("Piece Palette", EditorStyles.boldLabel);

            if (catalog.Count == 0)
            {
                EditorGUILayout.HelpBox("No PipePieceDefinitions found in project.", MessageType.Warning);
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                GUIStyle style = (i == selectedIndex) ? new GUIStyle(GUI.skin.button) { normal = { textColor = Color.yellow } } : GUI.skin.button;
                if (GUILayout.Button($"[{catalog[i].diameterCategory}] {catalog[i].displayName}", style))
                {
                    selectedIndex = i;
                    currentRollAngle = 0f;
                    UpdateGhostPrefab();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Hotkeys in SceneView:\n• [Left Click] Confirm Snap Placement\n• [Shift + Left Click] Free-Place in air\n• [TAB] Cycle active connecting socket\n• [R] or [Scroll Wheel] Rotate 90°\n• [ESC] Abort / Deactivate Tool", MessageType.Info);
        }

        private void UpdateGhostPrefab()
        {
            DestroyGhost();
            if (!isToolActive || activePiece == null || activePiece.prefab == null) return;

            ghostInstance = (GameObject)PrefabUtility.InstantiatePrefab(activePiece.prefab);
            ghostInstance.hideFlags = HideFlags.HideAndDontSave;

            // Disable colliders on ghost so raycasts ignore it
            foreach (var col in ghostInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }
        }

        private void DestroyGhost()
        {
            if (ghostInstance != null)
            {
                DestroyImmediate(ghostInstance);
                ghostInstance = null;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (showOpenSockets) DrawOpenSocketMarkers();
            if (!isToolActive || activePiece == null) return;

            Event e = Event.current;

            // Prevent Unity's default selection tool from hijacking left-clicks
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            // Handle ESC to abort
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                isToolActive = false;
                DestroyGhost();
                Repaint();
                e.Use();
                return;
            }

            // Handle Rotation Hotkey (R or Scroll)
            if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.R) || (e.type == EventType.ScrollWheel && e.modifiers == EventModifiers.None))
            {
                float delta = (e.type == EventType.ScrollWheel) ? (e.delta.y > 0 ? 90f : -90f) : 90f;
                currentRollAngle = (currentRollAngle + delta) % 360f;
                e.Use();
            }

            // Handle TAB Hotkey to cycle which socket on a multi-socket piece connects to the anchor
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab)
            {
                activeSocketIndex++;
                e.Use();
            }

            if (ghostInstance == null) UpdateGhostPrefab();

            // Find nearest compatible open socket in scene
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            targetSceneSocket = FindNearestCompatibleSocket(mouseRay);

            if (targetSceneSocket != null)
            {
                // Select active socket on the ghost (controlled by TAB cycling)
                activeGhostSocket = GetCompatibleGhostSocket(targetSceneSocket.category);

                if (activeGhostSocket != null)
                {
                    AlignPieceToSocket(ghostInstance, targetSceneSocket, activeGhostSocket, currentRollAngle);

                    // Visual feedback for active snap
                    Handles.color = Color.green;
                    Handles.DrawLine(targetSceneSocket.transform.position, activeGhostSocket.transform.position, 4f);
                    
                    // Highlight the specific branch socket being used as the anchor point
                    Handles.color = Color.cyan;
                    Handles.SphereHandleCap(0, activeGhostSocket.transform.position, Quaternion.identity, 0.35f, EventType.Repaint);
                }
            }
            else
            {
                // Fallback: Raycast to physics colliders or imaginary Y=0 ground plane
                if (Physics.Raycast(mouseRay, out RaycastHit hit))
                {
                    ghostInstance.transform.position = hit.point;
                    ghostInstance.transform.rotation = Quaternion.Euler(0, currentRollAngle, 0);
                }
                else
                {
                    Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                    if (groundPlane.Raycast(mouseRay, out float enter))
                    {
                        ghostInstance.transform.position = mouseRay.GetPoint(enter);
                        ghostInstance.transform.rotation = Quaternion.Euler(0, currentRollAngle, 0);
                    }
                }
            }

            // Handle Placement
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (targetSceneSocket != null && activeGhostSocket != null)
                {
                    PlacePiece();
                    e.Use();
                }
                else if (e.shift) // Hold SHIFT to free-place in air without snapping
                {
                    GameObject freeInstance = (GameObject)PrefabUtility.InstantiatePrefab(activePiece.prefab);
                    Undo.RegisterCreatedObjectUndo(freeInstance, "Free Place Pipe");
                    freeInstance.transform.position = ghostInstance.transform.position;
                    freeInstance.transform.rotation = ghostInstance.transform.rotation;
                    Debug.Log($"Free-placed {activePiece.displayName} (Hold Shift used)");
                    e.Use();
                }
            }

            sceneView.Repaint();
        }

        private PipeSocket FindNearestCompatibleSocket(Ray ray)
        {
            // Updated for Unity 6: Use FindObjectsInactive.Exclude instead of FindObjectsSortMode
            PipeSocket[] allSockets = Object.FindObjectsByType<PipeSocket>(FindObjectsInactive.Exclude);
            PipeSocket closest = null;
            float minDist = snapRadius;

            foreach (var socket in allSockets)
            {
                if (socket.IsOccupied || socket.category != activePiece.diameterCategory) continue;
                if (socket.transform.IsChildOf(ghostInstance.transform)) continue;

                // Calculate distance from socket to mouse ray
                float dist = HandleUtility.DistancePointLine(socket.transform.position, ray.origin, ray.origin + ray.direction * 100f);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = socket;
                }
            }
            return closest;
        }

        private PipeSocket GetCompatibleGhostSocket(PipeDiameter targetCategory)
        {
            PipeSocket[] ghostSockets = ghostInstance.GetComponentsInChildren<PipeSocket>();
            if (ghostSockets.Length == 0) return null;

            // Collect all sockets matching our diameter
            List<PipeSocket> validSockets = new List<PipeSocket>();
            foreach (var s in ghostSockets)
            {
                if (s.category == targetCategory) validSockets.Add(s);
            }

            if (validSockets.Count == 0) return null;

            // Use active index (cycled by pressing TAB) to pick which opening connects
            int safeIndex = Mathf.Abs(activeSocketIndex) % validSockets.Count;
            return validSockets[safeIndex];
        }

        /// <summary>
        /// Exact 2-step alignment transform bypasses mesh pivot bugs.
        /// Uses 'pieceToAlign' parameter to prevent ghost preview orphaning.
        /// </summary>
        private void AlignPieceToSocket(GameObject pieceToAlign, PipeSocket target, PipeSocket source, float rollAngle)
        {
            Transform root = pieceToAlign.transform;

            // 1. Calculate Target Rotation: Opposite forward vectors + User roll angle
            Quaternion targetRot = Quaternion.LookRotation(-target.transform.forward, target.transform.up);
            targetRot *= Quaternion.AngleAxis(rollAngle, Vector3.forward);

            // Apply rotation offset from root to source socket
            Quaternion rootToSocketRot = Quaternion.Inverse(root.rotation) * source.transform.rotation;
            root.rotation = targetRot * Quaternion.Inverse(rootToSocketRot);

            // 2. Calculate Target Position including insertion offset
            Vector3 targetPos = target.transform.position + (target.transform.forward * target.insertionOffset);
            Vector3 socketOffsetFromRoot = source.transform.position - root.position;
            root.position = targetPos - socketOffsetFromRoot;

            // 3. Optional PS3 Vertex Quantization
            if (enablePS3Quantize)
            {
                root.position = new Vector3(
                    Mathf.Round(root.position.x / quantizeStep) * quantizeStep,
                    Mathf.Round(root.position.y / quantizeStep) * quantizeStep,
                    Mathf.Round(root.position.z / quantizeStep) * quantizeStep
                );
            }
        }

        private void PlacePiece()
        {
            // 1. Instantiate real prefab instance keeping asset connection intact
            GameObject newInstance = (GameObject)PrefabUtility.InstantiatePrefab(activePiece.prefab);
            Undo.RegisterCreatedObjectUndo(newInstance, "Place Pipe Piece");

            // 2. Find matching socket on the real instance
            PipeSocket realSourceSocket = null;
            PipeSocket[] newSockets = newInstance.GetComponentsInChildren<PipeSocket>();
            foreach (var s in newSockets)
            {
                if (s.name == activeGhostSocket.name) { realSourceSocket = s; break; }
            }
            if (realSourceSocket == null && newSockets.Length > 0) realSourceSocket = newSockets[0];

            // 3. Align the REAL instance cleanly without hijacking the ghostInstance variable
            AlignPieceToSocket(newInstance, targetSceneSocket, realSourceSocket, currentRollAngle);

            // 4. Establish bidirectional link
            Undo.RecordObject(targetSceneSocket, "Connect Socket");
            Undo.RecordObject(realSourceSocket, "Connect Socket");
            targetSceneSocket.Connect(realSourceSocket);

            // 5. Mark scene dirty
            EditorUtility.SetDirty(targetSceneSocket);
            EditorUtility.SetDirty(realSourceSocket);

            Debug.Log($"Placed {activePiece.displayName} connected to {targetSceneSocket.name}");
            
            // 6. Refresh preview cleanly
            UpdateGhostPrefab();
        }

        private void DrawOpenSocketMarkers()
        {
            // Updated for Unity 6: Use FindObjectsInactive.Exclude instead of FindObjectsSortMode
            PipeSocket[] allSockets = Object.FindObjectsByType<PipeSocket>(FindObjectsInactive.Exclude);
            foreach (var socket in allSockets)
            {
                if (!socket.IsOccupied && (ghostInstance == null || !socket.transform.IsChildOf(ghostInstance.transform)))
                {
                    Handles.color = Color.red;
                    Handles.SphereHandleCap(0, socket.transform.position, Quaternion.identity, 0.2f, EventType.Repaint);
                }
            }
        }
    }
}