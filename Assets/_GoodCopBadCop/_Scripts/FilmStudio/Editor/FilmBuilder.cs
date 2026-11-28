using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>A built film: scene root + director + generated Timeline asset.</summary>
    public class FilmInstance
    {
        public string slug;
        public int revision;
        public GameObject root;
        public PlayableDirector director;
        public TimelineAsset timeline;
        public string timelinePath;
        public string jsonPath;
        public string srtPath;
        public float duration;
        public List<string> warnings = new();

        public string DirectorPath => $"{StudioSceneUtility.FilmsRootName}/{root.name}";
        public string OutputBaseName => $"{slug}_v{revision}";
    }

    /// <summary>
    /// Deterministically turns a <see cref="Screenplay"/> into a Timeline in FilmStudio.unity:
    /// cast spawned on marks, one CinemachineCamera per shot (framing presets + ShotRig moves),
    /// animation / move / variant / visibility tracks per actor, SFX and speed ramps.
    /// </summary>
    public static class FilmBuilder
    {
        // ── Tuning ────────────────────────────────────────────────────────────────
        private const float DefaultWalkSpeed = 1.4f;
        private const float FaceDuration = 0.4f;
        private const float AnimBlend = 0.2f;
        private const float CameraEaseBlend = 0.35f;
        private const string NoisePresetPath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

        /// <summary>
        /// Shot sizes relative to the subject's real standing height h (top of head, incl. film scale):
        /// frame = vertical extent of the picture at the subject, dist = preferred camera distance (perspective),
        /// anchor = what the composer centres on (face anchor = centre of the face, not the skull-base head bone),
        /// screenY = CM3 composition offset (negative = subject higher on screen). The lens is fitted to frame/dist,
        /// so a blocked camera that has to come closer keeps the same shot size instead of filling the frame with a chin.
        /// </summary>
        private static readonly Dictionary<string, (float frameK, float distK, string anchor, float sy)> ShotSizes = new()
        {
            { "extreme_wide",     (3.2f,  3.6f,  "chest", 0f) },
            { "wide",             (1.3f,  1.7f,  "chest", 0.02f) },
            { "medium",           (0.58f, 1.0f,  "face", -0.14f) },
            { "close_up",         (0.30f, 0.62f, "face", -0.04f) },
            { "extreme_close_up", (0.12f, 0.42f, "face", 0f) },
            { "insert",           (0.22f, 0.45f, "chest", 0f) },
        };

        // ── Build-time state ──────────────────────────────────────────────────────
        private static string _filmFolder;
        private static readonly Dictionary<(string actorId, string id), (CustomAnim anim, float duration, bool loop)> _customs = new();
        private static readonly Dictionary<(string id, string variant), AnimationClip> _customClips = new();

        private class AnimSeg
        {
            public float start, end;
            public AnimationClip clip;
            public string id;
            /// <summary>Generic mode: catalog entry resolved per rig variant at track-build time.</summary>
            public StudioCatalog.ClipEntry entry;
            /// <summary>Screenplay custom anim, baked per variant at track-build time.</summary>
            public CustomAnim custom;
        }

        private class ActorState
        {
            public StudioActor actor;
            public StudioCatalog.ActorEntry entry;
            public Vector3 pos;
            public float yaw;
            public bool mutated;
            public float busyUntil;
            public float headHeight;
            public bool generic;

            public Vector3 initialPos;
            public float initialYaw;
            public bool initialMutated;

            public readonly List<AnimSeg> baseSegs = new();
            public readonly List<AnimSeg> mutSegs = new();
            public readonly List<(float t, float dur, Vector3 to)> moves = new();
            public readonly List<(float t, float dur, float yaw)> faces = new();
            public readonly List<(float t, bool mutated)> variants = new();
            public readonly List<(float t, bool visible)> visibility = new();
        }

        // ── Entry point ───────────────────────────────────────────────────────────

        public static FilmInstance Build(Screenplay s, StudioCatalog catalog, string slug, int revision)
        {
            if (s == null || catalog == null) { Debug.LogError("[FilmStudio] Build: screenplay or catalog missing."); return null; }
            if (!StudioSceneUtility.EnsureOpen(out Scene scene)) return null;

            s.RecomputeTiming();
            var film = new FilmInstance { slug = slug, revision = revision, duration = s.durationSec };

            StudioSet set = StudioSceneUtility.FindSet(scene, s.setId);
            if (set == null) { Debug.LogError($"[FilmStudio] Set '{s.setId}' not found in the film overlay. Import it first (Tools/Film Studio/Import Set…)."); return null; }
            set.RefreshMarks();
            GameObject setsRoot = StudioSceneUtility.FindOrCreateRoot(scene, StudioSceneUtility.SetsRootName);
            foreach (StudioSet other in setsRoot.GetComponentsInChildren<StudioSet>(true)) other.Activate(other == set);

            CinemachineBrain brain = StudioSceneUtility.FindBrain(scene);
            if (brain == null) { Debug.LogError("[FilmStudio] Studio Main Camera has no CinemachineBrain."); return null; }

            // Root + director
            GameObject filmsRoot = StudioSceneUtility.FindOrCreateRoot(scene, StudioSceneUtility.FilmsRootName);
            foreach (Transform child in filmsRoot.transform)
                child.gameObject.SetActive(false);
            string rootName = $"--FILM_{slug}";
            Transform old = filmsRoot.transform.Find(rootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            film.root = new GameObject(rootName);
            film.root.transform.SetParent(filmsRoot.transform, false);
            film.director = film.root.AddComponent<PlayableDirector>();
            film.director.playOnAwake = false;
            film.director.extrapolationMode = DirectorWrapMode.None;
            film.director.timeUpdateMode = DirectorUpdateMode.GameTime;
            var sfxSource = film.root.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            var castRoot = new GameObject("Cast").transform; castRoot.SetParent(film.root.transform, false);
            var camRoot = new GameObject("Cameras").transform; camRoot.SetParent(film.root.transform, false);

            // Timeline asset
            string folder = $"{FilmStudioSettings.instance.filmsFolder.TrimEnd('/')}/{slug}";
            EnsureFolder(folder);
            film.timelinePath = $"{folder}/{film.OutputBaseName}.playable";
            film.jsonPath = $"{folder}/{film.OutputBaseName}.json";
            film.srtPath = $"{folder}/{film.OutputBaseName}.srt";
            AssetDatabase.DeleteAsset(film.timelinePath);
            film.timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            film.timeline.editorSettings.frameRate = s.fps > 0 ? s.fps : 30;
            AssetDatabase.CreateAsset(film.timeline, film.timelinePath);
            film.director.playableAsset = film.timeline;

            // Cast
            bool generic = FilmStudioSettings.instance.genericAnimation;
            _filmFolder = folder;
            _customs.Clear();
            _customClips.Clear();
            foreach (CustomAnim ca in s.customAnims ?? new List<CustomAnim>())
            {
                if (ca == null || string.IsNullOrEmpty(ca.id)) continue;
                try
                {
                    AnimGen.AnimPlan p = StudioGenericAnims.ParsePlan(ca);
                    _customs[(ca.actorId, ca.id)] = (ca, p.duration, p.loop);
                }
                catch (System.Exception e) { film.warnings.Add($"CUSTOM ANIM FAILED '{ca.id}': plan does not parse ({e.Message})."); }
            }

            var states = new Dictionary<string, ActorState>();
            foreach (CastEntry ce in s.cast)
            {
                StudioCatalog.ActorEntry entry = catalog.FindActor(ce.actorId);
                StudioMark mark = set.FindMark(ce.startMarkId);
                if (entry == null) { film.warnings.Add($"Unknown actor '{ce.actorId}' skipped."); continue; }
                if (states.ContainsKey(ce.actorId)) continue;
                Vector3 pos = mark != null ? mark.transform.position : set.transform.position;
                float yaw = mark != null ? mark.transform.eulerAngles.y : 0f;
                if (mark == null) film.warnings.Add($"Mark '{ce.startMarkId}' not found for '{ce.actorId}'; placed at set origin.");

                bool mutated = ce.variant == "mutated" && entry.hasMutatedVariant;
                StudioActor actor = StudioActorFactory.Spawn(entry, pos, yaw, castRoot, mutated, generic);
                if (actor == null) continue;
                states[ce.actorId] = new ActorState
                {
                    actor = actor, entry = entry, pos = pos, yaw = yaw, mutated = mutated,
                    initialPos = pos, initialYaw = yaw, initialMutated = mutated,
                    headHeight = StudioActorFactory.HeadHeight(actor), generic = generic,
                };
            }

            // Tracks shared by all shots
            var cmTrack = film.timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            film.director.SetGenericBinding(cmTrack, brain);
            var rigGroup = film.timeline.CreateTrack<GroupTrack>(null, "Camera Rigs");
            AudioTrack sfxTrack = null;
            TimeScaleTrack speedTrack = null;
            TimelineClip prevShotClip = null;

            // Shots
            LastOcclusionNotes.Clear();
            for (int i = 0; i < s.shots.Count; i++)
            {
                Shot shot = s.shots[i];
                CinemachineCamera vcam = BuildShotCamera(shot, states, set, camRoot, out ShotRigClipData rig);

                if (vcam != null)
                {
                    TimelineClip shotClip = cmTrack.CreateClip<CinemachineShot>();
                    var shotAsset = (CinemachineShot)shotClip.asset;
                    shotAsset.DisplayName = shot.id;
                    shotAsset.VirtualCamera.exposedName = GUID.Generate().ToString();
                    film.director.SetReferenceValue(shotAsset.VirtualCamera.exposedName, vcam);
                    shotClip.displayName = $"{shot.id} {shot.camera.framing} {shot.camera.move}";
                    double start = shot.start, end = shot.start + shot.duration;
                    if (prevShotClip != null && shot.camera.blendIn == "ease")
                    {
                        start = Mathf.Max(0f, shot.start - CameraEaseBlend);
                        shotClip.start = start;
                        shotClip.duration = end - start;
                        shotClip.blendInDuration = shot.start - start;
                        prevShotClip.blendOutDuration = shot.start - start;
                    }
                    else
                    {
                        shotClip.start = start;
                        shotClip.duration = end - start;
                    }
                    prevShotClip = shotClip;

                    var rigTrack = film.timeline.CreateTrack<ShotRigTrack>(rigGroup, $"Rig {shot.id}");
                    film.director.SetGenericBinding(rigTrack, vcam);
                    TimelineClip rigClip = rigTrack.CreateClip<ShotRigClip>();
                    rigClip.start = start;
                    rigClip.duration = end - start;
                    rigClip.displayName = shot.camera.move;
                    rig.ApplyTo((ShotRigClip)rigClip.asset);
                }

                // Actions (shot-relative → absolute)
                var actions = new List<ActorAction>(shot.actions);
                actions.Sort((a, b) => a.t.CompareTo(b.t));
                foreach (ActorAction a in actions)
                {
                    if (!states.TryGetValue(a.actorId, out ActorState st)) { film.warnings.Add($"{shot.id}: action for unknown/uncast actor '{a.actorId}'."); continue; }
                    float t = shot.start + Mathf.Max(0f, a.t);
                    ApplyAction(a, t, st, states, set, film);
                }

                // SFX
                foreach (SfxCue cue in shot.sfx)
                {
                    StudioCatalog.SfxEntry sfx = catalog.FindSfx(cue.sfxId);
                    if (sfx == null || sfx.clip == null) { film.warnings.Add($"{shot.id}: SFX '{cue.sfxId}' not found."); continue; }
                    sfxTrack ??= film.timeline.CreateTrack<AudioTrack>(null, "SFX");
                    TimelineClip ac = sfxTrack.CreateClip(sfx.clip);
                    ac.start = shot.start + Mathf.Max(0f, cue.t);
                    ac.displayName = sfx.sfxId;
                    SetAudioVolume((AudioPlayableAsset)ac.asset, cue.volume);
                }

                // Speed ramp
                if (Mathf.Abs(shot.speed - 1f) > 0.01f)
                {
                    speedTrack ??= film.timeline.CreateTrack<TimeScaleTrack>(null, "Speed");
                    TimelineClip sc = speedTrack.CreateClip<TimeScaleClip>();
                    sc.start = shot.start;
                    sc.duration = shot.duration;
                    sc.displayName = $"x{shot.speed:0.##}";
                    ((TimeScaleClip)sc.asset).timeScale = shot.speed;
                }
            }
            if (sfxTrack != null) film.director.SetGenericBinding(sfxTrack, sfxSource);
            film.warnings.AddRange(LastOcclusionNotes);

            // Per-actor tracks
            foreach (ActorState st in states.Values)
                BuildActorTracks(st, film, s.durationSec);

            // Persist
            EditorUtility.SetDirty(film.timeline);
            AssetDatabase.SaveAssets();
            film.director.RebuildGraph();
            StudioSceneUtility.EvaluatePreview(film.director, 0);

            File.WriteAllText(film.jsonPath, JsonConvert.SerializeObject(s, Formatting.Indented));
            File.WriteAllText(film.srtPath, BuildSrt(s));
            AssetDatabase.ImportAsset(film.jsonPath);
            AssetDatabase.ImportAsset(film.srtPath);

            StudioSceneUtility.SaveStudio();
            Selection.activeGameObject = film.root;

            foreach (string w in film.warnings) Debug.LogWarning($"[FilmStudio] {w}");
            Debug.Log($"[FilmStudio] Built '{s.title}' ({film.OutputBaseName}): {s.shots.Count} shots, {states.Count} actors, {s.durationSec:0.0}s → {film.timelinePath}");
            return film;
        }

        /// <summary>Finds an already-built film root in the open studio scene.</summary>
        public static FilmInstance Find(string slug)
        {
            Scene scene = SceneManager.GetSceneByPath(StudioSceneUtility.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject filmsRoot = StudioSceneUtility.FindRoot(scene, StudioSceneUtility.FilmsRootName);
            Transform t = filmsRoot != null ? filmsRoot.transform.Find($"--FILM_{slug}") : null;
            if (t == null) return null;
            var director = t.GetComponent<PlayableDirector>();
            var tl = director != null ? director.playableAsset as TimelineAsset : null;
            if (tl == null) return null;
            string path = AssetDatabase.GetAssetPath(tl);
            string baseName = Path.GetFileNameWithoutExtension(path);
            int rev = 1;
            int vi = baseName.LastIndexOf("_v", System.StringComparison.Ordinal);
            if (vi >= 0) int.TryParse(baseName.Substring(vi + 2), out rev);
            return new FilmInstance
            {
                slug = slug, revision = rev, root = t.gameObject, director = director, timeline = tl,
                timelinePath = path, jsonPath = Path.ChangeExtension(path, ".json"), srtPath = Path.ChangeExtension(path, ".srt"),
                duration = (float)tl.duration,
            };
        }

        // ── Actions ───────────────────────────────────────────────────────────────

        private static void ApplyAction(ActorAction a, float t, ActorState st, Dictionary<string, ActorState> states, StudioSet set, FilmInstance film)
        {
            switch (a.type)
            {
                case "anim":
                {
                    StudioCatalog.ClipEntry ce = st.entry.FindClip(a.clipId);
                    if (ce == null && _customs.TryGetValue((st.entry.actorId, a.clipId), out var custom))
                    {
                        CustomAnim ca = custom.anim;
                        bool onMut = ca.variant == "mutated" || (ca.variant == "both" && st.mutated);
                        float cend = custom.loop ? float.PositiveInfinity : t + Mathf.Max(0.1f, custom.duration);
                        (onMut ? st.mutSegs : st.baseSegs).Add(new AnimSeg { start = t, end = cend, id = ca.id, custom = ca });
                        break;
                    }
                    if (ce == null || ce.clip == null) { film.warnings.Add($"Clip '{a.clipId}' not found for '{st.entry.actorId}'."); return; }
                    // Generic mode retargets every catalog clip onto the variant that is currently visible.
                    bool onMutated = st.generic ? st.mutated : ce.variant == "mutated" || (ce.variant == "both" && st.mutated);
                    float end = ce.loop ? float.PositiveInfinity : t + Mathf.Max(0.1f, ce.length);
                    (onMutated ? st.mutSegs : st.baseSegs).Add(new AnimSeg { start = t, end = end, clip = ce.clip, id = ce.clipId, entry = ce });
                    break;
                }
                case "move":
                {
                    StudioMark mark = set.FindMark(a.toMarkId);
                    if (mark == null) { film.warnings.Add($"Mark '{a.toMarkId}' not found for move of '{st.entry.actorId}'."); return; }
                    t = Mathf.Max(t, st.busyUntil);
                    Vector3 to = mark.transform.position;
                    float dist = Vector3.Distance(st.pos, to);
                    float speed = a.moveSpeed > 0.05f ? a.moveSpeed : DefaultWalkSpeed;
                    float dur = Mathf.Max(0.3f, dist / speed);
                    st.moves.Add((t, dur, to));
                    Vector3 dir = (to - st.pos).WithY0();
                    if (dir.sqrMagnitude > 0.0001f) st.yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                    st.pos = to;
                    st.busyUntil = t + dur;

                    StudioCatalog.ClipEntry walk = FindVariantClip(st, "walk");
                    if (walk != null)
                        (st.mutated && (st.generic || walk.variant != "base") ? st.mutSegs : st.baseSegs)
                            .Add(new AnimSeg { start = t, end = t + dur, clip = walk.clip, id = walk.clipId, entry = walk });
                    break;
                }
                case "face":
                {
                    Vector3? target = null;
                    if (!string.IsNullOrEmpty(a.faceTargetId))
                    {
                        if (states.TryGetValue(a.faceTargetId, out ActorState other)) target = other.pos;
                        else target = set.FindTargetPosition(a.faceTargetId);
                    }
                    if (target == null) { film.warnings.Add($"Face target '{a.faceTargetId}' not found."); return; }
                    Vector3 dir = (target.Value - st.pos).WithY0();
                    if (dir.sqrMagnitude < 0.0001f) return;
                    t = Mathf.Max(t, st.busyUntil);
                    st.yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                    st.faces.Add((t, FaceDuration, st.yaw));
                    st.busyUntil = t + FaceDuration;
                    break;
                }
                case "variant_swap":
                    if (!st.entry.hasMutatedVariant || st.actor.mutatedArt == null) { film.warnings.Add($"'{st.entry.actorId}' has no mutated variant."); return; }
                    st.mutated = !st.mutated;
                    st.variants.Add((t, st.mutated));
                    break;
                case "hide":
                    st.visibility.Add((t, false));
                    break;
                case "show":
                    st.visibility.Add((t, true));
                    break;
            }
        }

        private static StudioCatalog.ClipEntry FindVariantClip(ActorState st, string tag)
        {
            string want = st.mutated ? "mutated" : "base";
            StudioCatalog.ClipEntry fallback = null;
            foreach (StudioCatalog.ClipEntry c in st.entry.clips)
            {
                if (c.tags == null || !c.tags.Contains(tag) || c.clip == null) continue;
                if (c.variant == want || c.variant == "both") return c;
                fallback ??= c;
            }
            // Generic clips are retargeted per rig, so any variant's clip works on the mutant too.
            return st.mutated && !st.generic ? null : fallback;
        }

        // ── Actor tracks ──────────────────────────────────────────────────────────

        private static void BuildActorTracks(ActorState st, FilmInstance film, float filmEnd)
        {
            TimelineAsset tl = film.timeline;
            string id = st.entry.actorId;
            var group = tl.CreateTrack<GroupTrack>(null, id);

            Animator baseAnim = st.actor.BaseAnimator;
            Animator mutAnim = st.actor.MutatedAnimator;
            if (st.generic)
            {
                if (baseAnim != null) CreateGenericAnimTracks(tl, group, $"{id} Anim", baseAnim, st, false, st.baseSegs, filmEnd, film);
                if (mutAnim != null) CreateGenericAnimTracks(tl, group, $"{id} Anim (Mutant)", mutAnim, st, true, st.mutSegs, filmEnd, film);
            }
            else
            {
                ResolveSegClips(st, false, st.baseSegs, film);
                ResolveSegClips(st, true, st.mutSegs, film);
                if (baseAnim != null && st.baseSegs.Count > 0)
                    CreateAnimTrack(tl, group, $"{id} Anim", baseAnim, st.baseSegs, filmEnd, film.director);
                if (mutAnim != null && st.mutSegs.Count > 0)
                    CreateAnimTrack(tl, group, $"{id} Anim (Mutant)", mutAnim, st.mutSegs, filmEnd, film.director);
            }

            if (st.moves.Count > 0 || st.faces.Count > 0)
            {
                var move = tl.CreateTrack<ActorMoveTrack>(group, $"{id} Move");
                move.initialPosition = st.initialPos;
                move.initialYaw = st.initialYaw;
                foreach (var m in st.moves)
                {
                    TimelineClip c = move.CreateClip<ActorMoveClip>();
                    c.start = m.t; c.duration = m.dur; c.displayName = "move";
                    ((ActorMoveClip)c.asset).toPosition = m.to;
                }
                foreach (var f in st.faces)
                {
                    TimelineClip c = move.CreateClip<ActorFaceClip>();
                    c.start = f.t; c.duration = f.dur; c.displayName = "face";
                    ((ActorFaceClip)c.asset).toYaw = f.yaw;
                }
                film.director.SetGenericBinding(move, st.actor);
            }

            if (st.variants.Count > 0)
            {
                var variant = tl.CreateTrack<ActorVariantTrack>(group, $"{id} Variant");
                variant.initialMutated = st.initialMutated;
                foreach (var v in st.variants)
                {
                    TimelineClip c = variant.CreateClip<ActorVariantClip>();
                    c.start = v.t; c.duration = 0.1; c.displayName = v.mutated ? "MUTATE" : "revert";
                    ((ActorVariantClip)c.asset).mutated = v.mutated;
                }
                film.director.SetGenericBinding(variant, st.actor);
            }

            if (st.visibility.Count > 0)
            {
                var act = tl.CreateTrack<ActivationTrack>(group, $"{id} Visible");
                act.postPlaybackState = ActivationTrack.PostPlaybackState.Revert;
                st.visibility.Sort((a, b) => a.t.CompareTo(b.t));
                bool visible = !(st.visibility[0].visible); // first event "show" → hidden at start
                float since = 0f;
                foreach (var v in st.visibility)
                {
                    if (v.visible == visible) continue;
                    if (visible && v.t > since) AddActivation(act, since, v.t);
                    visible = v.visible;
                    since = v.t;
                }
                if (visible && filmEnd > since) AddActivation(act, since, filmEnd);
                film.director.SetGenericBinding(act, st.actor.gameObject);
            }

            // Prefer an empty group over clutter when the actor just stands there.
            if (!System.Linq.Enumerable.Any(group.GetChildTracks()))
                tl.DeleteTrack(group);
        }

        private static void AddActivation(ActivationTrack track, float start, float end)
        {
            TimelineClip c = track.CreateDefaultClip();
            c.start = start;
            c.duration = Mathf.Max(0.05f, end - start);
            c.displayName = "visible";
        }

        private static void CreateAnimTrack(TimelineAsset tl, TrackAsset group, string trackName, Animator animator,
                                            List<AnimSeg> segs, float filmEnd, PlayableDirector director)
        {
            var track = tl.CreateTrack<AnimationTrack>(group, trackName);
            track.trackOffset = TrackOffset.ApplySceneOffsets;
            director.SetGenericBinding(track, animator);
            FillAnimTrack(track, segs, filmEnd, false);
        }

        /// <summary>
        /// Resolves each segment's clip for one variant: custom anims are baked (once per film/variant), catalog clips
        /// are retargeted to Generic in Generic mode. Segments whose clip can't be produced are dropped.
        /// </summary>
        private static void ResolveSegClips(ActorState st, bool mutated, List<AnimSeg> segs, FilmInstance film)
        {
            string variant = StudioGenericAnims.VariantName(mutated);
            foreach (AnimSeg seg in segs)
            {
                if (seg.custom != null)
                {
                    var key = (seg.custom.id, st.generic ? variant : "humanoid");
                    if (!_customClips.TryGetValue(key, out AnimationClip clip))
                    {
                        clip = StudioGenericAnims.BakeCustom(seg.custom, st.entry, variant, _filmFolder, st.generic, film.warnings, out _);
                        _customClips[key] = clip;
                    }
                    seg.clip = clip;
                }
                else if (st.generic && seg.entry != null)
                {
                    seg.clip = StudioGenericAnims.GetGeneric(st.entry, seg.entry, variant, film.warnings);
                }
            }
            segs.RemoveAll(sg => sg.clip == null);
        }

        /// <summary>
        /// Generic mode: a looping standing idle covers the whole film on the base track (so the avatar-less rig never
        /// falls back to its bind T-pose), and the acting clips sit on an override track that eases in/out over it.
        /// The idle's first frame is also written into the scene pose as the Timeline default.
        /// </summary>
        private static void CreateGenericAnimTracks(TimelineAsset tl, TrackAsset group, string trackName, Animator animator,
                                                    ActorState st, bool mutated, List<AnimSeg> segs, float filmEnd, FilmInstance film)
        {
            string variant = StudioGenericAnims.VariantName(mutated);
            ResolveSegClips(st, mutated, segs, film);

            StudioCatalog.ClipEntry idleEntry = StudioGenericAnims.FindStandingIdle(st.entry, mutated);
            AnimationClip idle = idleEntry != null ? StudioGenericAnims.GetGeneric(st.entry, idleEntry, variant, film.warnings) : null;

            var baseTrack = tl.CreateTrack<AnimationTrack>(group, trackName);
            baseTrack.trackOffset = TrackOffset.ApplySceneOffsets;
            film.director.SetGenericBinding(baseTrack, animator);
            if (idle != null)
            {
                TimelineClip ic = baseTrack.CreateClip(idle);
                ic.start = 0;
                ic.duration = Mathf.Max(0.1f, filmEnd);
                ic.displayName = $"idle ({idleEntry.clipId})";
                idle.SampleAnimation(animator.gameObject, 0f);
            }
            else if (segs.Count > 0 || mutated == st.initialMutated)
            {
                film.warnings.Add($"'{st.entry.actorId}' ({variant}) has no standing idle clip; gaps between anims show the bind pose.");
            }

            if (segs.Count == 0) return;
            if (idle == null)
            {
                FillAnimTrack(baseTrack, segs, filmEnd, false);
                return;
            }
            var acting = tl.CreateTrack<AnimationTrack>(baseTrack, $"{trackName} Acting");
            FillAnimTrack(acting, segs, filmEnd, true);
        }

        /// <param name="easeOutIntoBase">Override tracks: also ease out of the last clip and of clips followed by a gap,
        /// so the idle underneath takes over smoothly.</param>
        private static void FillAnimTrack(AnimationTrack track, List<AnimSeg> segs, float filmEnd, bool easeOutIntoBase)
        {
            segs.Sort((a, b) => a.start.CompareTo(b.start));
            float easeMax = easeOutIntoBase ? 0.25f : 0.15f;
            TimelineClip prev = null;
            for (int i = 0; i < segs.Count; i++)
            {
                AnimSeg seg = segs[i];
                float end = Mathf.Min(seg.end, filmEnd);
                if (i + 1 < segs.Count) end = Mathf.Min(end, segs[i + 1].start + AnimBlend);
                if (end - seg.start < 0.1f) end = seg.start + 0.1f;

                TimelineClip c = track.CreateClip(seg.clip);
                c.start = seg.start;
                c.duration = end - seg.start;
                c.displayName = seg.id;

                if (prev != null && prev.end > c.start)
                {
                    double overlap = prev.end - c.start;
                    c.blendInDuration = overlap;
                    prev.blendOutDuration = overlap;
                }
                else if (c.start > 0.01)
                {
                    c.easeInDuration = Mathf.Min(easeMax, (float)c.duration * 0.3f);
                    if (prev != null) prev.easeOutDuration = Mathf.Min(easeMax, (float)prev.duration * 0.3f);
                }
                prev = c;
            }
            if (easeOutIntoBase && prev != null && prev.end < filmEnd - 0.01f)
                prev.easeOutDuration = Mathf.Min(easeMax, (float)prev.duration * 0.3f);
        }

        // ── Cameras ───────────────────────────────────────────────────────────────

        private struct ShotRigClipData
        {
            public Vector3 fromOffset, toOffset;
            public float orbitDegrees, fromFov, toFov, dutch, whipPanDegrees, whipPanStart, fromZoom, toZoom;
            public AnimationCurve ease;

            public void ApplyTo(ShotRigClip c)
            {
                c.fromOffset = fromOffset; c.toOffset = toOffset; c.orbitDegrees = orbitDegrees;
                c.fromFov = fromFov; c.toFov = toFov; c.dutch = dutch;
                c.whipPanDegrees = whipPanDegrees; c.whipPanStart = whipPanStart;
                c.fromZoomScale = fromZoom; c.toZoomScale = toZoom;
                c.ease = ease ?? AnimationCurve.EaseInOut(0, 0, 1, 1);
            }
        }

        private static CinemachineCamera BuildShotCamera(Shot shot, Dictionary<string, ActorState> states, StudioSet set, Transform parent, out ShotRigClipData rig)
        {
            rig = default;
            ShotCamera sc = shot.camera ?? new ShotCamera();

            states.TryGetValue(sc.subjectActorId ?? "", out ActorState subject);
            states.TryGetValue(sc.secondaryActorId ?? "", out ActorState secondary);
            Transform targetT = set != null ? set.FindTargetTransform(sc.targetId) : null;
            if (subject == null && targetT == null)
                foreach (ActorState any in states.Values) { subject = any; break; }
            if (subject == null && targetT == null) return null;

            string framing = sc.framing;
            if ((framing == "over_shoulder" || framing == "pov") && (subject == null || secondary == null || secondary == subject))
                framing = "medium";

            StudioCameraSpot spot = set != null && !string.IsNullOrEmpty(sc.spotId) ? set.FindSpot(sc.spotId) : null;
            Vector3? foreground = set != null && spot == null ? set.FindTargetPosition(sc.foregroundId) : null;

            Transform follow;
            Transform lookAt;
            Vector3 offset;
            float fov;
            float screenY;
            float subjH;
            bool throughGlass = false;

            if (targetT != null)
            {
                // Prop / mark shot: frame the object itself, from the side the subject (or the set) is on.
                float r = TargetRadius(set, sc.targetId);
                float frame = framing switch
                {
                    "extreme_close_up" => r * 1.4f,
                    "close_up" => r * 2f,
                    "medium" => r * 4f,
                    "wide" => r * 8f + 1f,
                    "extreme_wide" => r * 14f + 3f,
                    _ => r * 2.6f,
                };
                frame = Mathf.Max(0.18f, frame);
                follow = targetT;
                lookAt = targetT;
                screenY = 0f;
                subjH = Mathf.Max(0.5f, frame * 2f);
                Vector3 focus = targetT.position;
                Vector3 cam;
                if (spot != null) { cam = spot.transform.position; throughGlass = spot.throughGlass; }
                else if (foreground != null) cam = SolveForegroundCamera(focus, foreground.Value, Mathf.Max(0.4f, frame * 1.7f), 0f, sc, subjH, shot.id, out throughGlass);
                else cam = SolveTargetCamera(focus, frame, subject, set, sc, shot.id, out throughGlass);
                offset = cam - follow.position;
                fov = FitFov(frame, Vector3.Distance(cam, focus));
            }
            else if (framing == "over_shoulder" || framing == "pov")
            {
                bool subjMut = MutatedDuringShot(subject, shot);
                bool secMut = MutatedDuringShot(secondary, shot);
                subjH = SubjectHeight(subject, subjMut);
                Vector3 dir = (subject.pos - secondary.pos).WithY0();
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Quaternion.Euler(0, secondary.yaw, 0) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Transform secFace = secondary.actor.GetAnchor("face", secMut);
                float secFaceH = secFace.position.y - secondary.actor.transform.position.y;
                float secScale = Mathf.Max(0.4f, SubjectHeight(secondary, secMut) / 2.6f);
                follow = secondary.actor.transform;
                lookAt = subject.actor.GetAnchor("face", subjMut);
                if (framing == "over_shoulder")
                {
                    offset = -dir * (0.85f * secScale) + right * (0.45f * secScale) + Vector3.up * (secFaceH * 1.02f);
                    screenY = -0.08f;
                    fov = FitFov(subjH * 0.45f, Vector3.Distance(follow.position + offset, lookAt.position));
                }
                else
                {
                    offset = dir * 0.15f + Vector3.up * secFaceH;
                    screenY = 0f;
                    fov = Mathf.Clamp(FitFov(subjH * 0.7f, Vector3.Distance(follow.position + offset, lookAt.position)), 35f, 65f);
                }
            }
            else
            {
                bool mut = MutatedDuringShot(subject, shot);
                subjH = SubjectHeight(subject, mut);
                (float frameK, float distK, string anchor, float sy) size = ShotSizes.TryGetValue(framing ?? "", out var sz) ? sz : ShotSizes["medium"];
                follow = subject.actor.transform;
                lookAt = subject.actor.GetAnchor(size.anchor, mut);
                screenY = size.sy;
                Vector3 focus = lookAt.position;
                float frame = size.frameK * subjH;
                float dist = size.distK * subjH;
                Vector3 cam;
                if (spot != null)
                {
                    cam = spot.transform.position;
                    StudioSight.IsClear(cam, focus, out throughGlass);
                    throughGlass |= spot.throughGlass;
                }
                else if (foreground != null) cam = SolveForegroundCamera(focus, foreground.Value, dist, subject.yaw, sc, subjH, shot.id, out throughGlass, subject.pos.y);
                else cam = SolveCamera(focus, subject.yaw, subjH, dist, sc, subject.pos.y, shot.id, out throughGlass);
                offset = cam - follow.position;
                fov = FitFov(frame, Vector3.Distance(cam, focus));
            }
            if (sc.fov > 1f) fov = sc.fov;
            float scale = Mathf.Max(0.3f, subjH / 2.6f);
            bool deocclude = framing != "pov" && framing != "over_shoulder" && !throughGlass && spot == null && foreground == null;

            var go = new GameObject($"Shot_{shot.id}");
            go.transform.SetParent(parent, false);
            var vcam = go.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = follow;
            vcam.Target.LookAtTarget = lookAt;
            vcam.Target.CustomLookAtTarget = true;
            LensSettings lens = vcam.Lens;
            lens.FieldOfView = fov;
            lens.Dutch = sc.dutch;
            lens.NearClipPlane = 0.05f;
            vcam.Lens = lens;

            var body = go.AddComponent<CinemachineFollow>();
            Unity.Cinemachine.TargetTracking.TrackerSettings tracker = body.TrackerSettings;
            tracker.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            tracker.PositionDamping = new Vector3(0.25f, 0.25f, 0.25f);
            body.TrackerSettings = tracker;
            body.FollowOffset = offset;

            var aim = go.AddComponent<CinemachineRotationComposer>();
            ScreenComposerSettings comp = aim.Composition;
            comp.ScreenPosition = new Vector2(0f, screenY);
            aim.Composition = comp;
            aim.Damping = framing == "pov" ? Vector2.zero : new Vector2(0.3f, 0.3f);

            go.AddComponent<CinemachineRecomposer>();

            // Runtime deocclusion for moving subjects / camera moves (start pose is pre-solved by the camera solver).
            // POV / OTS deliberately sit next to a body; glass, spot and foreground shots deliberately look through/past things.
            if (deocclude)
            {
                var deoccluder = go.AddComponent<CinemachineDeoccluder>();
                deoccluder.CollideAgainst = Physics.DefaultRaycastLayers;
                deoccluder.MinimumDistanceFromTarget = 0.3f;
                CinemachineDeoccluder.ObstacleAvoidance avoid = deoccluder.AvoidObstacles;
                avoid.Enabled = true;
                avoid.CameraRadius = 0.15f;
                avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraDistance;
                avoid.MaximumEffort = 4;
                avoid.MinimumOcclusionTime = 0f;
                avoid.SmoothingTime = 0f;
                avoid.Damping = 0.3f;
                avoid.DampingWhenOccluded = 0f; // snap in front of occluders — no frames of blocked view
                deoccluder.AvoidObstacles = avoid;
            }

            if (sc.shake > 0.01f)
            {
                var noise = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePresetPath);
                noise.AmplitudeGain = sc.shake * 1.5f;
                noise.FrequencyGain = 1f;
            }

            go.transform.position = follow.position + offset;
            go.transform.LookAt(lookAt);

            // Camera move → ShotRig clip data
            float a = Mathf.Clamp01(sc.moveAmount <= 0f ? 0.5f : sc.moveAmount);
            Vector3 h = offset.WithY0();
            Vector3 up = Vector3.up * offset.y;
            Vector3 lateral = h.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, h.normalized) : Vector3.right;
            rig.fromOffset = offset;
            rig.toOffset = offset;
            rig.fromFov = fov;
            rig.toFov = fov;
            rig.dutch = sc.dutch;
            rig.fromZoom = 1f;
            rig.toZoom = 1f;
            rig.whipPanStart = 0.8f;
            rig.ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

            switch (sc.move)
            {
                case "dolly_in":
                    rig.fromOffset = h * (1f + 0.3f * a) + up;
                    rig.toOffset = h * (1f - 0.4f * a) + up;
                    break;
                case "dolly_out":
                    rig.fromOffset = h * (1f - 0.4f * a) + up;
                    rig.toOffset = h * (1f + 0.3f * a) + up;
                    break;
                case "orbit_left":
                    rig.orbitDegrees = -(25f + 65f * a);
                    break;
                case "orbit_right":
                    rig.orbitDegrees = 25f + 65f * a;
                    break;
                case "crane_up":
                    rig.fromOffset = offset - Vector3.up * (0.7f * a * scale);
                    rig.toOffset = offset + Vector3.up * (0.9f * a * scale);
                    break;
                case "crane_down":
                    rig.fromOffset = offset + Vector3.up * (0.9f * a * scale);
                    rig.toOffset = offset - Vector3.up * (0.7f * a * scale);
                    break;
                case "truck_left":
                    rig.fromOffset = offset + lateral * (0.6f * a);
                    rig.toOffset = offset - lateral * (0.6f * a);
                    break;
                case "truck_right":
                    rig.fromOffset = offset - lateral * (0.6f * a);
                    rig.toOffset = offset + lateral * (0.6f * a);
                    break;
                case "whip_pan":
                    rig.whipPanDegrees = 75f;
                    break;
                case "crash_zoom":
                    rig.toZoom = 1f - 0.55f * a;
                    rig.ease = new AnimationCurve(new Keyframe(0f, 0f, 0f, 8f), new Keyframe(0.18f, 1f, 0f, 0f), new Keyframe(1f, 1f));
                    break;
            }

            body.FollowOffset = rig.fromOffset;
            return vcam;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Occlusion adjustments from the last build, surfaced as build warnings.</summary>
        private static readonly List<string> LastOcclusionNotes = new();

        /// <summary>
        /// Variant the actor shows for most of this shot: its state at shot start, flipped if the shot itself
        /// swaps it in the first half (cameras are built before the shot's actions are applied).
        /// </summary>
        private static bool MutatedDuringShot(ActorState st, Shot shot)
        {
            bool mutated = st.mutated;
            if (shot.actions == null) return mutated;
            foreach (ActorAction a in shot.actions)
                if (a.actorId == st.entry.actorId && a.type == "variant_swap" && a.t <= shot.duration * 0.5f)
                    mutated = !mutated;
            return mutated && st.actor.HasMutatedArt;
        }

        private static float SubjectHeight(ActorState st, bool mutated)
        {
            float h = st.actor.GetHeight(mutated);
            return h > 0.4f && h < 6f ? h : Mathf.Max(0.5f, st.headHeight * 1.12f);
        }

        private static float TargetRadius(StudioSet set, string id)
        {
            StudioPoint p = set != null ? set.FindPoint(id) : null;
            return p != null ? Mathf.Clamp(p.radius, 0.05f, 2.5f) : 0.4f;
        }

        /// <summary>Vertical FOV that shows <paramref name="frame"/> metres of picture height at <paramref name="distance"/>.</summary>
        private static float FitFov(float frame, float distance) =>
            Mathf.Clamp(2f * Mathf.Atan(frame / (2f * Mathf.Max(0.05f, distance))) * Mathf.Rad2Deg, 10f, 85f);

        /// <summary>Camera height relative to the focus point for a camera.angle (ground is handled by the caller).</summary>
        private static float AngleHeight(string angle, float subjH) => angle switch
        {
            "low" => -0.36f * subjH,
            "high" => 0.30f * subjH,
            "overhead" => 0.85f * subjH,
            _ => 0f,
        };

        private static readonly float[] SolveYaws = { 0f, 20f, -20f, 40f, -40f, 65f, -65f, 90f, -90f, 130f, -130f, 180f };
        private static readonly float[] SolveDistances = { 1f, 0.85f, 0.7f, 0.55f, 0.42f };
        private static readonly float[] SolveHeights = { 1f, 0.55f, 0.15f };
        private const float CameraClearance = 0.18f;

        /// <summary>
        /// Picks the camera position for an actor shot: preferred = slightly off the subject's facing, at the shot
        /// size's distance and the requested angle. Candidates (yaw around the subject × distance × angle strength) are
        /// scored by how far they deviate; the cheapest one with room for the camera and a clear sight line to the
        /// focus (glass counts as clear) wins. Falls back to pulling in along the preferred direction.
        /// </summary>
        private static Vector3 SolveCamera(Vector3 focus, float subjectYaw, float subjH, float dist, ShotCamera sc, float groundY, string shotId, out bool throughGlass)
        {
            Physics.SyncTransforms();
            throughGlass = false;
            float baseYaw = subjectYaw + 10f;
            float wantDh = sc.angle == "ground" ? (groundY + 0.25f) - focus.y : AngleHeight(sc.angle, subjH);
            bool dolly = sc.move == "dolly_in" || sc.move == "dolly_out";

            float best = float.MaxValue;
            Vector3 bestCam = default;
            float bestYaw = 0f, bestDs = 1f, bestHs = 1f;
            foreach (float yaw in SolveYaws)
            foreach (float ds in SolveDistances)
            foreach (float hs in SolveHeights)
            {
                float cost = Mathf.Abs(yaw) / 90f * 0.8f + (1f - ds) * 1.6f + (Mathf.Abs(wantDh) > 0.01f ? (1f - hs) * 0.6f : 0f);
                if (cost >= best) continue;
                if (hs < 1f && Mathf.Abs(wantDh) < 0.01f) continue;
                Vector3 cam = CandidatePosition(focus, baseYaw + yaw, dist * ds, wantDh * hs, groundY);
                if (!IsGoodCamera(cam, focus, dolly, out bool glass)) continue;
                best = cost; bestCam = cam; throughGlass = glass;
                bestYaw = yaw; bestDs = ds; bestHs = hs;
            }

            if (best < float.MaxValue)
            {
                if (Mathf.Abs(bestYaw) > 0.1f || bestDs < 0.99f || bestHs < 0.99f)
                    LastOcclusionNotes.Add($"{shotId}: camera solved around set geometry (yaw {bestYaw:+0;-0;0}°, distance x{bestDs:0.##}, angle x{bestHs:0.##}; lens refitted).");
                return bestCam;
            }

            // Nothing clear: keep the intended angle but stop short of the first obstruction.
            Vector3 cam0 = CandidatePosition(focus, baseYaw, dist, wantDh, groundY);
            Vector3 dir = cam0 - focus;
            float open = StudioSight.OpenDistance(focus, dir, dir.magnitude);
            float keep = Mathf.Max(0.35f, open - CameraClearance);
            LastOcclusionNotes.Add($"{shotId}: no clear angle; camera pulled in to {keep:0.00}m from the subject (lens widened).");
            return focus + dir.normalized * Mathf.Min(keep, dir.magnitude);
        }

        private static Vector3 CandidatePosition(Vector3 focus, float yaw, float dist, float dh, float groundY)
        {
            float y = Mathf.Max(dh, groundY + 0.2f - focus.y);
            float horiz = Mathf.Sqrt(Mathf.Max(dist * dist - y * y, 0.35f * 0.35f * dist * dist));
            return focus + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * horiz + Vector3.up * y;
        }

        private static bool IsGoodCamera(Vector3 cam, Vector3 focus, bool dolly, out bool throughGlass)
        {
            throughGlass = false;
            if (!StudioSight.HasClearance(cam, CameraClearance)) return false;
            if (!StudioSight.IsClear(cam, focus, out throughGlass)) return false;
            if (dolly)
            {
                Vector3 far = focus + (cam - focus) * 1.3f;
                if (!StudioSight.IsClear(focus, far) || !StudioSight.HasClearance(far, CameraClearance * 0.8f)) return false;
            }
            return true;
        }

        /// <summary>
        /// Foreground framing: the camera stands beyond <paramref name="foreground"/> as seen from the subject, so the point
        /// (window glass, bell, counter) sits between lens and subject. Lifts the camera to see over opaque props.
        /// </summary>
        private static Vector3 SolveForegroundCamera(Vector3 focus, Vector3 foreground, float dist, float subjectYaw, ShotCamera sc, float subjH,
            string shotId, out bool throughGlass, float groundY = float.NegativeInfinity)
        {
            Physics.SyncTransforms();
            throughGlass = false;
            Vector3 toFg = (foreground - focus).WithY0();
            float df = toFg.magnitude;
            Vector3 dirH = df > 0.05f ? toFg / df : Quaternion.Euler(0f, subjectYaw, 0f) * Vector3.forward;
            float dh = sc.angle == "ground" && !float.IsNegativeInfinity(groundY) ? (groundY + 0.25f) - focus.y : AngleHeight(sc.angle, subjH);

            Vector3? fallback = null;
            foreach (float beyond in new[] { 0.45f, 0.8f, 1.2f, 0.25f })
            foreach (float lift in new[] { 0f, 0.12f, 0.25f, 0.4f })
            {
                float horiz = Mathf.Max(df + beyond, dist * 0.5f);
                Vector3 cam = focus + dirH * horiz + Vector3.up * (dh + lift * subjH * 0.5f);
                if (!StudioSight.HasClearance(cam, 0.12f)) continue;
                fallback ??= cam;
                if (StudioSight.IsClear(cam, focus, out throughGlass)) return cam;
            }
            if (fallback != null)
            {
                LastOcclusionNotes.Add($"{shotId}: foreground framing partly blocks the subject (no clear line past the foreground point).");
                throughGlass = true;
                return fallback.Value;
            }
            LastOcclusionNotes.Add($"{shotId}: no room for the camera behind the foreground point; solved a normal angle instead.");
            return SolveCamera(focus, subjectYaw, subjH, dist, sc, float.IsNegativeInfinity(groundY) ? focus.y - subjH : groundY, shotId, out throughGlass);
        }

        /// <summary>
        /// Prop insert: looks down at the point (angle sets the elevation) from the side the subject is on (or the set's
        /// marks), at a distance fitted to the object's size, choosing the least-rotated clear direction.
        /// </summary>
        private static Vector3 SolveTargetCamera(Vector3 focus, float frame, ActorState subject, StudioSet set, ShotCamera sc, string shotId, out bool throughGlass)
        {
            Physics.SyncTransforms();
            throughGlass = false;
            Vector3 pref = (subject != null ? subject.actor.transform.position : set != null ? set.MarksCentre() : focus + Vector3.forward) - focus;
            pref = pref.WithY0();
            float prefYaw = pref.sqrMagnitude > 0.01f ? Quaternion.LookRotation(pref).eulerAngles.y : 0f;
            float elev = sc.angle switch { "eye_level" => 10f, "low" => -12f, "high" => 55f, "overhead" => 82f, "ground" => -5f, _ => 32f };
            float dist = Mathf.Max(0.3f, frame * 1.7f);

            float best = float.MaxValue;
            Vector3 bestCam = focus + Quaternion.Euler(-elev, prefYaw, 0f) * Vector3.back * -dist;
            foreach (float yaw in SolveYaws)
            foreach (float ds in new[] { 1f, 0.75f, 0.55f })
            {
                float cost = Mathf.Abs(yaw) / 90f * 0.8f + (1f - ds) * 1.4f;
                if (cost >= best) continue;
                Vector3 dir = Quaternion.Euler(-elev, prefYaw + yaw, 0f) * Vector3.forward;
                Vector3 cam = focus + dir * (dist * ds);
                if (!StudioSight.HasClearance(cam, 0.1f) || !StudioSight.IsClear(cam, focus, out bool glass)) continue;
                best = cost; bestCam = cam; throughGlass = glass;
            }
            if (best == float.MaxValue) LastOcclusionNotes.Add($"{shotId}: no clear view of the insert target; camera may be blocked.");
            return bestCam;
        }

        private static void SetAudioVolume(AudioPlayableAsset asset, float volume)
        {
            var so = new SerializedObject(asset);
            SerializedProperty p = so.FindProperty("m_ClipProperties.volume");
            if (p == null) return;
            p.floatValue = Mathf.Clamp(volume <= 0f ? 1f : volume, 0f, 2f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string BuildSrt(Screenplay s)
        {
            var sb = new StringBuilder();
            int n = 1;
            foreach (Shot shot in s.shots)
            {
                if (string.IsNullOrWhiteSpace(shot.caption)) continue;
                sb.AppendLine(n++.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine($"{SrtTime(shot.start)} --> {SrtTime(shot.start + shot.duration)}");
                sb.AppendLine(shot.caption.Trim());
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string SrtTime(float seconds)
        {
            var ts = System.TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
        }

        public static void EnsureFolder(string assetFolder)
        {
            assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }
    }
}
