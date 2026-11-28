using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Registers a Main.unity environment root as a film set in the Film Mode overlay: a <see cref="StudioSet"/>
    /// with auto-generated <see cref="StudioMark"/>s (named spots + NavMesh grid samples) and invisible camera
    /// occluders mirroring the root's collider-less meshes. Nothing is copied and Main.unity is never modified.
    /// </summary>
    public static class StudioSetImporter
    {
        public static string MainScenePath => FilmStudioSettings.instance.mainScenePath;

        private const int MaxNamedMarks = 8;
        private const int MaxMarks = 16;
        private const float MinMarkSpacing = 1.0f;
        private const float MaxGridHalfExtent = 20f;

        private static readonly Regex SpotNameRegex =
            new(@"(pos|point|spot|stand|mark|waypoint|seat|chair|bench|door|window|desk|counter|gate|booth|table)",
                RegexOptions.IgnoreCase);

        private static readonly Regex ExcludeNameRegex =
            new(@"(light|lamp|probe|decal|volume|collider|trigger|audio|sound|particle|vfx|fx_|surface|effect|spawn|coupon|handset|supply|marker|hit)", RegexOptions.IgnoreCase);

        private static int StandScore(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("stand") || n.Contains("waypoint") || n.Contains("mark")) return 3;
            if (n.Contains("pos") || n.Contains("point") || n.Contains("spot")) return 2;
            return 1;
        }

        public static StudioSet ImportSet(string envRootName, string setId)
        {
            if (!StudioSceneUtility.EnterFilmMode(out Scene studio)) return null;
            Scene main = StudioSceneUtility.MainScene;

            GameObject source = StudioSceneUtility.FindRoot(main, envRootName);
            if (source == null)
            {
                Debug.LogError($"[FilmStudio] Root '{envRootName}' not found in {MainScenePath}.");
                return null;
            }

            CopyLookSettings(main, studio);

            GameObject setsRoot = StudioSceneUtility.FindOrCreateRoot(studio, StudioSceneUtility.SetsRootName);
            StudioSet existing = StudioSceneUtility.FindSet(studio, setId);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var setGo = new GameObject(setId);
            setGo.transform.SetParent(setsRoot.transform, false);
            var set = setGo.AddComponent<StudioSet>();
            set.setId = setId;
            set.sourceRoot = source.name;
            set.description = $"Main.unity '{source.name}'. Edit this description to tell the director what the set is.";

            GenerateMarks(set, source);
            set.RefreshMarks();
            int occluders = BuildOccluders(set, source);
            string analysis = StudioSetAnalyzer.Analyze(set, rebuildOccluders: false);

            StudioSceneUtility.SaveStudio();
            Debug.Log($"[FilmStudio] Registered set '{setId}' on Main.unity '{envRootName}': {set.marks.Count} marks, {occluders} camera occluders. {analysis}");
            return set;
        }

        /// <summary>
        /// One-time migration of sets imported by the old copy-based importer: keeps marks, deletes the copied
        /// environment (Main.unity renders the real one), records <see cref="StudioSet.sourceRoot"/> and builds occluders.
        /// Reports how far the copy had drifted from Main so misaligned marks are caught.
        /// </summary>
        public static string MigrateCopiedSets()
        {
            if (!StudioSceneUtility.EnterFilmMode(out Scene studio)) return "cancelled";
            Scene main = StudioSceneUtility.MainScene;
            GameObject setsRoot = StudioSceneUtility.FindRoot(studio, StudioSceneUtility.SetsRootName);
            if (setsRoot == null) return "no --SETS root";

            var report = new System.Text.StringBuilder();
            foreach (StudioSet set in setsRoot.GetComponentsInChildren<StudioSet>(true))
            {
                var copies = new List<GameObject>();
                foreach (Transform child in set.transform)
                    if (child.name != "Marks" && child.name != FilmModeShared.OccludersName && child.GetComponentInChildren<StudioMark>(true) == null)
                        copies.Add(child.gameObject);

                if (string.IsNullOrEmpty(set.sourceRoot) && copies.Count > 0) set.sourceRoot = copies[0].name;
                GameObject source = StudioSceneUtility.FindRoot(main, set.sourceRoot);
                if (source == null)
                {
                    report.AppendLine($"{set.setId}: Main root '{set.sourceRoot}' not found; left untouched.");
                    continue;
                }

                string drift = copies.Count > 0 ? Drift(copies[0], source) : "no copy";
                foreach (GameObject copy in copies) Object.DestroyImmediate(copy);
                set.description = set.description?.Replace("Copied from Main.unity", "Main.unity");
                int occluders = BuildOccluders(set, source);
                set.RefreshMarks();
                report.AppendLine($"{set.setId}: removed {copies.Count} copied root(s), source '{source.name}', {occluders} occluders, copy→Main drift {drift}.");
            }
            StudioSceneUtility.EnsureRoots(studio);
            StudioSceneUtility.SaveStudio();
            string result = report.ToString().TrimEnd();
            Debug.Log($"[FilmStudio] Migrated sets to Film Mode overlay:\n{result}");
            return result;
        }

        /// <summary>Max renderer-centre offset between a copied set and its Main source (matched by hierarchy path).</summary>
        private static string Drift(GameObject copy, GameObject source)
        {
            float max = 0f;
            int matched = 0;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(r.transform, copy.transform);
                Transform other = string.IsNullOrEmpty(path) ? source.transform : source.transform.Find(path);
                if (other == null) continue;
                max = Mathf.Max(max, (other.position - r.transform.position).magnitude);
                if (++matched >= 200) break;
            }
            return matched == 0 ? "unknown (no matching renderers)" : $"{max:0.###}m over {matched} renderers";
        }

        // ── Marks ─────────────────────────────────────────────────────────────────

        private static void GenerateMarks(StudioSet set, GameObject env)
        {
            var marksRoot = new GameObject("Marks");
            marksRoot.transform.SetParent(set.transform, false);

            Bounds bounds = ComputeBounds(env);
            Vector3 center = bounds.center;
            var placed = new List<Vector3>();
            var usedIds = new HashSet<string>();
            var namedAnchors = new List<Transform>();

            foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
            {
                if (t == env.transform || !SpotNameRegex.IsMatch(t.name) || ExcludeNameRegex.IsMatch(t.name)) continue;
                if (t.GetComponent<Light>() != null || t.GetComponent<ReflectionProbe>() != null) continue;
                namedAnchors.Add(t);
            }
            // Explicit stand/pos/point transforms are better blocking spots than furniture.
            namedAnchors.Sort((a, b) => StandScore(b.name).CompareTo(StandScore(a.name)));

            // Named spots first (most meaningful for the director).
            int furniture = 0;
            foreach (Transform t in namedAnchors)
            {
                if (placed.Count >= MaxNamedMarks) break;
                bool isFurniture = StandScore(t.name) < 2;
                if (isFurniture && furniture >= 3) continue;
                if (!TryGround(t.position, out Vector3 ground)) continue;
                if (t.position.y - ground.y > 0.6f) continue; // prop/attachment point on a desk or wall, not a floor spot
                if (!FarEnough(ground, placed)) continue;
                Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                if (fwd.sqrMagnitude < 0.01f) fwd = (center - ground).WithY0();
                CreateMark(marksRoot.transform, set.setId, StudioCatalogBuilder.Slug(CleanName(t.name)), $"at '{t.name}'", ground, fwd, usedIds);
                placed.Add(ground);
                if (isFurniture) furniture++;
            }

            // Grid samples across the set footprint, at the floor height of the named marks.
            float floorY = bounds.center.y;
            if (placed.Count > 0)
            {
                var ys = placed.ConvertAll(p => p.y);
                ys.Sort();
                floorY = ys[ys.Count / 2];
            }
            float hx = Mathf.Min(bounds.extents.x, MaxGridHalfExtent);
            float hz = Mathf.Min(bounds.extents.z, MaxGridHalfExtent);
            const int grid = 6;
            for (int ix = 0; ix < grid && placed.Count < MaxMarks; ix++)
            for (int iz = 0; iz < grid && placed.Count < MaxMarks; iz++)
            {
                var p = new Vector3(center.x + Mathf.Lerp(-hx, hx, (ix + 0.5f) / grid),
                                    floorY,
                                    center.z + Mathf.Lerp(-hz, hz, (iz + 0.5f) / grid));
                if (!TryGround(p, out Vector3 ground) || !FarEnough(ground, placed)) continue;
                string near = NearestNamedObject(env, ground);
                Vector3 fwd = (center - ground).WithY0();
                CreateMark(marksRoot.transform, set.setId, "near_" + StudioCatalogBuilder.Slug(near), $"open floor near '{near}'", ground, fwd, usedIds);
                placed.Add(ground);
            }
        }

        internal static void CreateMark(Transform parent, string setId, string baseId, string description, Vector3 pos, Vector3 forward, HashSet<string> usedIds)
        {
            string id = $"{setId}_{baseId}";
            if (id.Length > 48) id = id.Substring(0, 48);
            string unique = id;
            for (int i = 2; usedIds.Contains(unique); i++) unique = $"{id}_{i}";
            usedIds.Add(unique);

            var go = new GameObject(unique);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (forward.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            var mark = go.AddComponent<StudioMark>();
            mark.markId = unique;
            mark.description = description;
        }

        internal static bool TryGround(Vector3 p, out Vector3 ground)
        {
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                ground = hit.position;
                return true;
            }
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit rh, 3f, ~0, QueryTriggerInteraction.Ignore) &&
                rh.normal.y > 0.7f)
            {
                ground = rh.point;
                return true;
            }
            ground = p;
            return false;
        }

        private static bool FarEnough(Vector3 p, List<Vector3> placed)
        {
            foreach (Vector3 q in placed)
                if ((q - p).sqrMagnitude < MinMarkSpacing * MinMarkSpacing) return false;
            return true;
        }

        private static string NearestNamedObject(GameObject env, Vector3 p)
        {
            string best = "open_area";
            float bestD = float.MaxValue;
            foreach (Renderer r in env.GetComponentsInChildren<Renderer>(true))
            {
                if (r.bounds.extents.magnitude > 3f || r is ParticleSystemRenderer) continue; // skip floors, walls, fog, terrain
                string n = CleanName(r.name);
                if (ExcludeNameRegex.IsMatch(n) || Regex.IsMatch(n, @"^(polysurface|mesh|object|cube|plane|default)", RegexOptions.IgnoreCase)) continue;
                float d = (r.bounds.center - p).WithY0().sqrMagnitude;
                if (d < bestD && d < 25f)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }

        internal static string CleanName(string n)
        {
            n = Regex.Replace(n, @"\s*\(\d+\)$", "");
            n = Regex.Replace(n, @"[_\s]*(LOD\d|\d+)$", "");
            return string.IsNullOrWhiteSpace(n) ? "prop" : n;
        }

        internal static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(root.transform.position, Vector3.one * 10f);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ── Look settings ─────────────────────────────────────────────────────────

        /// <summary>
        /// FilmBuilder's camera occlusion checks raycast against colliders. Main.unity's walls, shutters and props
        /// often have no collider, so the set gets invisible MeshCollider proxies (overlay-only, no renderer) at the
        /// same world transforms. Rebuilt from scratch each call; Main.unity is only read.
        /// </summary>
        internal static int BuildOccluders(StudioSet set, GameObject source)
        {
            Transform old = set.transform.Find(FilmModeShared.OccludersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(FilmModeShared.OccludersName);
            root.transform.SetParent(set.transform, false);

            int added = 0;
            foreach (MeshRenderer mr in source.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled || mr.GetComponent<Collider>() != null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mr.bounds;
                if (b.size.x < 0.15f && b.size.y < 0.15f && b.size.z < 0.15f) continue; // tiny props don't occlude
                var go = new GameObject(mr.name);
                go.transform.SetParent(root.transform, false);
                Transform t = mr.transform;
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                go.transform.localScale = t.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                // Glass/windows stay as colliders (actors can't walk through) but cameras may look through them.
                if (StudioSight.IsGlassName(mr.name) || StudioSight.IsTransparent(mr)) go.AddComponent<StudioSeeThrough>();
                added++;
            }
            return added;
        }

        /// <summary>Copies skybox/ambient/fog and the first global Volume from Main so the stage looks like the game.</summary>
        private static void CopyLookSettings(Scene main, Scene studio)
        {
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(main);
            Material sky = RenderSettings.skybox;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientSky = RenderSettings.ambientSkyColor, ambientEq = RenderSettings.ambientEquatorColor,
                  ambientGround = RenderSettings.ambientGroundColor, ambientLight = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;
            bool fog = RenderSettings.fog;
            Color fogColor = RenderSettings.fogColor;
            FogMode fogMode = RenderSettings.fogMode;
            float fogDensity = RenderSettings.fogDensity, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;

            SceneManager.SetActiveScene(studio);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEq;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            if (StudioSceneUtility.FindRoot(studio, "Global Volume") == null)
            {
                foreach (GameObject root in main.GetRootGameObjects())
                {
                    foreach (Volume v in root.GetComponentsInChildren<Volume>(true))
                    {
                        if (!v.isGlobal || v.sharedProfile == null) continue;
                        var go = new GameObject("Global Volume");
                        SceneManager.MoveGameObjectToScene(go, studio);
                        var nv = go.AddComponent<Volume>();
                        nv.isGlobal = true;
                        nv.priority = v.priority;
                        nv.weight = v.weight;
                        nv.sharedProfile = v.sharedProfile;
                        goto copiedVolume;
                    }
                }
                copiedVolume:;
            }

            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }

        // ── Menu ──────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Film Studio/Import Set...")]
        private static void OpenImportWindow() => StudioSetImportWindow.Open();

        [MenuItem("Tools/Film Studio/Migrate Copied Sets To Main Overlay")]
        private static void MenuMigrate() => MigrateCopiedSets();
    }

    internal static class Vector3Ext
    {
        public static Vector3 WithY0(this Vector3 v) => new(v.x, 0f, v.z);
    }

    /// <summary>Lists Main.unity environment roots and imports the chosen one.</summary>
    public class StudioSetImportWindow : EditorWindow
    {
        private readonly List<string> _roots = new();
        private Vector2 _scroll;

        public static void Open()
        {
            var w = GetWindow<StudioSetImportWindow>(true, "Film Studio - Import Set");
            w.minSize = new Vector2(380, 300);
            w.Refresh();
        }

        private void Refresh()
        {
            _roots.Clear();
            Scene main = SceneManager.GetSceneByPath(StudioSetImporter.MainScenePath);
            bool opened = false;
            if (!main.IsValid() || !main.isLoaded)
            {
                main = EditorSceneManager.OpenScene(StudioSetImporter.MainScenePath, OpenSceneMode.Additive);
                opened = true;
            }
            foreach (GameObject go in main.GetRootGameObjects())
                if (go.name.ToUpperInvariant().Contains("ENV")) _roots.Add(go.name);
            if (opened) EditorSceneManager.CloseScene(main, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Registers a Main.unity environment root as a film set: marks + camera occluders in the Film Mode overlay. Nothing is copied and Main.unity is not modified.", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (string root in _roots)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(root);
                    if (GUILayout.Button("Import", GUILayout.Width(80)))
                    {
                        string id = StudioCatalogBuilder.Slug(root.Replace("ENV", ""));
                        if (StudioSetImporter.ImportSet(root, id) != null) StudioCatalogBuilder.RefreshSets();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Refresh")) Refresh();
        }
    }
}
