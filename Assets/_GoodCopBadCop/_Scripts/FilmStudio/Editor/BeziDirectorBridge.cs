using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// "Bezi" director backend. The Film Studio window posts a pending request (brief / current screenplay / notes)
    /// to Library/FilmStudio; Bezi reads it through <see cref="FilmStudioBeziActions"/>, writes the screenplay, and
    /// submits it back. Submissions are validated here and delivered to the open window (or built directly).
    /// </summary>
    public static class BeziDirectorBridge
    {
        private const string Log = "[FilmStudio]";
        private static readonly string RequestPath = Path.GetFullPath("Library/FilmStudio/bezi_request.json");
        private static readonly string ResponsePath = Path.GetFullPath("Library/FilmStudio/bezi_response.json");

        public const string ChatPrompt = "Direct the pending Film Studio request (use the film-studio-director skill).";

        [Serializable]
        public class PendingRequest
        {
            public string requestId;
            /// <summary>"generate" or "revise".</summary>
            public string kind = "generate";
            public string createdUtc;
            public Brief brief = new();
            public string slug = "";
            public string currentScreenplayJson;
            public List<ShotNote> notes = new();
            public string globalNote = "";
        }

        [Serializable]
        public class SubmittedResponse
        {
            public string requestId;
            public string kind;
            public string slug;
            public int revision;
            public string screenplayJson;
        }

        /// <summary>Set by the open Film Studio window; returns a status summary after accepting the screenplay.</summary>
        public static Func<DirectorResult, PendingRequest, string> WindowReceiver;

        // ── Request lifecycle ─────────────────────────────────────────────────────

        public static PendingRequest Post(string kind, Brief brief, string slug, Screenplay current, List<ShotNote> notes, string globalNote)
        {
            var req = new PendingRequest
            {
                requestId = Guid.NewGuid().ToString("N").Substring(0, 8),
                kind = kind,
                createdUtc = DateTime.UtcNow.ToString("o"),
                brief = brief,
                slug = slug ?? "",
                currentScreenplayJson = current != null ? JsonConvert.SerializeObject(current) : null,
                notes = notes ?? new List<ShotNote>(),
                globalNote = globalNote ?? "",
            };
            Directory.CreateDirectory(Path.GetDirectoryName(RequestPath)!);
            File.WriteAllText(RequestPath, JsonConvert.SerializeObject(req, Formatting.Indented));
            if (File.Exists(ResponsePath)) File.Delete(ResponsePath);
            Debug.Log($"{Log} Posted Bezi director request {req.requestId} ({kind}). In Bezi chat: \"{ChatPrompt}\"");
            return req;
        }

        public static PendingRequest GetPending()
        {
            try { return File.Exists(RequestPath) ? JsonConvert.DeserializeObject<PendingRequest>(File.ReadAllText(RequestPath)) : null; }
            catch (Exception e) { Debug.LogWarning($"{Log} Unreadable Bezi request: {e.Message}"); return null; }
        }

        public static void ClearPending()
        {
            if (File.Exists(RequestPath)) File.Delete(RequestPath);
        }

        /// <summary>A response submitted while no window was listening (window closed or mid domain reload).</summary>
        public static SubmittedResponse TakeUnclaimedResponse(string requestId)
        {
            if (!File.Exists(ResponsePath)) return null;
            try
            {
                var r = JsonConvert.DeserializeObject<SubmittedResponse>(File.ReadAllText(ResponsePath));
                if (r == null || (requestId != null && r.requestId != requestId)) return null;
                File.Delete(ResponsePath);
                return r;
            }
            catch (Exception) { return null; }
        }

        // ── Context for Bezi ──────────────────────────────────────────────────────

        /// <summary>Everything a director needs: rules, schema, catalog subset and the task.</summary>
        public static string ComposeContext(PendingRequest req, StudioCatalog catalog)
        {
            Brief brief = req.brief ?? new Brief();
            Screenplay current = string.IsNullOrEmpty(req.currentScreenplayJson) ? null : JsonConvert.DeserializeObject<Screenplay>(req.currentScreenplayJson);
            string task = req.kind == "revise" && current != null
                ? ClaudeDirectorClient.ComposeReviseText(brief, current, req.notes, req.globalNote)
                : ClaudeDirectorClient.ComposeGenerateText(brief);

            var sb = new StringBuilder();
            sb.AppendLine($"# Film Studio director request {req.requestId} ({req.kind})");
            sb.AppendLine("Answer by calling FilmStudioSubmitScreenplay with the FULL screenplay JSON (wherever the rules say `submit_screenplay`).");
            sb.AppendLine("Times inside a shot are relative to the shot start; `start`/`durationSec` are recomputed, you may omit them.");
            sb.AppendLine();
            sb.AppendLine("## Director rules");
            sb.AppendLine(ClaudeDirectorClient.LoadSystemPrompt());
            sb.AppendLine();
            sb.AppendLine("## Screenplay JSON schema");
            sb.AppendLine(ClaudeDirectorClient.ScreenplaySchema().ToString(Formatting.None));
            sb.AppendLine();
            sb.AppendLine("## " + ClaudeDirectorClient.CatalogText(brief, catalog));
            sb.AppendLine();
            sb.AppendLine("## Task");
            sb.AppendLine(task);
            return sb.ToString();
        }

        /// <summary>Short orientation when nothing is pending: sets, mutant-capable actors.</summary>
        public static string ComposeIdleSummary(StudioCatalog catalog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("No pending Film Studio request. Either ask the user to press Generate/Revise in Tools > Film Studio with the Bezi backend,");
            sb.AppendLine("or call FilmStudioGetDirectorContext(setId, castActorIdsCsv, premise) to direct ad hoc.");
            if (catalog != null)
            {
                sb.AppendLine("Sets: " + string.Join(", ", catalog.sets.Select(s => $"{s.setId} ({s.marks.Count} marks)")));
                sb.AppendLine("Actors: " + string.Join(", ", catalog.actors.Select(a => a.actorId + (a.hasMutatedVariant ? "*" : ""))) + "   (* = has mutated variant)");
            }
            return sb.ToString();
        }

        // ── Submission ────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates and delivers a Bezi-written screenplay. Hard validation errors reject the submission so Bezi can fix
        /// and resubmit. Returns a human/agent-readable summary.
        /// </summary>
        public static string Submit(string screenplayJson, StudioCatalog catalog)
        {
            JToken token;
            try { token = JToken.Parse(screenplayJson); }
            catch (JsonException e) { return $"REJECTED: invalid JSON ({e.Message})."; }

            DirectorResult r = ClaudeDirectorClient.FromScreenplayToken(token, screenplayJson);
            if (!r.ok) return $"REJECTED: {r.error}";

            PendingRequest req = GetPending() ?? AdHocFromWindow();
            if (req?.kind == "revise" && !string.IsNullOrEmpty(req.currentScreenplayJson))
            {
                var current = JsonConvert.DeserializeObject<Screenplay>(req.currentScreenplayJson);
                ClaudeDirectorClient.RestoreLockedShots(current, r.screenplay);
                r.screenplay.RecomputeTiming();
            }

            ValidationReport report = ScreenplayValidator.Validate(r.screenplay, catalog, FilmStudioSettings.instance.maxDurationSec);
            if (report.HasErrors)
                return "REJECTED: fix these validation errors and resubmit.\n" + report.ToNotes();

            req ??= new PendingRequest { requestId = "adhoc", kind = "generate" };
            ClearPending();

            if (WindowReceiver != null)
                return WindowReceiver(r, req) + (report.warnings.Count > 0 ? "\n" + report.ToNotes() : "");

            // No window open: build directly and leave the response for the window to adopt later.
            string slug = !string.IsNullOrEmpty(req.slug) ? req.slug
                : FilmStudioWindow.UniqueSlug(StudioCatalogBuilder.Slug(string.IsNullOrWhiteSpace(r.screenplay.title) ? "short" : r.screenplay.title));
            int revision = FilmStudioWindow.NextRevision(slug);
            FilmInstance film = FilmBuilder.Build(r.screenplay, catalog, slug, revision);
            if (film == null) return "Accepted, but the build failed (see Console).";
            File.WriteAllText(ResponsePath, JsonConvert.SerializeObject(new SubmittedResponse
            {
                requestId = req.requestId, kind = req.kind, slug = slug, revision = revision,
                screenplayJson = JsonConvert.SerializeObject(r.screenplay)
            }));
            return $"Accepted and built {film.timelinePath} ({film.duration:0.0}s, slug '{slug}' v{revision}). Window was closed; it will adopt this screenplay when opened.\n"
                   + string.Join("\n", film.warnings) + (report.warnings.Count > 0 ? "\n" + report.ToNotes() : "");
        }

        /// <summary>When nothing is pending but a window has a screenplay, treat a submission as a revision of it.</summary>
        private static PendingRequest AdHocFromWindow()
        {
            FilmStudioWindow w = FilmStudioWindow.ActiveWindow;
            if (w == null || w.CurrentScreenplay == null) return null;
            return new PendingRequest
            {
                requestId = "adhoc", kind = "revise", brief = w.CurrentBrief, slug = w.CurrentSlug,
                currentScreenplayJson = JsonConvert.SerializeObject(w.CurrentScreenplay)
            };
        }
    }
}
