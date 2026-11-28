using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bezi;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>Bezi-callable entry points for scripting the Film Studio pipeline.</summary>
    public static class FilmStudioBeziActions
    {
        [BeziAction("Film Studio: enter Film Mode — open Main.unity (prompting to save modified scenes if it isn't loaded) with the FilmStudio.unity overlay (sets/marks, films, film camera) loaded additively on top. Returns the overlay path.")]
        public static string FilmStudioOpenScene()
        {
            return StudioSceneUtility.EnterFilmMode(out var scene) ? scene.path : "cancelled";
        }

        [BeziAction("Film Studio: switch Film Mode on/off. On = Main.unity + film overlay; pressing Play runs the film with gameplay suppressed (recording mode). Off = overlay unloaded; Play runs the normal game. Main.unity is never saved.")]
        public static string FilmStudioSetFilmMode(bool on)
        {
            bool ok = StudioSceneUtility.SetFilmMode(on);
            return $"{(ok ? "ok" : "failed/cancelled")}: Film Mode is {(StudioSceneUtility.IsFilmMode ? "ON" : "OFF")}";
        }

        [BeziAction("Film Studio: one-time migration of sets imported by the old copy-based importer. Deletes the copied environments from the overlay (Main.unity renders the real ones), keeps marks, builds camera occluders and reports copy-to-Main drift per set.")]
        public static string FilmStudioMigrateSetsToMain() => StudioSetImporter.MigrateCopiedSets();

        [BeziAction("Film Studio: register a Main.unity environment root (e.g. '---ENV_Booth') as a film set in the Film Mode overlay — auto-generated marks plus invisible camera occluders, nothing copied — then refresh the catalog's sets. Returns the mark ids.")]
        public static string FilmStudioImportSet(string envRootName, string setId)
        {
            StudioSet set = StudioSetImporter.ImportSet(envRootName, setId);
            if (set == null) return "failed (see console)";
            StudioCatalogBuilder.RefreshSets();
            return $"{set.setId}: {set.marks.Count} marks -> {string.Join(", ", set.marks.Select(m => m.markId))}";
        }

        [BeziAction("Film Studio: refresh only the sets/marks section of the StudioCatalog asset (fast).")]
        public static string FilmStudioRefreshSets()
        {
            StudioCatalogBuilder.RefreshSets();
            StudioCatalog c = StudioCatalogBuilder.LoadOrCreate();
            return string.Join(", ", c.sets.Select(s => $"{s.setId} ({s.marks.Count} marks, {s.points.Count} points, {s.cameraSpots.Count} camera spots)"));
        }

        [BeziAction("Film Studio: analyze an imported set's environment (empty setId = all sets): rebuild camera occluders (glass marked see-through), " +
                    "add story marks found anywhere in Main.unity around the set (existing marks are never removed), generate points of interest " +
                    "(window glass, shutter, bell, posters, props), per-mark context (facing, open space, nearby points, visibility) and validated camera " +
                    "spots, then refresh the catalog. Returns a summary per set.")]
        public static string FilmStudioAnalyzeSet(string setId)
        {
            if (!StudioSceneUtility.EnsureOpen(out UnityEngine.SceneManagement.Scene overlay)) return "cancelled";
            GameObject setsRoot = StudioSceneUtility.FindRoot(overlay, StudioSceneUtility.SetsRootName);
            if (setsRoot == null) return "no --SETS root in the overlay";
            var results = new System.Collections.Generic.List<string>();
            foreach (StudioSet set in setsRoot.GetComponentsInChildren<StudioSet>(true))
            {
                if (!string.IsNullOrWhiteSpace(setId) && set.setId != setId.Trim()) continue;
                results.Add($"{set.setId}: {StudioSetAnalyzer.Analyze(set)}");
            }
            if (results.Count == 0) return $"set '{setId}' not found";
            StudioCatalogBuilder.RefreshSets();
            return string.Join("\n", results);
        }

        [BeziAction("Film Studio: set an actor's film-only size multiplier (0.3-1.6; 1 = prefab size). Spawned film actors are scaled, camera framing adapts and the director sees the new heightM; the game prefab is untouched. Survives catalog rebuilds.")]
        public static string FilmStudioSetActorScale(string actorId, float scale)
        {
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreateRaw();
            StudioCatalog.ActorEntry entry = catalog.FindActor(actorId);
            if (entry == null) return $"actor '{actorId}' not found";
            entry.filmScale = Mathf.Clamp(scale, 0.3f, 1.6f);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return $"{actorId}: filmScale = {entry.filmScale:0.###} (rebuild the film to apply)";
        }
        [BeziAction("Film Studio: incrementally sync the catalog cast — adds newly created characters (every SuspectData.CharacterPrefab plus character prefabs in the cast search folders) and drops actors whose prefab was deleted. Existing actorIds/clips are untouched. Set dryRun to list what would be added without changing the catalog.")]
        public static string FilmStudioSyncCast(bool dryRun)
        {
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreateRaw();
            if (dryRun)
            {
                var known = new System.Collections.Generic.HashSet<GameObject>(catalog.actors.Select(a => a.prefab));
                var missing = StudioCatalogBuilder.DiscoverActorPrefabs().Where(p => !known.Contains(p))
                    .Select(AssetDatabase.GetAssetPath).ToList();
                var dead = catalog.actors.Where(a => a.prefab == null).Select(a => a.actorId).ToList();
                return $"{catalog.actors.Count} actors in catalog. Would add {missing.Count}:\n  {string.Join("\n  ", missing)}\n" +
                       $"Would drop {dead.Count}: {string.Join(", ", dead)}";
            }
            StudioCatalogBuilder.SyncCast(catalog, out string summary);
            return $"{summary}\nCast now ({catalog.actors.Count}): {string.Join(", ", catalog.actors.Select(a => a.actorId))}";
        }



        [BeziAction("Film Studio: build a Timeline film in FilmStudio.unity from screenplay JSON (Screenplay schema). Validates/auto-repairs first. Returns build summary and warnings.")]
        public static string FilmStudioBuildFromJson(string screenplayJson, string slug, int revision)
        {
            var s = JsonConvert.DeserializeObject<Screenplay>(screenplayJson);
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreate();
            ValidationReport report = ScreenplayValidator.Validate(s, catalog, FilmStudioSettings.instance.maxDurationSec);
            FilmInstance film = FilmBuilder.Build(s, catalog, slug, Mathf.Max(1, revision));
            if (film == null) return "build failed (see console)\n" + report.ToNotes();
            return $"built {film.timelinePath} ({film.duration:0.0}s, director '{film.DirectorPath}')\n{report.ToNotes()}\n{string.Join("\n", film.warnings)}";
        }

        [BeziAction("Film Studio director: return the pending director request posted by the Film Studio window (Bezi backend) as full context — director rules, screenplay JSON schema, catalog subset (set marks, cast clips, SFX) and the task (brief, or current screenplay + notes + locked shots). If nothing is pending, returns available sets/actors.", IsReadOnly = true)]
        public static string FilmStudioGetDirectorRequest()
        {
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreate();
            BeziDirectorBridge.PendingRequest req = BeziDirectorBridge.GetPending();
            return req != null ? BeziDirectorBridge.ComposeContext(req, catalog) : BeziDirectorBridge.ComposeIdleSummary(catalog);
        }

        [BeziAction("Film Studio director: build ad-hoc director context (rules, schema, catalog subset, task) for a new film without a pending window request. castActorIdsCsv is comma-separated actorIds.", IsReadOnly = true)]
        public static string FilmStudioGetDirectorContext(string setId, string castActorIdsCsv, string premise, float targetLengthSec)
        {
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreate();
            var brief = new Brief
            {
                setId = setId ?? "",
                castActorIds = (castActorIdsCsv ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
                premise = premise ?? "",
                targetLengthSec = targetLengthSec > 0 ? targetLengthSec : 20f,
            };
            var req = new BeziDirectorBridge.PendingRequest { requestId = "adhoc", kind = "generate", brief = brief };
            return BeziDirectorBridge.ComposeContext(req, catalog);
        }

        [BeziAction("Film Studio director: submit a complete screenplay JSON as the director's answer. Validates against the catalog (REJECTED + errors if invalid — fix and resubmit), restores locked shots on revisions, hands it to the Film Studio window and builds the Timeline. Returns slug/revision, duration and build warnings; then preview with FilmStudioPreviewAt.")]
        public static string FilmStudioSubmitScreenplay(string screenplayJson)
        {
            return BeziDirectorBridge.Submit(screenplayJson, StudioCatalogBuilder.LoadOrCreate());
        }

        [BeziAction("Film Studio: generate a screenplay with the configured Claude backend (Claude Code CLI or Anthropic API — not Bezi), validate it, and build the Timeline. Slow (30-120s). castActorIdsCsv is comma-separated actorIds.")]
        public static async Task<string> FilmStudioGenerateWithClaude(string setId, string castActorIdsCsv, string premise, float targetLengthSec)
        {
            if (FilmStudioSettings.instance.backend == DirectorBackend.Bezi)
                return "Backend is set to Bezi; use FilmStudioGetDirectorContext + FilmStudioSubmitScreenplay instead.";
            StudioCatalog catalog = StudioCatalogBuilder.LoadOrCreate();
            var brief = new Brief
            {
                setId = setId ?? "",
                castActorIds = (castActorIdsCsv ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
                premise = premise ?? "",
                targetLengthSec = targetLengthSec > 0 ? targetLengthSec : 20f,
            };
            DirectorResult r = await ClaudeDirectorClient.GenerateAsync(brief, catalog, CancellationToken.None).AsTask();
            if (!r.ok) return $"{FilmStudioSettings.instance.backend} failed: {r.error}";

            ValidationReport report = ScreenplayValidator.Validate(r.screenplay, catalog, FilmStudioSettings.instance.maxDurationSec);
            string slug = FilmStudioWindow.UniqueSlug(StudioCatalogBuilder.Slug(string.IsNullOrWhiteSpace(r.screenplay.title) ? "short" : r.screenplay.title));
            FilmInstance film = FilmBuilder.Build(r.screenplay, catalog, slug, 1);
            if (film == null) return "Generated but build failed (see Console).\n" + report.ToNotes();
            return $"{FilmStudioSettings.instance.backend}: '{r.screenplay.title}' — {r.screenplay.shots.Count} shots, {film.duration:0.0}s, slug '{slug}' " +
                   $"(tokens in {r.inputTokens} / out {r.outputTokens}).\nHook: {r.screenplay.hook}\n{report.ToNotes()}\n{string.Join("\n", film.warnings)}";
        }

        [BeziAction("Film Studio: delete the Generic retarget cache (all actors, or one actorId) so catalog clips are re-converted on the next build. Use after changing a rig, the baker, or a source clip import.")]
        public static string FilmStudioClearGenericCache(string actorId)
        {
            string root = FilmStudioSettings.instance.genericCacheFolder.TrimEnd('/');
            string path = string.IsNullOrWhiteSpace(actorId) ? root : $"{root}/{actorId.Trim()}";
            bool ok = AssetDatabase.DeleteAsset(path);
            return ok ? $"deleted {path}" : $"nothing to delete at {path}";
        }

        [BeziAction("Film Studio director: render a contact-sheet PNG of a built film's customAnim on an isolated copy of that actor's rig variant (only that variant drawn, avatar-less like the film). views: up to 4 of front, side, threeQuarter, back, top. Returns the absolute PNG path for logAttachmentFile.", IsReadOnly = true)]
        public static string FilmStudioRenderCustomAnim(string slug, string actorId, string animId, string variant, int frameCount, string views)
        {
            StudioCatalog.ActorEntry entry = StudioCatalogBuilder.LoadOrCreate().FindActor(actorId);
            if (entry == null) return $"actor '{actorId}' not found";
            variant = string.IsNullOrWhiteSpace(variant) ? "base" : variant.Trim();
            string path = $"{FilmStudioSettings.instance.filmsFolder.TrimEnd('/')}/{slug}/Anims/{animId}_{variant}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) == null) return $"no baked clip at {path} (build the film first; check the variant)";
            var list = string.IsNullOrWhiteSpace(views) ? null : views.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            return AnimGen.AnimPreviewRenderer.RenderBodySheet(path, StudioGenericAnims.RigPath(entry), frameCount, 256, list, variant);
        }

        [BeziAction("Film Studio: set the edit-mode preview time of a built film (by slug) and evaluate it so the Game/Scene view shows that frame.", IsReadOnly = false)]
        public static string FilmStudioPreviewAt(string slug, float time)
        {
            FilmInstance film = FilmBuilder.Find(slug);
            if (film == null) return $"film '{slug}' not found";
            StudioSceneUtility.EvaluatePreview(film.director, Mathf.Clamp(time, 0f, film.duration));
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            string camInfo = cam != null ? $" | cam {cam.position:F2} rot {cam.eulerAngles:F0}" : "";
            return $"{slug} at {film.director.time:0.00}s / {film.duration:0.00}s{camInfo}";
        }
    }
}
