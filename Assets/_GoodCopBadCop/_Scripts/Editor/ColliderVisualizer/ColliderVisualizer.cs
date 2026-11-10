using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoodCopBadCop.Editor.ColliderVisualization
{
    /// <summary>
    /// Draws every enabled 3D collider in the Scene view (without needing them selected) and lets you
    /// click a collider to select its GameObject. Clicking repeatedly on the same spot cycles through
    /// overlapping colliders along the mouse ray (e.g. nested triggers).
    /// Toggle from the Scene view overlay, the Good Cop Bad Cop menu, or Alt+Shift+C.
    /// </summary>
    [InitializeOnLoad]
    internal static class ColliderVisualizer
    {
        private const float PickControlDistance = 3f;
        private const float ClickDragTolerance = 6f;
        private const float HoverRefreshInterval = 0.1f;
        private const float MaxRayDistance = 100000f;

        private static readonly int ControlHash = "GCBC.ColliderVisualizer".GetHashCode();
        private static readonly List<Collider> colliders = new List<Collider>();
        private static readonly List<(Collider collider, float distance, Vector3 point)> hits = new List<(Collider, float, Vector3)>();
        private static readonly Dictionary<Mesh, Vector3[]> meshEdgeCache = new Dictionary<Mesh, Vector3[]>();
        private static readonly HashSet<GameObject> selectedSet = new HashSet<GameObject>();
        private static readonly Plane[] frustumPlanes = new Plane[6];

        private static bool cacheDirty = true;
        private static Collider hovered;
        private static Vector3 hoverPoint;
        private static Vector2 lastHoverMouse = new Vector2(float.NaN, float.NaN);
        private static double lastHoverTime;
        private static Vector2 mouseDownPosition;
        private static Vector2 lastClickPosition = new Vector2(float.NaN, float.NaN);
        private static int clickCycleIndex;

        private static Material fillMaterial;
        private static Mesh cubeMesh;
        private static Mesh sphereMesh;
        private static Mesh cylinderMesh;
        private static GUIStyle labelStyle;

        public static int LastDrawnCount { get; private set; }
        public static int TrackedCount => colliders.Count;

        static ColliderVisualizer()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorApplication.playModeStateChanged += _ => MarkDirty();
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => MarkDirty();
            PrefabStage.prefabStageOpened += _ => MarkDirty();
            PrefabStage.prefabStageClosing += _ => MarkDirty();
            ColliderVisualizerSettings.Changed += SceneView.RepaintAll;
        }

        public static void MarkDirty() => cacheDirty = true;

        public static void ClearMeshCache()
        {
            meshEdgeCache.Clear();
            SceneView.RepaintAll();
        }

        private static void RefreshCache()
        {
            cacheDirty = false;
            colliders.Clear();
            colliders.AddRange(StageUtility.GetCurrentStageHandle().FindComponentsOfType<Collider>());
        }

        private static void OnSceneGUI(SceneView view)
        {
            if (!ColliderVisualizerSettings.Enabled)
            {
                hovered = null;
                return;
            }

            if (cacheDirty) RefreshCache();

            var e = Event.current;
            var id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);

            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    if (EditorWindow.mouseOverWindow == view) UpdateHover(e.mousePosition, view);
                    if (hovered != null && CanPick(e)) HandleUtility.AddControl(id, PickControlDistance);
                    break;

                case EventType.MouseLeaveWindow:
                    if (hovered != null)
                    {
                        hovered = null;
                        view.Repaint();
                    }
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && HandleUtility.nearestControl == id && hovered != null && CanPick(e))
                    {
                        GUIUtility.hotControl = id;
                        mouseDownPosition = e.mousePosition;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        if ((e.mousePosition - mouseDownPosition).magnitude <= ClickDragTolerance) SelectAt(e);
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    Draw(view.camera);
                    break;
            }
        }

        private static bool CanPick(Event e)
        {
            if (e.alt || Tools.viewToolActive) return false;
            return ColliderVisualizerSettings.PickMode switch
            {
                ColliderPickMode.Click => true,
                ColliderPickMode.ActionKeyClick => EditorGUI.actionKey,
                _ => false
            };
        }

        #region Picking

        private static void UpdateHover(Vector2 mousePosition, SceneView view)
        {
            var now = EditorApplication.timeSinceStartup;
            if (mousePosition == lastHoverMouse && now - lastHoverTime < HoverRefreshInterval) return;
            lastHoverMouse = mousePosition;
            lastHoverTime = now;

            CollectHits(HandleUtility.GUIPointToWorldRay(mousePosition), view.camera.transform.position);
            var newHovered = hits.Count > 0 ? hits[0].collider : null;
            if (hits.Count > 0) hoverPoint = hits[0].point;

            if (newHovered != hovered)
            {
                hovered = newHovered;
                view.Repaint();
            }
        }

        private static void CollectHits(Ray ray, Vector3 cameraPosition)
        {
            hits.Clear();
            Physics.SyncTransforms();

            var maxDistance = ColliderVisualizerSettings.MaxDistance;
            var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
            var visibility = SceneVisibilityManager.instance;

            foreach (var c in colliders)
            {
                if (!PassesFilter(c)) continue;
                if (visibility.IsPickingDisabled(c.gameObject)) continue;
                if (c.bounds.SqrDistance(cameraPosition) > maxDistanceSqr) continue;
                if (c.Raycast(ray, out var hit, MaxRayDistance)) hits.Add((c, hit.distance, hit.point));
            }

            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        private static void SelectAt(Event e)
        {
            var view = SceneView.currentDrawingSceneView;
            var cameraPosition = view != null ? view.camera.transform.position : Vector3.zero;
            CollectHits(HandleUtility.GUIPointToWorldRay(e.mousePosition), cameraPosition);
            if (hits.Count == 0) return;

            var sameSpot = (e.mousePosition - lastClickPosition).sqrMagnitude < 9f;
            clickCycleIndex = sameSpot ? (clickCycleIndex + 1) % hits.Count : 0;
            lastClickPosition = e.mousePosition;

            var target = hits[clickCycleIndex].collider;
            var go = target.gameObject;
            hovered = target;
            hoverPoint = hits[clickCycleIndex].point;

            var additive = e.shift || (ColliderVisualizerSettings.PickMode == ColliderPickMode.Click && EditorGUI.actionKey);
            if (additive)
            {
                var selection = new List<Object>(Selection.objects);
                if (!selection.Remove(go)) selection.Add(go);
                Selection.objects = selection.ToArray();
            }
            else
            {
                Selection.activeGameObject = go;
            }
        }

        private static bool PassesFilter(Collider c)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) return false;
            if (c is TerrainCollider) return false;
            if (((1 << c.gameObject.layer) & ColliderVisualizerSettings.LayerMask) == 0) return false;
            if (c.isTrigger ? !ColliderVisualizerSettings.ShowTriggers : !ColliderVisualizerSettings.ShowSolid) return false;
            return !SceneVisibilityManager.instance.IsHidden(c.gameObject);
        }

        #endregion

        #region Drawing

        private static void Draw(Camera camera)
        {
            if (camera == null) return;
            EnsureResources();

            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            var cameraPosition = camera.transform.position;
            var maxDistance = ColliderVisualizerSettings.MaxDistance;
            var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;

            selectedSet.Clear();
            foreach (var go in Selection.gameObjects) selectedSet.Add(go);

            var previousZTest = Handles.zTest;
            var previousMatrix = Handles.matrix;
            var previousColor = Handles.color;
            var zTest = ColliderVisualizerSettings.XRay ? CompareFunction.Always : CompareFunction.LessEqual;
            Handles.zTest = zTest;
            if (fillMaterial != null) fillMaterial.SetInt("_ZTest", (int)zTest);

            var drawn = 0;
            foreach (var c in colliders)
            {
                if (!PassesFilter(c)) continue;
                var bounds = c.bounds;
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) continue;
                if (bounds.SqrDistance(cameraPosition) > maxDistanceSqr) continue;

                DrawCollider(c, GetColor(c), c == hovered, camera);
                drawn++;
            }

            LastDrawnCount = drawn;
            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;

            if (hovered != null && ColliderVisualizerSettings.ShowHoverLabel && PassesFilter(hovered)) DrawHoverLabel();
        }

        private static Color GetColor(Collider c)
        {
            if (c == hovered) return ColliderVisualizerSettings.HoverColor;
            if (selectedSet.Contains(c.gameObject)) return ColliderVisualizerSettings.SelectedColor;
            if (c.isTrigger) return ColliderVisualizerSettings.TriggerColor;
            var body = c.attachedRigidbody;
            return body != null && !body.isKinematic ? ColliderVisualizerSettings.RigidbodyColor : ColliderVisualizerSettings.SolidColor;
        }

        private static void DrawCollider(Collider c, Color color, bool emphasize, Camera camera)
        {
            var fillColor = color;
            fillColor.a = ColliderVisualizerSettings.DrawFill ? Mathf.Clamp01(ColliderVisualizerSettings.FillAlpha * (emphasize ? 3f : 1f)) : 0f;
            Handles.color = color;
            var t = c.transform;

            switch (c)
            {
                case BoxCollider box:
                    DrawBox(t.localToWorldMatrix, box.center, box.size, fillColor);
                    break;

                case SphereCollider sphere:
                {
                    var s = Abs(t.lossyScale);
                    DrawSphere(t.TransformPoint(sphere.center), t.rotation, sphere.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z)), fillColor, camera);
                    break;
                }

                case CapsuleCollider capsule:
                {
                    GetCapsule(t, capsule.center, capsule.radius, capsule.height, capsule.direction, out var center, out var rotation, out var radius, out var height);
                    DrawCapsule(center, rotation, radius, height, fillColor, camera);
                    break;
                }

                case CharacterController controller:
                {
                    GetCapsule(t, controller.center, controller.radius, controller.height, 1, out var center, out var rotation, out var radius, out var height);
                    DrawCapsule(center, rotation, radius, height, fillColor, camera);
                    break;
                }

                case MeshCollider meshCollider:
                    DrawMeshCollider(meshCollider, fillColor);
                    break;

                case WheelCollider wheel:
                {
                    var s = Abs(t.lossyScale);
                    Handles.matrix = Matrix4x4.identity;
                    Handles.DrawWireDisc(t.TransformPoint(wheel.center), t.right, wheel.radius * Mathf.Max(s.y, s.z));
                    break;
                }

                default:
                    DrawWorldBounds(c.bounds);
                    break;
            }
        }

        private static void DrawBox(Matrix4x4 matrix, Vector3 center, Vector3 size, Color fillColor)
        {
            Handles.matrix = matrix;
            Handles.DrawWireCube(center, size);
            DrawFill(cubeMesh, matrix * Matrix4x4.TRS(center, Quaternion.identity, size), fillColor);
        }

        private static void DrawWorldBounds(Bounds bounds)
        {
            Handles.matrix = Matrix4x4.identity;
            Handles.DrawWireCube(bounds.center, bounds.size);
        }

        private static void DrawSphere(Vector3 center, Quaternion rotation, float radius, Color fillColor, Camera camera)
        {
            Handles.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.right, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);

            Handles.matrix = Matrix4x4.identity;
            DrawSphereSilhouette(center, radius, camera);
            DrawFill(sphereMesh, Matrix4x4.TRS(center, rotation, Vector3.one * (radius * 2f)), fillColor);
        }

        private static void DrawSphereSilhouette(Vector3 center, float radius, Camera camera)
        {
            if (camera.orthographic)
            {
                Handles.DrawWireDisc(center, -camera.transform.forward, radius);
                return;
            }

            var toCamera = camera.transform.position - center;
            var distance = toCamera.magnitude;
            if (distance <= radius) return;

            var direction = toCamera / distance;
            var silhouetteRadius = radius * Mathf.Sqrt(distance * distance - radius * radius) / distance;
            Handles.DrawWireDisc(center + direction * (radius * radius / distance), direction, silhouetteRadius);
        }

        private static void DrawCapsule(Vector3 center, Quaternion rotation, float radius, float height, Color fillColor, Camera camera)
        {
            var half = Mathf.Max(0f, height * 0.5f - radius);
            var top = Vector3.up * half;
            var bottom = -top;

            Handles.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Handles.DrawWireDisc(top, Vector3.up, radius);
            Handles.DrawWireDisc(bottom, Vector3.up, radius);
            Handles.DrawLine(bottom + Vector3.right * radius, top + Vector3.right * radius);
            Handles.DrawLine(bottom - Vector3.right * radius, top - Vector3.right * radius);
            Handles.DrawLine(bottom + Vector3.forward * radius, top + Vector3.forward * radius);
            Handles.DrawLine(bottom - Vector3.forward * radius, top - Vector3.forward * radius);
            Handles.DrawWireArc(top, Vector3.forward, Vector3.right, 180f, radius);
            Handles.DrawWireArc(bottom, Vector3.forward, -Vector3.right, 180f, radius);
            Handles.DrawWireArc(top, -Vector3.right, Vector3.forward, 180f, radius);
            Handles.DrawWireArc(bottom, Vector3.right, Vector3.forward, 180f, radius);

            if (fillColor.a <= 0f) return;
            var up = rotation * Vector3.up;
            var sphereScale = Vector3.one * (radius * 2f);
            DrawFill(sphereMesh, Matrix4x4.TRS(center + up * half, rotation, sphereScale), fillColor);
            DrawFill(sphereMesh, Matrix4x4.TRS(center - up * half, rotation, sphereScale), fillColor);
            if (half > 0f) DrawFill(cylinderMesh, Matrix4x4.TRS(center, rotation, new Vector3(radius * 2f, half, radius * 2f)), fillColor);
        }

        private static void DrawMeshCollider(MeshCollider meshCollider, Color fillColor)
        {
            var mesh = meshCollider.sharedMesh;
            if (mesh == null) return;

            var matrix = meshCollider.transform.localToWorldMatrix;
            var edges = GetMeshEdges(mesh);
            if (edges != null)
            {
                Handles.matrix = matrix;
                Handles.DrawLines(edges);
            }
            else
            {
                DrawWorldBounds(meshCollider.bounds);
            }

            DrawFill(mesh, matrix, fillColor);
        }

        private static void DrawFill(Mesh mesh, Matrix4x4 matrix, Color color)
        {
            if (color.a <= 0f || mesh == null || fillMaterial == null) return;
            fillMaterial.SetColor("_Color", color);
            fillMaterial.SetPass(0);
            for (var i = 0; i < mesh.subMeshCount; i++) Graphics.DrawMeshNow(mesh, matrix, i);
        }

        private static void DrawHoverLabel()
        {
            labelStyle ??= new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = true };

            var type = hovered.GetType().Name;
            var trigger = hovered.isTrigger ? " (Trigger)" : string.Empty;
            var hint = ColliderVisualizerSettings.PickMode switch
            {
                ColliderPickMode.Click => "\n<i>Click to select, click again to cycle</i>",
                ColliderPickMode.ActionKeyClick => $"\n<i>{(Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl")}+Click to select</i>",
                _ => string.Empty
            };

            Handles.matrix = Matrix4x4.identity;
            Handles.Label(hoverPoint, $"<b>{hovered.gameObject.name}</b>\n{type}{trigger}  |  {LayerMask.LayerToName(hovered.gameObject.layer)}{hint}", labelStyle);
        }

        #endregion

        #region Helpers

        private static void GetCapsule(Transform t, Vector3 localCenter, float localRadius, float localHeight, int direction,
            out Vector3 center, out Quaternion rotation, out float radius, out float height)
        {
            var s = Abs(t.lossyScale);
            float axisScale;
            float radiusScale;
            Quaternion axisRotation;

            switch (direction)
            {
                case 0:
                    axisScale = s.x;
                    radiusScale = Mathf.Max(s.y, s.z);
                    axisRotation = Quaternion.Euler(0f, 0f, -90f);
                    break;
                case 2:
                    axisScale = s.z;
                    radiusScale = Mathf.Max(s.x, s.y);
                    axisRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                default:
                    axisScale = s.y;
                    radiusScale = Mathf.Max(s.x, s.z);
                    axisRotation = Quaternion.identity;
                    break;
            }

            radius = localRadius * radiusScale;
            height = Mathf.Max(localHeight * axisScale, radius * 2f);
            center = t.TransformPoint(localCenter);
            rotation = t.rotation * axisRotation;
        }

        private static Vector3[] GetMeshEdges(Mesh mesh)
        {
            if (meshEdgeCache.TryGetValue(mesh, out var cached)) return cached;

            Vector3[] result = null;
            try
            {
                using (var dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = dataArray[0];
                    var triangleCount = 0;
                    for (var i = 0; i < data.subMeshCount; i++)
                    {
                        var desc = data.GetSubMesh(i);
                        if (desc.topology == MeshTopology.Triangles) triangleCount += desc.indexCount / 3;
                    }

                    if (triangleCount > 0 && triangleCount <= ColliderVisualizerSettings.MaxMeshTriangles)
                    {
                        var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                        data.GetVertices(vertices);

                        var seen = new HashSet<long>();
                        var lines = new List<Vector3>(triangleCount * 4);
                        for (var sub = 0; sub < data.subMeshCount; sub++)
                        {
                            var desc = data.GetSubMesh(sub);
                            if (desc.topology != MeshTopology.Triangles) continue;

                            var indices = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                            data.GetIndices(indices, sub);
                            for (var i = 0; i + 2 < indices.Length; i += 3)
                            {
                                AddEdge(indices[i], indices[i + 1]);
                                AddEdge(indices[i + 1], indices[i + 2]);
                                AddEdge(indices[i + 2], indices[i]);
                            }
                            indices.Dispose();
                        }

                        result = lines.ToArray();
                        vertices.Dispose();

                        void AddEdge(int a, int b)
                        {
                            var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                            if (!seen.Add(key)) return;
                            lines.Add(vertices[a]);
                            lines.Add(vertices[b]);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ColliderVisualizer] Could not read mesh '{mesh.name}' for wireframe, drawing bounds instead. {ex.Message}");
                result = null;
            }

            meshEdgeCache[mesh] = result;
            return result;
        }

        private static void EnsureResources()
        {
            if (fillMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    fillMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    fillMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    fillMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    fillMaterial.SetInt("_Cull", (int)CullMode.Off);
                    fillMaterial.SetInt("_ZWrite", 0);
                }
            }

            if (cubeMesh == null) cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (sphereMesh == null) sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            if (cylinderMesh == null) cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        #endregion
    }
}
