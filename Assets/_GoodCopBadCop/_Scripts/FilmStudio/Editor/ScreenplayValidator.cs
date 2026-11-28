using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    public class ValidationReport
    {
        public List<string> errors = new();
        public List<string> warnings = new();
        public List<string> fixes = new();

        public bool HasErrors => errors.Count > 0;

        /// <summary>Formats the report as revision notes suitable for feeding back to the director.</summary>
        public string ToNotes()
        {
            var sb = new StringBuilder();
            if (errors.Count > 0)
            {
                sb.AppendLine("Validation errors (must fix):");
                foreach (string e in errors) sb.Append("- ").AppendLine(e);
            }
            if (warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (string w in warnings) sb.Append("- ").AppendLine(w);
            }
            if (fixes.Count > 0)
            {
                sb.AppendLine("Auto-fixes applied:");
                foreach (string f in fixes) sb.Append("- ").AppendLine(f);
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>Validates a <see cref="Screenplay"/> against a <see cref="StudioCatalog"/> and auto-repairs what it can, in place.</summary>
    public static class ScreenplayValidator
    {
        private const float MinShotDuration = 0.4f;
        private const float MaxShotDuration = 8f;
        private const float HookMaxFirstShot = 2f;
        private const int MaxShotCountWarning = 25;

        public static ValidationReport Validate(Screenplay s, StudioCatalog c, float maxDurationSec)
        {
            var r = new ValidationReport();
            if (s == null)
            {
                r.errors.Add("Screenplay is null.");
                return r;
            }

            // ---- Structural repairs ----
            if (s.cast == null) { s.cast = new List<CastEntry>(); r.fixes.Add("cast was null -> empty list"); }
            if (s.shots == null) { s.shots = new List<Shot>(); r.fixes.Add("shots was null -> empty list"); }
            s.customAnims ??= new List<CustomAnim>();
            s.customAnims.RemoveAll(x => x == null);
            foreach (CustomAnim ca in s.customAnims)
            {
                ca.id = (ca.id ?? "").Trim();
                ca.actorId ??= "";
                ca.description ??= "";
                ca.variant = FixEnum(ca.variant, ScreenplayVocabulary.AnimVariants, "base", $"customAnim '{ca.id}' variant", r);
            }
            if (s.fps <= 0) { s.fps = 30; r.fixes.Add("fps defaulted to 30"); }
            s.title ??= "Untitled";
            s.logline ??= "";
            s.hook ??= "";
            s.setId ??= "";

            int nullCast = s.cast.RemoveAll(x => x == null);
            if (nullCast > 0) r.fixes.Add($"Removed {nullCast} null cast entr{(nullCast == 1 ? "y" : "ies")}");
            int nullShots = s.shots.RemoveAll(x => x == null);
            if (nullShots > 0) r.fixes.Add($"Removed {nullShots} null shot(s)");

            foreach (CastEntry ce in s.cast)
            {
                ce.actorId ??= "";
                ce.role ??= "";
                ce.startMarkId ??= "";
                ce.variant = FixEnum(ce.variant, ScreenplayVocabulary.Variants, "base", $"cast '{ce.actorId}' variant", r);
            }

            RepairShotIds(s, r);

            foreach (Shot shot in s.shots)
            {
                RepairShot(shot, r);
            }

            s.RecomputeTiming();

            if (s.shots.Count == 0) r.errors.Add("Screenplay has no shots.");

            // ---- Catalog validation ----
            if (c == null)
            {
                r.errors.Add("No StudioCatalog available to validate against.");
                AddGlobalWarnings(s, r, maxDurationSec);
                return r;
            }

            StudioCatalog.SetEntry set = c.FindSet(s.setId);
            if (set == null)
                r.errors.Add($"Unknown setId '{s.setId}'.");

            bool MarkExists(string markId) =>
                set != null && set.marks != null && set.marks.Any(m => m != null && m.markId == markId);
            bool PointExists(string id) => set != null && set.HasPoint(id);
            bool TargetExists(string id) => MarkExists(id) || PointExists(id);

            var castIds = new HashSet<string>();
            var currentVariant = new Dictionary<string, string>();
            foreach (CastEntry ce in s.cast)
            {
                StudioCatalog.ActorEntry actor = c.FindActor(ce.actorId);
                if (actor == null)
                {
                    r.errors.Add($"Cast: unknown actorId '{ce.actorId}'.");
                    continue;
                }
                if (!castIds.Add(ce.actorId))
                    r.warnings.Add($"Cast: actor '{ce.actorId}' listed more than once.");

                if (string.IsNullOrEmpty(ce.startMarkId) || !MarkExists(ce.startMarkId))
                    r.errors.Add($"Cast '{ce.actorId}': startMarkId '{ce.startMarkId}' is missing or not a mark in set '{s.setId}'.");

                if (ce.variant == "mutated" && !actor.hasMutatedVariant)
                    r.errors.Add($"Cast '{ce.actorId}': variant 'mutated' but actor has no mutated variant.");

                currentVariant[ce.actorId] = ce.variant;
            }

            // Custom anims (baked per rig when the film is built).
            var customs = new Dictionary<(string actorId, string id), CustomAnim>();
            var usedCustoms = new HashSet<(string, string)>();
            foreach (CustomAnim ca in s.customAnims)
            {
                string label = $"customAnim '{ca.id}'";
                if (!StudioGenericAnims.IsValidId(ca.id)) { r.errors.Add($"{label}: id must be non-empty letters/digits/_/- only."); continue; }
                StudioCatalog.ActorEntry actor = c.FindActor(ca.actorId);
                if (actor == null) { r.errors.Add($"{label}: unknown actorId '{ca.actorId}'."); continue; }
                if (!castIds.Contains(ca.actorId)) r.warnings.Add($"{label}: actor '{ca.actorId}' is not in the cast.");
                if (actor.FindClip(ca.id) != null) r.errors.Add($"{label}: id clashes with a catalog clipId of '{ca.actorId}'; pick another id.");
                if (!customs.TryAdd((ca.actorId, ca.id), ca)) r.errors.Add($"{label}: duplicate id for actor '{ca.actorId}'.");
                if (ca.variant != "base" && !actor.hasMutatedVariant) r.errors.Add($"{label}: variant '{ca.variant}' but '{ca.actorId}' has no mutated variant.");
                foreach (string v in ca.variant == "both" ? new[] { "base", "mutated" } : new[] { ca.variant })
                    if (v == "base" || actor.hasMutatedVariant)
                        if (!StudioGenericAnims.HasHumanoidRig(actor, v))
                            r.errors.Add($"{label}: '{ca.actorId}' has no Humanoid-mapped {v} rig, so plans can't be baked for it.");
                try
                {
                    AnimGen.AnimPlan plan = StudioGenericAnims.ParsePlan(ca);
                    if (plan.duration > 12f) r.errors.Add($"{label}: duration {plan.duration:0.#}s is too long (max 12s; split it).");
                    string bp = StudioGenericAnims.CheckBasePose(plan, actor);
                    if (bp != null) r.errors.Add($"{label}: {bp}");
                    if (!FilmStudioSettings.instance.genericAnimation && ((plan.bones?.Count ?? 0) > 0 || (plan.blendShapes?.Count ?? 0) > 0))
                        r.warnings.Add($"{label}: bones/blendShapes need Generic animation mode (Film Studio settings); they are ignored in Humanoid mode.");
                }
                catch (Exception e)
                {
                    r.errors.Add($"{label}: plan is invalid ({e.Message}).");
                }
            }

            foreach (Shot shot in s.shots)
            {
                string sid = shot.id;

                // Camera subjects
                ShotCamera cam = shot.camera;
                if (string.IsNullOrEmpty(cam.subjectActorId))
                {
                    if (cam.framing != "insert" && string.IsNullOrEmpty(cam.targetId))
                        r.warnings.Add($"{sid}: camera has no subjectActorId or targetId (only expected for insert shots).");
                }
                else if (c.FindActor(cam.subjectActorId) == null)
                {
                    r.errors.Add($"{sid}: camera subject '{cam.subjectActorId}' is not a known actor.");
                }
                else if (!castIds.Contains(cam.subjectActorId))
                {
                    r.warnings.Add($"{sid}: camera subject '{cam.subjectActorId}' is not in the cast.");
                }

                if (!string.IsNullOrEmpty(cam.secondaryActorId) && c.FindActor(cam.secondaryActorId) == null)
                    r.errors.Add($"{sid}: camera secondary actor '{cam.secondaryActorId}' is not a known actor.");
                if (!string.IsNullOrEmpty(cam.spotId) && (set == null || !set.HasSpot(cam.spotId)))
                    r.errors.Add($"{sid}: camera.spotId '{cam.spotId}' is not a camera spot of set '{s.setId}'.");
                if (!string.IsNullOrEmpty(cam.targetId) && !TargetExists(cam.targetId))
                    r.errors.Add($"{sid}: camera.targetId '{cam.targetId}' is not a point or mark of set '{s.setId}'.");
                if (!string.IsNullOrEmpty(cam.foregroundId) && !TargetExists(cam.foregroundId))
                    r.errors.Add($"{sid}: camera.foregroundId '{cam.foregroundId}' is not a point or mark of set '{s.setId}'.");
                if (!string.IsNullOrEmpty(cam.spotId) && !string.IsNullOrEmpty(cam.foregroundId))
                    r.warnings.Add($"{sid}: spotId and foregroundId both set; the spot wins.");

                // Actions, in time order so variant tracking is correct within a shot.
                foreach (ActorAction a in shot.actions.OrderBy(x => x.t))
                {
                    StudioCatalog.ActorEntry actor = c.FindActor(a.actorId);
                    if (actor == null)
                    {
                        r.errors.Add($"{sid}: action '{a.type}' references unknown actorId '{a.actorId}'.");
                        continue;
                    }
                    if (!castIds.Contains(a.actorId))
                        r.warnings.Add($"{sid}: actor '{a.actorId}' acts but is not in the cast.");

                    string variant = currentVariant.TryGetValue(a.actorId, out string v) ? v : "base";

                    switch (a.type)
                    {
                        case "anim":
                        {
                            StudioCatalog.ClipEntry clip = actor.FindClip(a.clipId);
                            if (clip == null && customs.TryGetValue((a.actorId, a.clipId), out CustomAnim ca))
                            {
                                usedCustoms.Add((a.actorId, a.clipId));
                                if (ca.variant != "both" && ca.variant != variant)
                                    r.errors.Add($"{sid}: customAnim '{a.clipId}' is baked for the '{ca.variant}' rig but '{a.actorId}' is '{variant}' at t={a.t:0.##} (use variant 'both' or match it).");
                            }
                            else if (clip == null)
                            {
                                r.errors.Add($"{sid}: clipId '{a.clipId}' is neither a catalog clip nor a customAnim of actor '{a.actorId}'.");
                            }
                            else if (!FilmStudioSettings.instance.genericAnimation)
                            {
                                string cv = string.IsNullOrEmpty(clip.variant) ? "base" : clip.variant;
                                if (cv != "both" && cv != variant)
                                    r.warnings.Add($"{sid}: clip '{a.clipId}' is for the '{cv}' variant but '{a.actorId}' is '{variant}' at t={a.t:0.##}.");
                            }
                            break;
                        }
                        case "move":
                            if (string.IsNullOrEmpty(a.toMarkId) || !MarkExists(a.toMarkId))
                                r.errors.Add($"{sid}: move for '{a.actorId}' targets markId '{a.toMarkId}' which is not in set '{s.setId}'.");
                            break;
                        case "variant_swap":
                            if (!actor.hasMutatedVariant)
                                r.errors.Add($"{sid}: variant_swap on '{a.actorId}' which has no mutated variant.");
                            else
                                currentVariant[a.actorId] = variant == "mutated" ? "base" : "mutated";
                            break;
                        case "face":
                            if (!string.IsNullOrEmpty(a.faceTargetId) && c.FindActor(a.faceTargetId) == null && !TargetExists(a.faceTargetId))
                                r.warnings.Add($"{sid}: face target '{a.faceTargetId}' is neither a known actor nor a mark/point in the set.");
                            break;
                    }
                }

                // SFX: unknown ids are dropped with a warning.
                for (int i = shot.sfx.Count - 1; i >= 0; i--)
                {
                    SfxCue cue = shot.sfx[i];
                    if (c.FindSfx(cue.sfxId) == null)
                    {
                        r.warnings.Add($"{sid}: unknown sfxId '{cue.sfxId}' — cue dropped.");
                        r.fixes.Add($"{sid}: dropped sfx cue '{cue.sfxId}'");
                        shot.sfx.RemoveAt(i);
                    }
                }
            }

            foreach (CustomAnim ca in s.customAnims)
                if (customs.ContainsKey((ca.actorId, ca.id)) && !usedCustoms.Contains((ca.actorId, ca.id)))
                    r.warnings.Add($"customAnim '{ca.id}' is never used by an anim action.");

            AddGlobalWarnings(s, r, maxDurationSec);
            return r;
        }

        private static void AddGlobalWarnings(Screenplay s, ValidationReport r, float maxDurationSec)
        {
            if (maxDurationSec > 0f && s.durationSec > maxDurationSec * 1.1f)
                r.errors.Add($"Total duration {s.durationSec:0.##}s exceeds the maximum {maxDurationSec:0.##}s.");

            if (s.shots.Count > 0 && s.shots[0].duration > HookMaxFirstShot)
                r.warnings.Add($"First shot is {s.shots[0].duration:0.##}s — the hook should land within {HookMaxFirstShot:0.#}s.");

            if (s.shots.Count > MaxShotCountWarning)
                r.warnings.Add($"Shot count {s.shots.Count} exceeds {MaxShotCountWarning}.");
        }

        private static void RepairShotIds(Screenplay s, ValidationReport r)
        {
            bool needsRenumber = false;
            var seen = new HashSet<string>();
            foreach (Shot shot in s.shots)
            {
                if (string.IsNullOrWhiteSpace(shot.id) || !seen.Add(shot.id))
                {
                    needsRenumber = true;
                    break;
                }
            }
            if (!needsRenumber) return;

            for (int i = 0; i < s.shots.Count; i++)
                s.shots[i].id = $"s{i + 1:00}";
            r.fixes.Add("Missing or duplicate shot ids -> renumbered s01..");
        }

        private static void RepairShot(Shot shot, ValidationReport r)
        {
            string sid = shot.id;
            shot.caption ??= "";
            shot.notes ??= "";
            if (shot.camera == null) { shot.camera = new ShotCamera(); r.fixes.Add($"{sid}: camera was null -> default"); }
            if (shot.actions == null) { shot.actions = new List<ActorAction>(); r.fixes.Add($"{sid}: actions was null -> empty"); }
            if (shot.sfx == null) { shot.sfx = new List<SfxCue>(); r.fixes.Add($"{sid}: sfx was null -> empty"); }
            int na = shot.actions.RemoveAll(x => x == null);
            if (na > 0) r.fixes.Add($"{sid}: removed {na} null action(s)");
            int ns = shot.sfx.RemoveAll(x => x == null);
            if (ns > 0) r.fixes.Add($"{sid}: removed {ns} null sfx cue(s)");

            float d = Sanitize(shot.duration, 2f);
            float cd = Mathf.Clamp(d, MinShotDuration, MaxShotDuration);
            if (!Mathf.Approximately(cd, shot.duration))
                r.fixes.Add($"{sid}: duration {shot.duration:0.##} clamped to {cd:0.##}");
            shot.duration = cd;

            float sp = Mathf.Clamp(Sanitize(shot.speed, 1f), 0.1f, 3f);
            if (!Mathf.Approximately(sp, shot.speed))
                r.fixes.Add($"{sid}: speed {shot.speed:0.##} clamped to {sp:0.##}");
            shot.speed = sp;

            float maxT = Mathf.Max(0f, shot.duration - 0.05f);

            // Camera
            ShotCamera cam = shot.camera;
            cam.subjectActorId ??= "";
            cam.secondaryActorId ??= "";
            cam.framing = FixEnum(cam.framing, ScreenplayVocabulary.Framings, "medium", $"{sid} camera.framing", r);
            cam.move = FixEnum(cam.move, ScreenplayVocabulary.Moves, "static", $"{sid} camera.move", r);
            cam.blendIn = FixEnum(cam.blendIn, ScreenplayVocabulary.Blends, "cut", $"{sid} camera.blendIn", r);
            cam.angle = string.IsNullOrWhiteSpace(cam.angle) ? "eye_level" : FixEnum(cam.angle, ScreenplayVocabulary.Angles, "eye_level", $"{sid} camera.angle", r);
            cam.spotId = (cam.spotId ?? "").Trim();
            cam.targetId = (cam.targetId ?? "").Trim();
            cam.foregroundId = (cam.foregroundId ?? "").Trim();

            float fov = Sanitize(cam.fov, 0f);
            if (fov != 0f)
            {
                float cf = fov <= 0f ? 0f : Mathf.Clamp(fov, 15f, 100f);
                if (!Mathf.Approximately(cf, cam.fov)) r.fixes.Add($"{sid}: fov {cam.fov:0.##} clamped to {cf:0.##}");
                cam.fov = cf;
            }
            else cam.fov = 0f;

            cam.moveAmount = Mathf.Clamp01(Sanitize(cam.moveAmount, 0.5f));
            cam.shake = Mathf.Clamp01(Sanitize(cam.shake, 0f));
            cam.dutch = Mathf.Clamp(Sanitize(cam.dutch, 0f), -45f, 45f);

            // Actions
            foreach (ActorAction a in shot.actions)
            {
                a.actorId ??= "";
                a.clipId ??= "";
                a.toMarkId ??= "";
                a.faceTargetId ??= "";
                a.type = FixEnum(a.type, ScreenplayVocabulary.ActionTypes, "anim", $"{sid} action type", r);
                float t = Mathf.Clamp(Sanitize(a.t, 0f), 0f, maxT);
                if (!Mathf.Approximately(t, a.t)) r.fixes.Add($"{sid}: action '{a.type}' t {a.t:0.##} clamped to {t:0.##}");
                a.t = t;
                a.moveSpeed = Mathf.Max(0f, Sanitize(a.moveSpeed, 0f));
            }

            // SFX
            foreach (SfxCue cue in shot.sfx)
            {
                cue.sfxId ??= "";
                float t = Mathf.Clamp(Sanitize(cue.t, 0f), 0f, maxT);
                if (!Mathf.Approximately(t, cue.t)) r.fixes.Add($"{sid}: sfx '{cue.sfxId}' t {cue.t:0.##} clamped to {t:0.##}");
                cue.t = t;
                cue.volume = Mathf.Clamp(Sanitize(cue.volume, 1f), 0f, 2f);
            }
        }

        private static string FixEnum(string value, string[] allowed, string fallback, string what, ValidationReport r)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (Array.IndexOf(allowed, value) >= 0) return value;
                string norm = value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
                if (Array.IndexOf(allowed, norm) >= 0)
                {
                    r.fixes.Add($"{what}: '{value}' normalized to '{norm}'");
                    return norm;
                }
            }
            r.fixes.Add($"{what}: unknown value '{value}' -> '{fallback}'");
            return fallback;
        }

        private static float Sanitize(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;
    }
}
