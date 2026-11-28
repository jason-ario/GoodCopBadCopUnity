using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Builds the <see cref="StudioCatalog"/> asset: actors (suspect prefabs + compatible clips),
    /// sets (StudioSet components in the studio scene) and short SFX. Props are left untouched.
    /// </summary>
    public static class StudioCatalogBuilder
    {
        private const string NpcPrefabFolder = "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs";
        private const string NpcArtFolder = NpcPrefabFolder + "/Art";
        private const string AnimationsRoot = "Assets/_GoodCopBadCop/_Animations";
        private const string AudioSearchRoot = "Assets/_GoodCopBadCop";

        private static readonly string[] SharedAnimFolders = { "Talking", "Gestures", "Mutants", "NPC", "Character Animations" };

        private const int MaxClipsPerActor = 80;
        private const int MaxSfx = 400;
        private const float MaxSfxLength = 6f;
        private const int BioMaxChars = 300;
        private const float GenericMatchRatio = 0.8f;
        private const int GenericMinPaths = 3;

        private static readonly string[] SfxExcludedPathKeywords = { "voice", "dialogue", "vo_", "music", "ambience" };
        private static readonly string[] SfxExcludedFolders = { "ost", "choir", "npc voices", "disembodied voices" };

        private static readonly (string tag, string[] keywords)[] ClipTagRules =
        {
            ("idle", new[] { "idle" }),
            ("walk", new[] { "walk", "stroll" }),
            ("run", new[] { "run" }),
            ("talk", new[] { "talk", "speak", "convers" }),
            ("gesture", new[] { "gesture", "point", "wave", "shrug", "nod", "clap", "thumb" }),
            ("react", new[] { "react", "surprise", "shock", "scared", "fear", "flinch", "hit" }),
            ("sit", new[] { "sit" }),
            ("angry", new[] { "angry", "rage", "yell", "shout" }),
            ("happy", new[] { "laugh", "happy", "cheer", "dance" }),
            ("sad", new[] { "sad", "cry" }),
            ("mutate", new[] { "mutat", "transform", "twitch", "uncanny", "glitch", "convuls" }),
            ("vomit", new[] { "vomit", "throw up", "puke" }),
            ("death", new[] { "death", "die", "dead" }),
            ("attack", new[] { "attack", "punch", "bite", "claw", "lunge" }),
            ("turn", new[] { "turn" }),
        };

        private static readonly (string tag, string[] keywords)[] SfxTagRules =
        {
            ("door", new[] { "door" }),
            ("slam", new[] { "slam" }),
            ("stamp", new[] { "stamp" }),
            ("paper", new[] { "paper" }),
            ("footstep", new[] { "footstep", "step" }),
            ("glass", new[] { "glass" }),
            ("scream", new[] { "scream" }),
            ("alarm", new[] { "alarm" }),
            ("buzz", new[] { "buzz" }),
            ("beep", new[] { "beep" }),
            ("hit", new[] { "hit" }),
            ("gun", new[] { "gun" }),
            ("shot", new[] { "shot" }),
            ("metal", new[] { "metal" }),
            ("creak", new[] { "creak" }),
            ("phone", new[] { "phone" }),
            ("radio", new[] { "radio" }),
            ("static", new[] { "static" }),
            ("whoosh", new[] { "whoosh" }),
            ("impact", new[] { "impact" }),
            ("sting", new[] { "sting" }),
            ("gore", new[] { "squish", "gore", "flesh" }),
        };

        // Clip -> distinct non-empty transform paths of its curve bindings.
        private static readonly Dictionary<AnimationClip, string[]> BindingPathCache = new();

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Loads the catalog and, if prefabs / SuspectData changed since the last check (or on the first call after a
        /// domain reload), incrementally adds new characters and drops deleted ones. See <see cref="SyncCast"/>.
        /// </summary>
        public static StudioCatalog LoadOrCreate()
        {
            StudioCatalog catalog = LoadOrCreateRaw();
            if (!s_Syncing && FilmStudioSettings.instance.autoSyncCast && catalog.actors.Count > 0 &&
                !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && CastNeedsSync())
            {
                SyncCast(catalog, out _);
            }
            return catalog;
        }

        internal static StudioCatalog LoadOrCreateRaw()
        {
            string path = FilmStudioSettings.instance.catalogPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "Assets/_GoodCopBadCop/FilmStudio/StudioCatalog.asset";
            }
            path = path.Replace('\\', '/');

            var catalog = AssetDatabase.LoadAssetAtPath<StudioCatalog>(path);
            if (catalog != null) return catalog;

            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<StudioCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FilmStudio] Created catalog at {path}");
            return catalog;
        }

        [MenuItem("Tools/Film Studio/Rebuild Catalog")]
        public static void RebuildMenu()
        {
            Rebuild();
        }

        public static StudioCatalog Rebuild()
        {
            StudioCatalog catalog = LoadOrCreateRaw();
            BindingPathCache.Clear();
            s_CastDirty = false;

            try
            {
                EditorUtility.DisplayProgressBar("Film Studio", "Collecting shared animation clips...", 0f);
                List<AnimationClip> sharedClips = CollectSharedClips();

                EditorUtility.DisplayProgressBar("Film Studio", "Building actors...", 0.2f);
                var previousScales = new Dictionary<string, float>();
                foreach (var a in catalog.actors)
                    if (a != null && !string.IsNullOrEmpty(a.actorId)) previousScales[a.actorId] = a.filmScale;
                catalog.actors = BuildActors(sharedClips);
                foreach (var a in catalog.actors)
                    if (a != null && previousScales.TryGetValue(a.actorId, out float scale)) a.filmScale = scale;
                StoreCastFingerprint();

                EditorUtility.DisplayProgressBar("Film Studio", "Collecting sets...", 0.7f);
                catalog.sets = BuildSets(catalog.sets);

                EditorUtility.DisplayProgressBar("Film Studio", "Collecting SFX...", 0.85f);
                catalog.sfx = BuildSfx();

                catalog.props ??= new List<StudioCatalog.PropEntry>();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                BindingPathCache.Clear();
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            int clipCount = catalog.actors.Sum(a => a.clips.Count);
            var sb = new StringBuilder();
            sb.Append($"[FilmStudio] Catalog: {catalog.actors.Count} actors, {clipCount} clips, {catalog.sets.Count} sets, {catalog.sfx.Count} sfx");
            foreach (var a in catalog.actors)
            {
                sb.Append('\n').Append($"  {a.actorId}: {(a.isHumanoid ? "Humanoid" : "Generic")}, {a.clips.Count} clips{(a.hasMutatedVariant ? ", mutated" : "")}");
            }
            Debug.Log(sb.ToString(), catalog);
            return catalog;
        }

        /// <summary>Fast refresh of just the sets/marks section (used after set import or mark edits).</summary>
        [MenuItem("Tools/Film Studio/Refresh Sets In Catalog")]
        public static void RefreshSets()
        {
            StudioCatalog catalog = LoadOrCreateRaw();
            catalog.sets = BuildSets(catalog.sets);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FilmStudio] Catalog sets refreshed: {string.Join(", ", catalog.sets.Select(s => $"{s.setId} ({s.marks.Count} marks)"))}");
        }

        public static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool lastUnderscore = false;
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }
            return sb.ToString().Trim('_');
        }

        // ------------------------------------------------------------------ actors

        private static List<StudioCatalog.ActorEntry> BuildActors(List<AnimationClip> sharedClips)
        {
            var actors = new List<StudioCatalog.ActorEntry>();
            var usedIds = new HashSet<string>();

            List<GameObject> prefabs = DiscoverActorPrefabs();
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                EditorUtility.DisplayProgressBar("Film Studio", $"Actor {prefab.name}", 0.2f + 0.5f * i / Mathf.Max(1, prefabs.Count));
                try
                {
                    actors.Add(BuildActor(prefab, sharedClips, usedIds));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FilmStudio] Skipped actor '{prefab.name}': {e.Message}", prefab);
                }
            }
            return actors;
        }

        // ------------------------------------------------------------------ dynamic cast discovery

        /// <summary>True when prefabs / SuspectData changed since the last sync (set by <see cref="StudioCastWatcher"/>).</summary>
        private static bool s_CastDirty;
        private static bool s_FingerprintChecked;
        private static bool s_Syncing;
        private const string CastFingerprintKey = "FilmStudio.CastFingerprint";
        /// <summary>Prefab paths already loaded and found not to be characters (cleared per path on re-import).</summary>
        private static readonly HashSet<string> s_RejectedPrefabs = new(StringComparer.OrdinalIgnoreCase);

        internal static void MarkCastDirty() => s_CastDirty = true;
        internal static void ForgetRejected(string path) => s_RejectedPrefabs.Remove(path);

        /// <summary>
        /// Cheap check (GUID lists only, no prefab loading) run once per domain load: dirty if the set of candidate
        /// prefabs / SuspectData assets differs from what the last sync or rebuild saw this editor session.
        /// </summary>
        private static bool CastNeedsSync()
        {
            if (s_CastDirty) return true;
            if (s_FingerprintChecked) return false;
            s_FingerprintChecked = true;
            s_CastDirty = CastFingerprint() != SessionState.GetString(CastFingerprintKey, "");
            return s_CastDirty;
        }

        private static string CastFingerprint()
        {
            string[] folders = CastSearchFolders();
            IEnumerable<string> guids = AssetDatabase.FindAssets("t:SuspectData");
            if (folders.Length > 0) guids = guids.Concat(AssetDatabase.FindAssets("t:Prefab", folders));
            List<string> sorted = guids.Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
            ulong h = 14695981039346656037UL;
            foreach (string g in sorted)
            foreach (char c in g) { h ^= c; h *= 1099511628211UL; }
            string settings = string.Join("|", FilmStudioSettings.instance.castSearchFolders ?? Array.Empty<string>()) + "#" +
                              string.Join("|", FilmStudioSettings.instance.castExcludedFolders ?? Array.Empty<string>());
            foreach (char c in settings) { h ^= c; h *= 1099511628211UL; }
            return $"{sorted.Count}:{h:x16}";
        }

        private static void StoreCastFingerprint() => SessionState.SetString(CastFingerprintKey, CastFingerprint());

        private static string[] CastSearchFolders() =>
            (FilmStudioSettings.instance.castSearchFolders ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().Replace('\\', '/').TrimEnd('/'))
            .Where(AssetDatabase.IsValidFolder)
            .ToArray();

        /// <summary>
        /// Every cast candidate, in a stable order: the original rule first (NPCs/Suspect*, then NPCs/Art) so existing
        /// actorIds never change, then everything discovered dynamically (SuspectData.CharacterPrefab anywhere in the
        /// project + character prefabs under <see cref="FilmStudioSettings.castSearchFolders"/>), sorted by path.
        /// </summary>
        public static List<GameObject> DiscoverActorPrefabs()
        {
            var result = new List<GameObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(GameObject go)
            {
                if (go == null) return;
                string p = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(p) || IsExcludedCastPath(p) || !seen.Add(p)) return;
                result.Add(go);
            }

            foreach (GameObject go in CollectLegacyActorPrefabs()) Add(go);

            var extra = new List<GameObject>();
            var extraPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddExtra(GameObject go)
            {
                if (go == null) return;
                string p = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(p) && !seen.Contains(p) && extraPaths.Add(p)) extra.Add(go);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:SuspectData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<SuspectData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.CharacterPrefab != null) AddExtra(data.CharacterPrefab.transform.root.gameObject);
            }

            string[] folders = CastSearchFolders();
            if (folders.Length > 0)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (seen.Contains(path) || extraPaths.Contains(path) || s_RejectedPrefabs.Contains(path) || IsExcludedCastPath(path)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && IsCharacterPrefab(go)) AddExtra(go);
                    else s_RejectedPrefabs.Add(path);
                }
            }

            SortByPath(extra);
            foreach (GameObject go in extra) Add(go);
            return result;
        }

        private static bool IsCharacterPrefab(GameObject go)
        {
            if (go.GetComponentInChildren<SuspectCharacter>(true) != null) return true;
            return go.GetComponentInChildren<Animator>(true) != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        }

        private static bool IsExcludedCastPath(string path)
        {
            path = path.Replace('\\', '/');
            foreach (string f in FilmStudioSettings.instance.castExcludedFolders ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                string folder = f.Trim().Replace('\\', '/').TrimEnd('/') + "/";
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Incremental cast update: drops actors whose prefab was deleted and builds entries for newly discovered
        /// characters, keeping every existing actorId and clip list untouched. Much cheaper than <see cref="Rebuild"/>.
        /// </summary>
        public static bool SyncCast(StudioCatalog catalog, out string summary)
        {
            summary = "Cast up to date.";
            if (catalog == null || s_Syncing) return false;
            s_Syncing = true;
            s_CastDirty = false;
            var added = new List<string>();
            var removed = new List<string>();
            try
            {
                catalog.actors.RemoveAll(a =>
                {
                    if (a.prefab != null) return false;
                    removed.Add(a.actorId);
                    return true;
                });

                var known = new HashSet<GameObject>(catalog.actors.Select(a => a.prefab));
                List<GameObject> missing = DiscoverActorPrefabs().Where(p => !known.Contains(p)).ToList();
                if (missing.Count > 0)
                {
                    BindingPathCache.Clear();
                    try
                    {
                        EditorUtility.DisplayProgressBar("Film Studio", "Collecting shared animation clips...", 0f);
                        List<AnimationClip> shared = CollectSharedClips();
                        var usedIds = new HashSet<string>(catalog.actors.Select(a => a.actorId));
                        for (int i = 0; i < missing.Count; i++)
                        {
                            GameObject prefab = missing[i];
                            EditorUtility.DisplayProgressBar("Film Studio", $"New actor {prefab.name}", 0.1f + 0.9f * i / missing.Count);
                            try
                            {
                                StudioCatalog.ActorEntry entry = BuildActor(prefab, shared, usedIds);
                                catalog.actors.Add(entry);
                                added.Add(entry.actorId);
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning($"[FilmStudio] Skipped actor '{prefab.name}': {e.Message}", prefab);
                            }
                        }
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                        BindingPathCache.Clear();
                    }
                }

                StoreCastFingerprint();
                if (added.Count == 0 && removed.Count == 0) return false;

                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                var sb = new StringBuilder("Cast synced:");
                if (added.Count > 0) sb.Append($" +{added.Count} ({string.Join(", ", added)})");
                if (removed.Count > 0) sb.Append($" -{removed.Count} ({string.Join(", ", removed)})");
                summary = sb.ToString();
                Debug.Log("[FilmStudio] " + summary, catalog);
                return true;
            }
            finally
            {
                s_Syncing = false;
            }
        }

        [MenuItem("Tools/Film Studio/Sync Cast (add new characters)")]
        public static void SyncCastMenu()
        {
            SyncCast(LoadOrCreateRaw(), out string summary);
            Debug.Log("[FilmStudio] " + summary);
        }

        private static List<GameObject> CollectLegacyActorPrefabs()
        {
            var result = new List<GameObject>();
            if (AssetDatabase.IsValidFolder(NpcPrefabFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { NpcPrefabFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), NpcPrefabFolder, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Path.GetFileName(path).StartsWith("Suspect", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) result.Add(go);
                    else Debug.LogWarning($"[FilmStudio] Could not load prefab '{path}' (broken or missing variant parent?)");
                }
                SortByPath(result);
            }
            else
            {
                Debug.LogWarning($"[FilmStudio] NPC prefab folder not found: {NpcPrefabFolder}");
            }

            // Art prefabs come after the main suspects so main suspects win actorId collisions.
            if (AssetDatabase.IsValidFolder(NpcArtFolder))
            {
                var art = new List<GameObject>();
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { NpcArtFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), NpcArtFolder, StringComparison.OrdinalIgnoreCase)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) art.Add(go);
                    else Debug.LogWarning($"[FilmStudio] Could not load prefab '{path}' (broken or missing variant parent?)");
                }
                SortByPath(art);
                result.AddRange(art);
            }
            return result;
        }

        private static void SortByPath(List<GameObject> list)
        {
            list.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase));
        }

        private static StudioCatalog.ActorEntry BuildActor(GameObject prefab, List<AnimationClip> sharedClips, HashSet<string> usedIds)
        {
            var entry = new StudioCatalog.ActorEntry { prefab = prefab };

            entry.actorId = MakeUnique(Slug(ActorBaseName(prefab.name)), usedIds, "actor");
            FillIdentity(prefab, entry);

            Transform baseChild = FindDirectChild(prefab.transform, "Base Version");
            Transform mutatedChild = FindDirectChild(prefab.transform, "Mutated Version");

            Animator baseAnim = FindBaseAnimator(prefab, baseChild, mutatedChild);
            Animator mutatedAnim = mutatedChild != null ? mutatedChild.GetComponentInChildren<Animator>(true) : null;

            entry.hasMutatedVariant = mutatedChild != null && mutatedAnim != null;
            entry.isHumanoid = baseAnim != null && baseAnim.avatar != null && baseAnim.avatar.isHuman;

            // clip -> (variant flags, isOwn)
            var order = new List<AnimationClip>();
            var flags = new Dictionary<AnimationClip, (bool onBase, bool onMutated, bool own)>();

            void AddClip(AnimationClip clip, bool onBase, bool onMutated, bool own)
            {
                if (clip == null || (!onBase && !onMutated)) return;
                if (clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return;
                if (flags.TryGetValue(clip, out var f))
                {
                    flags[clip] = (f.onBase || onBase, f.onMutated || onMutated, f.own || own);
                }
                else
                {
                    flags[clip] = (onBase, onMutated, own);
                    order.Add(clip);
                }
            }

            foreach (var clip in ControllerClips(baseAnim)) AddClip(clip, true, false, true);
            if (entry.hasMutatedVariant)
            {
                foreach (var clip in ControllerClips(mutatedAnim)) AddClip(clip, false, true, true);
            }

            foreach (var clip in sharedClips)
            {
                if (flags.ContainsKey(clip)) continue;
                bool onBase = baseAnim != null && IsCompatible(clip, baseAnim);
                bool onMutated = entry.hasMutatedVariant && IsCompatible(clip, mutatedAnim);
                AddClip(clip, onBase, onMutated, false);
            }

            var built = new List<(StudioCatalog.ClipEntry e, bool own, int index)>();
            var usedClipIds = new HashSet<string>();
            for (int i = 0; i < order.Count; i++)
            {
                AnimationClip clip = order[i];
                var f = flags[clip];
                var ce = new StudioCatalog.ClipEntry
                {
                    clipId = MakeUnique(Slug(clip.name), usedClipIds, "clip"),
                    clip = clip,
                    tags = TagsFor(clip.name, ClipTagRules),
                    length = clip.length,
                    loop = clip.isLooping,
                    variant = f.onBase && f.onMutated ? "both" : f.onMutated ? "mutated" : "base",
                };
                built.Add((ce, f.own, i));
            }

            if (built.Count > MaxClipsPerActor)
            {
                built = built
                    .OrderBy(b => b.own ? 0 : (b.e.tags.Count == 1 && b.e.tags[0] == "other") ? 2 : 1)
                    .ThenBy(b => b.index)
                    .Take(MaxClipsPerActor)
                    .OrderBy(b => b.index)
                    .ToList();
            }

            entry.clips = built.Select(b => b.e).ToList();
            return entry;
        }

        private static string ActorBaseName(string prefabName)
        {
            string n = prefabName.Trim();
            if (n.StartsWith("Suspect", StringComparison.OrdinalIgnoreCase))
            {
                n = n.Substring("Suspect".Length).TrimStart('_', ' ', '-');
            }
            if (n.EndsWith(" Variant", StringComparison.OrdinalIgnoreCase))
            {
                n = n.Substring(0, n.Length - " Variant".Length);
            }
            n = n.Trim();
            return string.IsNullOrEmpty(n) ? prefabName : n;
        }

        private static void FillIdentity(GameObject prefab, StudioCatalog.ActorEntry entry)
        {
            entry.displayName = prefab.name;
            entry.bio = prefab.name;

            var character = prefab.GetComponentInChildren<SuspectCharacter>(true);
            if (character == null) return;

            SuspectData data;
            using (var so = new SerializedObject(character))
            {
                data = so.FindProperty("suspectData")?.objectReferenceValue as SuspectData;
            }
            if (data == null) return;

            string fullName = $"{data.FirstName} {data.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(data.Nickname))
            {
                fullName = string.IsNullOrEmpty(fullName) ? data.Nickname.Trim() : $"{fullName} \"{data.Nickname.Trim()}\"";
            }
            if (!string.IsNullOrEmpty(fullName)) entry.displayName = fullName;

            var bio = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(data.Occupation)) bio.Append(data.Occupation.Trim()).Append(". ");
            if (!string.IsNullOrWhiteSpace(data.CharacterDescription))
            {
                string desc = data.CharacterDescription.Trim();
                if (desc.Length > BioMaxChars) desc = desc.Substring(0, BioMaxChars).TrimEnd() + "...";
                bio.Append(desc);
            }
            if (!string.IsNullOrWhiteSpace(data.alterEgoName))
            {
                if (bio.Length > 0) bio.Append(' ');
                bio.Append("Alter ego: ").Append(data.alterEgoName.Trim()).Append('.');
            }
            string bioText = bio.ToString().Trim();
            if (!string.IsNullOrEmpty(bioText)) entry.bio = bioText;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (string.Equals(child.name.Trim(), childName, StringComparison.OrdinalIgnoreCase)) return child;
            }
            return null;
        }

        private static Animator FindBaseAnimator(GameObject prefab, Transform baseChild, Transform mutatedChild)
        {
            var character = prefab.GetComponentInChildren<SuspectCharacter>(true);
            if (character != null && character.animator != null && !IsUnder(character.animator.transform, mutatedChild))
            {
                return character.animator;
            }

            if (baseChild != null)
            {
                var a = baseChild.GetComponentInChildren<Animator>(true);
                if (a != null) return a;
            }

            var root = prefab.GetComponent<Animator>();
            if (root != null) return root;

            foreach (var a in prefab.GetComponentsInChildren<Animator>(true))
            {
                if (!IsUnder(a.transform, mutatedChild)) return a;
            }
            return null;
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            return ancestor != null && t != null && (t == ancestor || t.IsChildOf(ancestor));
        }

        private static IEnumerable<AnimationClip> ControllerClips(Animator animator)
        {
            if (animator == null) return Array.Empty<AnimationClip>();
            RuntimeAnimatorController rac = animator.runtimeAnimatorController;
            if (rac == null) return Array.Empty<AnimationClip>();

            var clips = new List<AnimationClip>();
            if (rac is AnimatorOverrideController aoc)
            {
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(aoc.overridesCount);
                aoc.GetOverrides(overrides);
                foreach (var kv in overrides)
                {
                    clips.Add(kv.Value != null ? kv.Value : kv.Key);
                }
            }
            clips.AddRange(rac.animationClips);
            return clips.Where(c => c != null).Distinct();
        }

        private static bool IsCompatible(AnimationClip clip, Animator animator)
        {
            if (animator == null) return false;
            if (clip.isHumanMotion)
            {
                return animator.avatar != null && animator.avatar.isHuman;
            }

            string[] paths = GetBindingPaths(clip);
            if (paths.Length < GenericMinPaths) return false;

            Transform root = animator.transform;
            int found = 0;
            foreach (string p in paths)
            {
                if (root.Find(p) != null) found++;
            }
            return found >= GenericMatchRatio * paths.Length;
        }

        private static string[] GetBindingPaths(AnimationClip clip)
        {
            if (BindingPathCache.TryGetValue(clip, out var cached)) return cached;
            string[] paths = AnimationUtility.GetCurveBindings(clip)
                .Select(b => b.path)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToArray();
            BindingPathCache[clip] = paths;
            return paths;
        }

        private static List<AnimationClip> CollectSharedClips()
        {
            var folders = SharedAnimFolders
                .Select(f => $"{AnimationsRoot}/{f}")
                .Where(AssetDatabase.IsValidFolder)
                .ToArray();
            var result = new List<AnimationClip>();
            if (folders.Length == 0) return result;

            var seen = new HashSet<AnimationClip>();
            var paths = AssetDatabase.FindAssets("t:AnimationClip", folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                IEnumerable<AnimationClip> clips = path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
                    ? new[] { AssetDatabase.LoadAssetAtPath<AnimationClip>(path) }
                    : AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>();

                foreach (var clip in clips)
                {
                    if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                    if (seen.Add(clip)) result.Add(clip);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ sets

        private static List<StudioCatalog.SetEntry> BuildSets(List<StudioCatalog.SetEntry> previous)
        {
            string scenePath = FilmStudioSettings.instance.studioScenePath;
            if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
            {
                Debug.LogWarning($"[FilmStudio] Studio scene not found at '{scenePath}'; skipping sets.");
                return new List<StudioCatalog.SetEntry>();
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            var sets = new List<StudioCatalog.SetEntry>();
            var usedSetIds = new HashSet<string>();
            StudioSet[] found = UnityEngine.Object.FindObjectsByType<StudioSet>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (StudioSet set in found.OrderBy(s => s.setId ?? s.name, StringComparer.OrdinalIgnoreCase))
            {
                set.RefreshMarks();
                EditorUtility.SetDirty(set);

                var se = new StudioCatalog.SetEntry
                {
                    setId = MakeUnique(string.IsNullOrWhiteSpace(set.setId) ? Slug(set.name) : set.setId, usedSetIds, "set"),
                    description = set.description,
                };
                var usedMarkIds = new HashSet<string>();
                foreach (StudioMark mark in set.marks)
                {
                    if (mark == null) continue;
                    se.marks.Add(new StudioCatalog.MarkEntry
                    {
                        markId = MakeUnique(string.IsNullOrWhiteSpace(mark.markId) ? Slug(mark.name) : mark.markId, usedMarkIds, "mark"),
                        description = mark.description,
                        context = mark.context,
                        position = mark.transform.position,
                    });
                }
                foreach (StudioPoint p in set.points)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.pointId)) continue;
                    se.points.Add(new StudioCatalog.PointEntry
                    {
                        pointId = p.pointId, kind = p.kind, description = p.description,
                        position = p.transform.position, radius = p.radius, seeThrough = p.seeThrough,
                    });
                }
                foreach (StudioCameraSpot c in set.cameraSpots)
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.spotId)) continue;
                    se.cameraSpots.Add(new StudioCatalog.CameraSpotEntry
                    {
                        spotId = c.spotId, description = c.description, markId = c.markId,
                        throughGlass = c.throughGlass, position = c.transform.position,
                    });
                }
                sets.Add(se);
            }
            return sets;
        }

        // ------------------------------------------------------------------ sfx

        private static List<StudioCatalog.SfxEntry> BuildSfx()
        {
            var result = new List<StudioCatalog.SfxEntry>();
            var usedIds = new HashSet<string>();

            var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioSearchRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => !IsExcludedSfxPath(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                if (result.Count >= MaxSfx) break;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null || clip.length <= 0f || clip.length > MaxSfxLength) continue;

                string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
                result.Add(new StudioCatalog.SfxEntry
                {
                    sfxId = MakeUnique(Slug(clip.name), usedIds, "sfx"),
                    clip = clip,
                    tags = TagsFor(clip.name + " " + folder, SfxTagRules),
                    length = clip.length,
                });
            }
            return result;
        }

        private static bool IsExcludedSfxPath(string path)
        {
            string lower = path.ToLowerInvariant();
            foreach (string kw in SfxExcludedPathKeywords)
            {
                if (lower.Contains(kw)) return true;
            }
            string[] segments = lower.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (SfxExcludedFolders.Contains(segments[i])) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static List<string> TagsFor(string name, (string tag, string[] keywords)[] rules)
        {
            // Split CamelCase so word-start matching works on "PersonSitIdle".
            string words = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2").ToLowerInvariant();
            words = Regex.Replace(words, "[^a-z0-9]+", " ");

            var tags = new List<string>();
            foreach (var (tag, keywords) in rules)
            {
                foreach (string kw in keywords)
                {
                    if (MatchesKeyword(words, kw))
                    {
                        tags.Add(tag);
                        break;
                    }
                }
            }
            if (tags.Count == 0) tags.Add("other");
            return tags;
        }

        private static bool MatchesKeyword(string words, string keyword)
        {
            // Short keywords ("sit", "die", "hit", "run", ...) must start a word to avoid
            // false positives like "position" or "soldier".
            if (keyword.Length <= 3)
            {
                return Regex.IsMatch(words, $@"(^|\s){Regex.Escape(keyword)}");
            }
            return words.Contains(keyword);
        }

        private static string MakeUnique(string id, HashSet<string> used, string fallback)
        {
            if (string.IsNullOrEmpty(id)) id = fallback;
            string candidate = id;
            int n = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{id}_{n++}";
            }
            return candidate;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>Flags the Film Studio cast for a re-sync when prefabs or SuspectData assets are added, changed, moved or deleted.</summary>
    internal sealed class StudioCastWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool hit = false;
            void Check(string[] paths, bool canInspect)
            {
                foreach (string p in paths)
                {
                    if (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        StudioCatalogBuilder.ForgetRejected(p);
                        hit = true;
                    }
                    else if (canInspect && p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                             AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(SuspectData))
                    {
                        hit = true;
                    }
                }
            }
            Check(imported, true);
            Check(moved, true);
            Check(deleted, false);
            Check(movedFrom, false);
            if (hit) StudioCatalogBuilder.MarkCastDirty();
        }
    }
}
