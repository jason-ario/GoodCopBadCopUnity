using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Gives the director (and the camera solver) an understanding of a set's environment:
    /// <list type="bullet">
    /// <item>Points of interest (<see cref="StudioPoint"/>) recognised from Main.unity object names: windows/glass, shutters,
    /// doors, counters, bells, phones, screens, posters, props.</item>
    /// <item>Story marks found anywhere in Main.unity around the set (e.g. the suspect's spot outside the booth window, which
    /// lives under the suspect controller rather than the environment root). Existing marks are never removed or renamed.</item>
    /// <item>Per-mark context: facing, what is ahead (incl. through glass), open space per side, indoors/outdoors, nearby points.</item>
    /// <item>Camera spots (<see cref="StudioCameraSpot"/>) with a validated sight line to each story mark at head height.</item>
    /// </list>
    /// Re-runnable: generated points/spots are rebuilt, mark contexts refreshed, hand edits to descriptions are kept.
    /// </summary>
    public static class StudioSetAnalyzer
    {
        public const string PointsRootName = "__Points";
        public const string SpotsRootName = "__CameraSpots";

        /// <summary>Typical head height of the game's (large-imported) characters, used for sight lines and spot heights.</summary>
        private const float EyeHeight = 2.2f;
        private const float ChestHeight = 1.5f;
        private const float SceneMarkMargin = 3.5f;
        private const int MaxSceneMarks = 6;
        private const int MaxPoints = 32;
        private const int MaxSpotsPerMark = 6;
        private const int MaxSpotMarks = 8;

        private struct PointRule
        {
            public Regex match;
            public string kind;
            public string label;
            public string hint;
            public int priority;
            public bool seeThrough;

            public PointRule(string pattern, string kind, string label, string hint, int priority, bool seeThrough = false)
            {
                match = new Regex(pattern, RegexOptions.IgnoreCase);
                this.kind = kind; this.label = label; this.hint = hint; this.priority = priority; this.seeThrough = seeThrough;
            }
        }

        // Order matters: first match wins. Lower priority number = more important (kept first when capping).
        private static readonly PointRule[] Rules =
        {
            new(@"(window\s*pane|breakable\s*glass|booth\s*glass|purchase\s*glass|\bglass\b(?!.*(shot|cup|dirt|lamp)))", "opening", "window glass", "transparent; shoot through it, faces read behind it", 0, true),
            new(@"(rolling\s*shutter|shutter)", "opening", "rolling shutter", "metal shutter over the window; can slam down", 0),
            new(@"(window\s*blocker|window(?!\s*lamp))", "opening", "window", "window opening", 1),
            new(@"(door)", "opening", "door", "door; entrances and exits", 1),
            new(@"(table\s*bell|\bbell\b)", "prop", "table bell", "ring it for a ding", 1),
            new(@"(stamp)", "prop", "rubber stamps", "approve/deny stamps", 1),
            new(@"(x\s*ray)", "prop", "x-ray machine", "scanner", 1),
            new(@"(lever)", "prop", "lever", "pull lever", 2),
            new(@"(call\s*suspect\s*button|button)", "prop", "button", "push button", 2),
            new(@"(telephone|phone)", "prop", "telephone", "", 2),
            new(@"(quarantine\s*board|polaroid|wanted|poster|propag|calendar|newspaper|\bsign\b|notice)", "sign", "poster board", "readable wall details", 2),
            new(@"(security\s*camera|entrance\s*camera|cctv)", "prop", "security camera", "surveillance POV", 2),
            new(@"(\bpc\b|monitor|computer|\btv\b|screen)", "screen", "screen", "glowing screen", 2),
            new(@"(folder|clipboard|id\s*card|document|passport|paper)", "prop", "documents", "papers on the counter", 2),
            new(@"(counter|office\s*desk|\bdesk\b|\btable\b)", "surface", "counter", "work surface", 3),
            new(@"(chair|stool|bench)", "prop", "chair", "", 3),
            new(@"(radio|fax|speaker|fan\b|coffee|mug|ashtray|flashlight|time\s*card|light\s*switch|\batm\b|fridge|trash|mop|bucket|\bcat\b|cardboard|box)", "prop", "", "", 4),
            new(@"(cabinet|drawer|shel(f|ving)|locker)", "prop", "", "furniture", 5),
        };

        private static readonly Regex SceneMarkName = new(@"(stand|\bpos\b|\bpos\s|position|\bpoint\b|\bspot\b|waypoint|follow)", RegexOptions.IgnoreCase);
        private static readonly Regex SceneMarkExclude = new(@"(look|spawn|camera|\bcam\b|zoom|slot|attack|light|audio|sound|trigger|hit|hold|ui\b|canvas|menu|hud|cleanup|fx|effect|reappear)", RegexOptions.IgnoreCase);
        private static readonly Regex PointExclude = new(@"(toilet|radiation|cylinder|alarm|placer|hold\s*point)", RegexOptions.IgnoreCase);
        private static readonly Regex UiAncestor = new(@"(menu|canvas|\bui\b|hud|screen\s*space)", RegexOptions.IgnoreCase);

        // ── Entry points ──────────────────────────────────────────────────────────

        [MenuItem("Tools/Film Studio/Analyze All Sets (points, marks, camera spots)")]
        private static void MenuAnalyzeAll()
        {
            if (!StudioSceneUtility.EnsureOpen(out Scene overlay)) return;
            GameObject setsRoot = StudioSceneUtility.FindRoot(overlay, StudioSceneUtility.SetsRootName);
            if (setsRoot == null) return;
            foreach (StudioSet set in setsRoot.GetComponentsInChildren<StudioSet>(true))
                Debug.Log($"[FilmStudio] {set.setId}: {Analyze(set)}");
            StudioCatalogBuilder.RefreshSets();
        }

        /// <summary>Analyzes one set in the open Film Mode overlay. Returns a one-line summary.</summary>
        public static string Analyze(StudioSet set, bool rebuildOccluders = true)
        {
            if (set == null) return "no set";
            Scene main = StudioSceneUtility.MainScene;
            if (!main.IsValid() || !main.isLoaded) return "Main.unity is not loaded (enter Film Mode first)";
            if (string.IsNullOrWhiteSpace(set.sourceRoot))
            {
                // Sets from the old copy-based importer only name their source in the description ("... Main.unity '---ENV_Booth' ...").
                Match named = Regex.Match(set.description ?? "", @"'([^']*ENV[^']*)'");
                if (named.Success && StudioSceneUtility.FindRoot(main, named.Groups[1].Value) != null) set.sourceRoot = named.Groups[1].Value;
            }
            GameObject env = StudioSceneUtility.FindRoot(main, set.sourceRoot);
            if (env == null) return $"Main.unity root '{set.sourceRoot}' not found (set StudioSet.sourceRoot)";

            bool wasActive = set.gameObject.activeSelf;
            set.gameObject.SetActive(true); // occluders must be live for physics queries
            try
            {
                int occluders = rebuildOccluders ? StudioSetImporter.BuildOccluders(set, env) : -1;
                Physics.SyncTransforms();
                set.RefreshMarks();

                Bounds bounds = CoreArea(set, env);
                int addedMarks = AddSceneMarks(set, env, main, bounds);
                set.RefreshMarks();
                int pruned = PruneFarGridMarks(set);
                if (pruned > 0) set.RefreshMarks();

                int points = GeneratePoints(set, env, main, CoreArea(set, env));
                set.RefreshMarks();

                DescribeMarks(set);
                int spots = GenerateCameraSpots(set);
                set.RefreshMarks();

                if (string.IsNullOrWhiteSpace(set.description) || set.description.Contains("Edit this description") || set.description.Contains("(auto-described"))
                    set.description = AutoDescription(set);

                EditorUtility.SetDirty(set);
                StudioSceneUtility.SaveStudio();
                return $"{(occluders >= 0 ? $"{occluders} occluders, " : "")}{addedMarks} new marks, {pruned} far grid marks removed ({set.marks.Count} total), {points} points, {spots} camera spots";
            }
            finally
            {
                set.gameObject.SetActive(wasActive);
            }
        }

        // ── Areas and mark classification ─────────────────────────────────────────

        /// <summary>
        /// Where the set's action happens: the floor-level story marks (named spots, not grid samples) plus a margin.
        /// Environment roots often contain far-away dressing, so their renderer bounds are far too big to scan.
        /// </summary>
        private static Bounds CoreArea(StudioSet set, GameObject env)
        {
            var story = StoryFloorMarks(set);
            if (story.Count == 0) story = set.marks.Where(m => m != null && IsStoryMark(set, m)).ToList();
            if (story.Count == 0) return StudioSetImporter.ComputeBounds(env);
            var b = new Bounds(story[0].transform.position, Vector3.zero);
            foreach (StudioMark m in story) b.Encapsulate(m.transform.position);
            b.Expand(new Vector3(SceneMarkMargin * 2f, 0f, SceneMarkMargin * 2f));
            b.SetMinMax(new Vector3(b.min.x, b.min.y - 1f, b.min.z), new Vector3(b.max.x, b.min.y + 6f, b.max.z));
            return b;
        }

        /// <summary>
        /// Removes auto-generated grid marks ("{set}_near_*") that the old importer scattered far outside the story area.
        /// They mislead the director into blocking scenes in empty dressing. Named story marks are never touched.
        /// </summary>
        private static int PruneFarGridMarks(StudioSet set, float maxDistance = 15f)
        {
            var story = StoryFloorMarks(set).Select(m => m.transform.position).ToList();
            if (story.Count == 0) return 0;
            int removed = 0;
            foreach (StudioMark m in set.marks.ToList())
            {
                if (m == null || IsStoryMark(set, m)) continue;
                Vector3 p = m.transform.position;
                if (story.Min(s => (s - p).magnitude) <= maxDistance) continue;
                Undo.DestroyObjectImmediate(m.gameObject);
                removed++;
            }
            return removed;
        }

        private static bool IsStoryMark(StudioSet set, StudioMark m) => !m.markId.StartsWith($"{set.setId}_near_");

        /// <summary>True if the position stands on walkable floor (not on a desk, wall attachment or in the air).</summary>
        private static bool IsFloorMark(Vector3 p, out float heightAboveFloor)
        {
            heightAboveFloor = 0f;
            // The NavMesh knows real floors (desks, roofs and props are not baked as walkable floor in this project).
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit nav, 6f, UnityEngine.AI.NavMesh.AllAreas) &&
                (nav.position - p).WithY0().magnitude < 1.2f &&
                nav.position.y <= p.y + 0.5f) // a walkable roof above the mark is not its floor
            {
                heightAboveFloor = p.y - nav.position.y;
                return Mathf.Abs(heightAboveFloor) < 0.5f;
            }
            // Walk down through shelves/desks to the first upward surface that is walkable NavMesh floor.
            RaycastHit[] downHits = Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit dh in downHits.OrderBy(h => h.distance))
            {
                if (dh.normal.y < 0.7f) continue;
                if (UnityEngine.AI.NavMesh.SamplePosition(dh.point, out UnityEngine.AI.NavMeshHit dn, 0.4f, UnityEngine.AI.NavMesh.AllAreas) &&
                    Mathf.Abs(dn.position.y - dh.point.y) < 0.3f)
                {
                    heightAboveFloor = p.y - dh.point.y;
                    return heightAboveFloor < 0.5f;
                }
            }
            if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // Desks and counters are floors for the raycast too, so compare with the lowest surface below.
                float floorY = hit.point.y;
                if (Physics.Raycast(hit.point + Vector3.down * 0.05f, Vector3.down, out RaycastHit lower, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && lower.normal.y > 0.7f && hit.point.y - lower.point.y > 0.45f)
                    floorY = lower.point.y;
                heightAboveFloor = p.y - floorY;
                return heightAboveFloor < 0.5f;
            }
            return StudioSetImporter.TryGround(p, out Vector3 g) && (heightAboveFloor = p.y - g.y) < 0.5f;
        }

        // ── Story marks from the whole scene ──────────────────────────────────────

        private static int AddSceneMarks(StudioSet set, GameObject env, Scene main, Bounds bounds)
        {
            Transform marksRoot = set.transform.Find("Marks");
            if (marksRoot == null) { marksRoot = new GameObject("Marks").transform; marksRoot.SetParent(set.transform, false); }

            var usedIds = new HashSet<string>(set.marks.Where(m => m != null).Select(m => m.markId));
            var existing = set.marks.Where(m => m != null).Select(m => m.transform.position).ToList();
            Bounds area = bounds;

            var candidates = new List<(Transform t, Vector3 ground, int score)>();
            foreach (GameObject root in main.GetRootGameObjects())
            {
                if (root == env) continue; // the importer already scanned the environment root
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t is RectTransform || !SceneMarkName.IsMatch(t.name) || SceneMarkExclude.IsMatch(t.name)) continue;
                    if (!area.Contains(t.position) || HasUiAncestor(t)) continue;
                    if (t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null) continue;
                    if (!StudioSetImporter.TryGround(t.position, out Vector3 ground) || Mathf.Abs(t.position.y - ground.y) > 0.6f) continue;
                    if (!IsFloorMark(ground, out _)) continue;
                    string anc = AncestorPath(t, 4);
                    int score = Regex.IsMatch(anc, "suspect|interrog|queue", RegexOptions.IgnoreCase) ? 3
                              : Regex.IsMatch(anc, "tutorial|cutscene|intro", RegexOptions.IgnoreCase) ? 0 : 1;
                    candidates.Add((t, ground, score));
                }
            }

            int added = 0;
            foreach (var c in candidates.OrderByDescending(c => c.score))
            {
                if (added >= MaxSceneMarks) break;
                if (existing.Any(p => (p - c.ground).sqrMagnitude < 0.8f * 0.8f)) continue;
                Vector3 fwd = Vector3.ProjectOnPlane(c.t.forward, Vector3.up);
                if (fwd.sqrMagnitude < 0.01f) fwd = (bounds.center - c.ground).WithY0();
                string path = AncestorPath(c.t, 3);
                string desc = $"at '{c.t.name}' ({path})";
                if (Regex.IsMatch(path, "suspect", RegexOptions.IgnoreCase)) desc += " - suspect-controller gameplay spot";
                else if (c.score == 0) desc += " - tutorial/cutscene spot";
                StudioSetImporter.CreateMark(marksRoot, set.setId, StudioCatalogBuilder.Slug(StudioSetImporter.CleanName(c.t.name)), desc, c.ground, fwd, usedIds);
                existing.Add(c.ground);
                added++;
            }
            return added;
        }

        private static bool HasUiAncestor(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p is RectTransform || p.GetComponent<Canvas>() != null || UiAncestor.IsMatch(p.name)) return true;
            return false;
        }

        private static string AncestorPath(Transform t, int depth)
        {
            var parts = new List<string>();
            for (Transform p = t.parent; p != null && parts.Count < depth; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ── Points of interest ────────────────────────────────────────────────────

        private static int GeneratePoints(StudioSet set, GameObject env, Scene main, Bounds bounds)
        {
            Transform old = set.transform.Find(PointsRootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(PointsRootName).transform;
            root.SetParent(set.transform, false);

            Bounds area = bounds;

            // Highest matching ancestor per object, so "Table Bell/Dome" becomes one "Table Bell" point.
            var found = new List<(Transform t, PointRule rule, Bounds b)>();
            var claimed = new HashSet<Transform>();
            foreach (GameObject r in main.GetRootGameObjects())
            {
                foreach (Transform t in r.GetComponentsInChildren<Transform>(false))
                {
                    if (t == r.transform && r != env && t.childCount > 20) continue; // big containers aren't props
                    if (t is RectTransform || claimed.Contains(t) || IsUnderClaimed(t, claimed)) continue;
                    string clean = StudioSetImporter.CleanName(t.name);
                    if (PointExclude.IsMatch(clean)) continue;
                    int ri = MatchRule(clean);
                    if (ri < 0) continue;
                    if (!TryRenderBounds(t, out Bounds b) || !area.Contains(b.center)) continue;
                    if (b.extents.magnitude > 6f || HasUiAncestor(t)) continue; // whole buildings / walls / UI
                    claimed.Add(t);
                    found.Add((t, Rules[ri], b));
                }
            }

            var usedIds = new HashSet<string>(set.marks.Where(m => m != null).Select(m => m.markId));
            var placed = new List<(string label, Vector3 pos, float radius)>();
            var created = new List<(StudioPoint point, Bounds bounds, PointRule rule)>();
            int count = 0;
            // Most important kinds first; within a kind the biggest object wins (the real window pane over a small glass part).
            foreach (var f in found.OrderBy(f => f.rule.priority).ThenByDescending(f => f.b.extents.magnitude))
            {
                if (count >= MaxPoints) break;
                string name = StudioSetImporter.CleanName(f.t.name).Trim().TrimEnd('.');
                string label = string.IsNullOrEmpty(f.rule.label) ? name.ToLowerInvariant() : f.rule.label;
                // Merge duplicates (three drawers, many glass cells) into the biggest one nearby.
                if (placed.Any(p => p.label == label && (p.pos - f.b.center).magnitude < Mathf.Max(0.8f, p.radius * 0.8f))) continue;

                string slug = StudioCatalogBuilder.Slug(name);
                if (slug.StartsWith(set.setId + "_")) slug = slug.Substring(set.setId.Length + 1);
                string baseId = $"{set.setId}_{(string.IsNullOrEmpty(slug) ? StudioCatalogBuilder.Slug(label) : slug)}";
                string id = baseId;
                for (int i = 2; usedIds.Contains(id); i++) id = $"{baseId}_{i}";
                usedIds.Add(id);

                var go = new GameObject(id);
                go.transform.SetParent(root, false);
                go.transform.position = f.b.center;
                var p = go.AddComponent<StudioPoint>();
                p.pointId = id;
                p.kind = f.rule.kind;
                p.radius = Mathf.Clamp(Mathf.Max(f.b.extents.x, f.b.extents.y, f.b.extents.z), 0.05f, 2.5f);
                p.seeThrough = f.rule.seeThrough;
                string hint = string.IsNullOrEmpty(f.rule.hint) ? "" : $"; {f.rule.hint}";
                p.description = $"{label} ('{name}'){hint}; centre {f.b.center.y:0.0}m high, ~{p.radius * 2f:0.0}m across";
                placed.Add((label, f.b.center, p.radius));
                created.Add((p, f.b, f.rule));
                count++;
            }

            // Glass covered by a closed shutter / blocker in the scene is not see-through for films (Film Mode doesn't run
            // gameplay, so a shutter that opens at runtime stays down). Tell the director instead of promising a view.
            foreach (var g in created.Where(c => c.point.seeThrough))
            {
                foreach (var cover in created.Where(c => c.rule.kind == "opening" && !c.point.seeThrough && c.point != g.point))
                {
                    if (!cover.bounds.Intersects(g.bounds)) continue;
                    Bounds inter = Intersection(cover.bounds, g.bounds);
                    float covered = (inter.size.x * inter.size.y + inter.size.z * inter.size.y) / Mathf.Max(0.0001f, g.bounds.size.x * g.bounds.size.y + g.bounds.size.z * g.bounds.size.y);
                    if (covered < 0.5f) continue;
                    g.point.seeThrough = false;
                    g.point.description = $"{g.point.description.Replace("; transparent; shoot through it, faces read behind it", "")}; COVERED by {cover.point.pointId} (closed in the scene): not see-through in films";
                    cover.point.description += $"; CLOSED in the scene, covering {g.point.pointId}";
                    break;
                }
            }
            return count;
        }

        private static Bounds Intersection(Bounds a, Bounds b)
        {
            Vector3 min = Vector3.Max(a.min, b.min), max = Vector3.Min(a.max, b.max);
            var r = new Bounds();
            r.SetMinMax(min, Vector3.Max(min, max));
            return r;
        }

        private static bool IsUnderClaimed(Transform t, HashSet<Transform> claimed)
        {
            for (Transform p = t.parent; p != null; p = p.parent) if (claimed.Contains(p)) return true;
            return false;
        }

        private static int MatchRule(string name)
        {
            for (int i = 0; i < Rules.Length; i++) if (Rules[i].match.IsMatch(name)) return i;
            return -1;
        }

        private static bool TryRenderBounds(Transform t, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        // ── Mark context ──────────────────────────────────────────────────────────

        /// <summary>
        /// Catches marks resting on desks/shelves where no walkable floor is baked (raycasts stop on the prop):
        /// compares the mark's height with the median height of nearby floor marks.
        /// </summary>
        /// <summary>Named story marks that stand on the floor (not on desks, shelves or in the air).</summary>
        private static List<StudioMark> StoryFloorMarks(StudioSet set)
        {
            var floor = set.marks.Where(o => o != null && IsStoryMark(set, o) && IsFloorMark(o.transform.position, out _)).ToList();
            return floor.Where(o => !IsElevatedVsNeighbors(o, floor, out _)).ToList();
        }

        private static bool IsElevatedVsNeighbors(StudioMark m, List<StudioMark> floorMarks, out float above)
        {
            above = 0f;
            Vector3 p = m.transform.position;
            var ys = floorMarks.Where(o => o != m)
                .Select(o => o.transform.position)
                .Where(q => (q - p).WithY0().magnitude < 8f && Mathf.Abs(q.y - p.y) < 4f)
                .Select(q => q.y).OrderBy(y => y).ToList();
            if (ys.Count < 2) return false;
            above = p.y - ys[ys.Count / 2];
            return above > 0.8f;
        }

        private static void DescribeMarks(StudioSet set)
        {
            var storyFloor = StoryFloorMarks(set);
            foreach (StudioMark m in set.marks)
            {
                if (m == null) continue;
                Vector3 p = m.transform.position;
                Vector3 fwd = Vector3.ProjectOnPlane(m.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 chest = p + Vector3.up * ChestHeight;
                var sb = new StringBuilder();

                if (!IsFloorMark(p, out float above) || IsElevatedVsNeighbors(m, storyFloor, out above))
                    sb.Append($"NOT A FLOOR SPOT ({above:0.0}m above the floor): use only as a face/look target, never as a start/move mark; ");
                float nearestStory = storyFloor.Where(o => o != m).Select(o => (o.transform.position - p).magnitude).DefaultIfEmpty(0f).Min();
                if (nearestStory > 15f) sb.Append($"FAR from the set's story area ({nearestStory:0}m away); ");

                float ceiling = StudioSight.OpenDistance(p + Vector3.up * 1f, Vector3.up, 8f);
                sb.Append(ceiling < 7.9f ? $"indoors (ceiling {ceiling + 1f:0.0}m)" : "outdoors/open above");

                // What's ahead (and whether the view continues through glass).
                float ahead = StudioSight.OpenDistance(chest, fwd, 12f);
                StudioSight.IsClear(chest, chest + fwd * Mathf.Max(0.1f, ahead - 0.05f), out bool glassAhead);
                StudioPoint glass = glassAhead ? NearestPoint(set, chest + fwd * 1f, 4f, pt => pt.seeThrough) : null;
                sb.Append($"; ahead: {(ahead >= 11.9f ? "open 12m+" : $"{ahead:0.0}m to {Describe(set, chest + fwd * ahead)}")}");
                if (glassAhead) sb.Append($", looking through {(glass != null ? glass.pointId : "glass")}");

                sb.Append($"; open space L {StudioSight.OpenDistance(chest, -right, 8f):0.0}m, R {StudioSight.OpenDistance(chest, right, 8f):0.0}m, behind {StudioSight.OpenDistance(chest, -fwd, 8f):0.0}m");

                // Nearby points with direction relative to the mark's facing.
                var near = set.points.Where(pt => pt != null)
                    .Select(pt => (pt, d: (pt.transform.position - p).WithY0().magnitude))
                    .Where(x => x.d < 3.2f).OrderBy(x => x.d).Take(5).ToList();
                if (near.Count > 0)
                    sb.Append("; near: " + string.Join(", ", near.Select(x => $"{x.pt.pointId} {x.d:0.0}m {Direction(fwd, x.pt.transform.position - p)} at {x.pt.transform.position.y - p.y:0.0}m high")));

                // Other marks across glass / close by.
                var others = set.marks.Where(o => o != null && o != m)
                    .Select(o => (o, d: (o.transform.position - p).magnitude)).Where(x => x.d < 5f).OrderBy(x => x.d).Take(3).ToList();
                if (others.Count > 0)
                {
                    sb.Append("; marks: " + string.Join(", ", others.Select(x =>
                    {
                        Vector3 a = chest, b = x.o.transform.position + Vector3.up * ChestHeight;
                        bool clear = StudioSight.IsClear(a, b, out bool viaGlass);
                        string vis = !clear ? "no line of sight" : viaGlass ? "seen through glass" : "in view";
                        return $"{x.o.markId} {x.d:0.0}m {Direction(fwd, x.o.transform.position - p)} ({vis})";
                    })));
                }
                m.context = sb.ToString();
                EditorUtility.SetDirty(m);
            }
        }

        private static string Describe(StudioSet set, Vector3 hitPoint)
        {
            StudioPoint pt = NearestPoint(set, hitPoint, 0.9f, _ => true);
            return pt != null ? pt.pointId : "a wall/obstacle";
        }

        private static StudioPoint NearestPoint(StudioSet set, Vector3 p, float maxDist, System.Func<StudioPoint, bool> filter)
        {
            StudioPoint best = null;
            float bestD = maxDist;
            foreach (StudioPoint pt in set.points)
            {
                if (pt == null || !filter(pt)) continue;
                float d = Vector3.Distance(pt.transform.position, p) - pt.radius * 0.5f;
                if (d < bestD) { bestD = d; best = pt; }
            }
            return best;
        }

        private static string Direction(Vector3 fwd, Vector3 delta)
        {
            delta = delta.WithY0();
            if (delta.sqrMagnitude < 0.0001f) return "here";
            float a = Vector3.SignedAngle(fwd, delta, Vector3.up);
            if (Mathf.Abs(a) <= 25f) return "ahead";
            if (Mathf.Abs(a) >= 155f) return "behind";
            string side = a > 0 ? "right" : "left";
            if (Mathf.Abs(a) < 65f) return $"ahead-{side}";
            if (Mathf.Abs(a) > 115f) return $"behind-{side}";
            return side;
        }

        // ── Camera spots ──────────────────────────────────────────────────────────

        private static readonly (string name, float yaw)[] SpotYaws =
        {
            ("front", 0f), ("front_left", -40f), ("front_right", 40f), ("left", -90f), ("right", 90f), ("behind", 180f),
        };

        private static readonly (string name, float height)[] SpotHeights = { ("eye", EyeHeight), ("low", 0.8f), ("high", 3.3f) };
        private static readonly float[] SpotDistances = { 3.4f, 2.6f, 1.8f, 1.3f };

        private static int GenerateCameraSpots(StudioSet set)
        {
            Transform old = set.transform.Find(SpotsRootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(SpotsRootName).transform;
            root.SetParent(set.transform, false);

            // Floor-level story marks only; suspect/gameplay spots first, grid samples only if there are few.
            var marks = StoryFloorMarks(set)
                .OrderByDescending(m => (m.description ?? "").Contains("suspect") ? 1 : 0).ToList();
            if (marks.Count < 3) marks.AddRange(set.marks.Where(m => m != null && !IsStoryMark(set, m)).Take(3 - marks.Count));
            marks = marks.Take(MaxSpotMarks).ToList();

            int total = 0;
            foreach (StudioMark m in marks)
            {
                Vector3 p = m.transform.position;
                Vector3 fwd = Vector3.ProjectOnPlane(m.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                Vector3 head = p + Vector3.up * EyeHeight;
                Vector3 body = p + Vector3.up * ChestHeight;
                string shortId = m.markId.StartsWith(set.setId + "_") ? m.markId.Substring(set.setId.Length + 1) : m.markId;

                var valid = new List<(string id, Vector3 pos, float dist, bool glass, string desc, int rank)>();
                for (int yi = 0; yi < SpotYaws.Length; yi++)
                for (int hi = 0; hi < SpotHeights.Length; hi++)
                {
                    var (yawName, yaw) = SpotYaws[yi];
                    var (hName, h) = SpotHeights[hi];
                    if (yi >= 3 && hName != "eye") continue; // side/behind spots only at eye level: keeps the list short and useful
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * fwd;
                    foreach (float dist in SpotDistances)
                    {
                        Vector3 cam = p + dir * dist + Vector3.up * h;
                        if (!StudioSight.HasClearance(cam, 0.2f)) continue;
                        if (!StudioSight.IsClear(cam, head, out bool glass)) continue;
                        bool bodyVisible = StudioSight.IsClear(cam, body);
                        string desc = $"{yawName.Replace('_', '-')} of {m.markId}, {hName} level, {Vector3.Distance(cam, head):0.0}m from the head" +
                                      $"{(glass ? ", through the window glass" : "")}{(bodyVisible ? "" : ", head and shoulders only (body hidden)")}";
                        valid.Add(($"cam_{shortId}_{yawName}_{hName}", cam, dist, glass, desc, yi * 3 + hi));
                        break; // farthest valid distance for this direction/height
                    }
                }

                foreach (var v in valid.OrderBy(v => v.rank).Take(MaxSpotsPerMark))
                {
                    var go = new GameObject(v.id);
                    go.transform.SetParent(root, false);
                    go.transform.position = v.pos;
                    go.transform.rotation = Quaternion.LookRotation((head - v.pos).normalized, Vector3.up);
                    var spot = go.AddComponent<StudioCameraSpot>();
                    spot.spotId = v.id;
                    spot.markId = m.markId;
                    spot.throughGlass = v.glass;
                    spot.description = v.desc;
                    total++;
                }
            }
            return total;
        }

        // ── Set description ───────────────────────────────────────────────────────

        private static string AutoDescription(StudioSet set)
        {
            var sb = new StringBuilder($"Main.unity '{set.sourceRoot}' (auto-described; edit freely). ");
            var openings = set.points.Where(p => p != null && p.kind == "opening").Select(p => p.pointId).ToList();
            var props = set.points.Where(p => p != null && p.kind != "opening").Take(12).Select(p => p.pointId).ToList();
            if (openings.Count > 0) sb.Append("Openings: ").Append(string.Join(", ", openings)).Append(". ");
            if (props.Count > 0) sb.Append("Props/details: ").Append(string.Join(", ", props)).Append(". ");
            var story = set.marks.Where(m => m != null && !m.markId.StartsWith($"{set.setId}_near_")).Select(m => m.markId).ToList();
            if (story.Count > 0) sb.Append("Story marks: ").Append(string.Join(", ", story)).Append(". ");
            sb.Append("Read each mark's context for what surrounds it.");
            return sb.ToString();
        }
    }
}
