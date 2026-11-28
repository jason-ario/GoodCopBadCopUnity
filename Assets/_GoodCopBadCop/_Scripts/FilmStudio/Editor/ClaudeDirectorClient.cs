using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GoodCopBadCop.FilmStudio
{
    [Serializable]
    public class Brief
    {
        public string premise = "";
        public string setId = "";
        public List<string> castActorIds = new();
        public float targetLengthSec = 30f;
        public string tone = "Horror reveal";
        public string hookIdea = "";
    }

    [Serializable]
    public class ShotNote
    {
        public string shotId;
        public string note;
    }

    public class DirectorResult
    {
        public bool ok;
        public Screenplay screenplay;
        public string rawJson;
        public string error;
        public int inputTokens;
        public int outputTokens;
    }

    /// <summary>
    /// Talks to the Anthropic Messages API and forces a <c>submit_screenplay</c> tool call to obtain a <see cref="Screenplay"/>.
    /// Never throws to the caller except <see cref="OperationCanceledException"/>.
    /// </summary>
    public static class ClaudeDirectorClient
    {
        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        private const string ToolName = "submit_screenplay";
        private const string Log = "[FilmStudio]";
        private static readonly int[] RetryDelaysMs = { 2000, 5000 };

        /// <summary>Last request body sent (never contains the API key).</summary>
        public static string LastRequestJson { get; private set; }

        /// <summary>Last raw response body received.</summary>
        public static string LastResponseJson { get; private set; }

        private const string DefaultSystemPrompt =
            "You are a viral-shorts film director for the game \"Uncanny Valley Checkpoint\" (aka Good Cop Bad Cop): a darkly comic, " +
            "uncanny border-checkpoint game where the player inspects residents who may be mutated doppelgangers.\n" +
            "Viral grammar: hook in the first 1.5s, shots 0.6-3s, escalation, a reveal/punchline (mutation reveals via variant_swap are the " +
            "signature beat), optionally a loopable ending.\n" +
            "Vertical 9:16 framing: favor medium, close_up, extreme_close_up, insert and over_shoulder; use wide shots rarely.\n" +
            "Stage against the environment: read each mark's context and the set's points (window glass, shutter, bell, posters...), " +
            "block actors on story marks, cut to prop inserts (camera.targetId), shoot through/past props (camera.foregroundId), " +
            "use cameraSpots (camera.spotId) and camera.angle (high = small/vulnerable, low = imposing).\n" +
            "Visual storytelling only - no voice. 'caption' is optional short, punchy on-screen text added later.\n" +
            "Rules: ONLY use ids present in the catalog (actorId; clipId belonging to that actor, or a customAnims id you defined for that actor; markId " +
            "belonging to the setId; sfxId). Times in actions/sfx are relative to shot start and must be < shot duration. Shot ids are " +
            "\"s01\",\"s02\",... Movement only between marks via \"move\" actions (the tool auto-plays a walk clip). Every cast member needs " +
            "startMarkId. Keep total duration within the requested length.\n" +
            "On revisions: return the FULL screenplay, apply the notes, and keep locked shots byte-identical.\n" +
            "Always answer by calling the submit_screenplay tool.";

        // ------------------------------------------------------------------ Public API

        public static async UniTask<DirectorResult> GenerateAsync(Brief brief, StudioCatalog catalog, CancellationToken ct)
        {
            try
            {
                if (!Preflight(brief, catalog, out DirectorResult fail)) return fail;
                return await DispatchAsync(brief, catalog, ComposeGenerateText(brief), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"{Log} GenerateAsync failed: {e}");
                return Fail($"Unexpected error: {e.Message}");
            }
        }

        public static async UniTask<DirectorResult> ReviseAsync(Brief brief, Screenplay current, IReadOnlyList<ShotNote> notes,
            string globalNote, StudioCatalog catalog, CancellationToken ct)
        {
            try
            {
                if (!Preflight(brief, catalog, out DirectorResult fail)) return fail;
                if (current == null) return Fail("No current screenplay to revise.");

                List<string> lockedIds = LockedShotIds(current);
                DirectorResult result = await DispatchAsync(brief, catalog, ComposeReviseText(brief, current, notes, globalNote), ct);

                if (result.ok && result.screenplay != null && lockedIds.Count > 0)
                {
                    RestoreLockedShots(current, result.screenplay);
                    result.screenplay.RecomputeTiming();
                }
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"{Log} ReviseAsync failed: {e}");
                return Fail($"Unexpected error: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ Prompt composition (shared by all backends)

        internal static List<string> LockedShotIds(Screenplay s) =>
            s?.shots?.Where(x => x != null && x.locked).Select(x => x.id).ToList() ?? new List<string>();

        internal static string ComposeGenerateText(Brief brief)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Write a new short film screenplay for this brief, then call submit_screenplay.");
            sb.AppendLine();
            sb.AppendLine("BRIEF:");
            sb.AppendLine(BriefJson(brief).ToString(Formatting.Indented));
            return sb.ToString();
        }

        internal static string ComposeReviseText(Brief brief, Screenplay current, IReadOnlyList<ShotNote> notes, string globalNote)
        {
            var notesArr = new JArray();
            if (notes != null)
            {
                foreach (ShotNote n in notes)
                {
                    if (n == null || string.IsNullOrWhiteSpace(n.note)) continue;
                    notesArr.Add(new JObject { ["shotId"] = n.shotId ?? "", ["note"] = n.note });
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("Revise the current screenplay. Return the FULL screenplay via submit_screenplay.");
            sb.AppendLine("Apply every shot note and the global note. Shots in lockedShotIds must be returned byte-identical.");
            sb.AppendLine();
            sb.AppendLine("BRIEF:");
            sb.AppendLine(BriefJson(brief).ToString(Formatting.Indented));
            sb.AppendLine();
            sb.AppendLine("CURRENT SCREENPLAY:");
            sb.AppendLine(JsonConvert.SerializeObject(current, Formatting.None));
            sb.AppendLine();
            sb.AppendLine("SHOT NOTES:");
            sb.AppendLine(notesArr.ToString(Formatting.None));
            sb.AppendLine();
            sb.AppendLine("GLOBAL NOTE:");
            sb.AppendLine(string.IsNullOrWhiteSpace(globalNote) ? "(none)" : globalNote.Trim());
            sb.AppendLine();
            sb.AppendLine("lockedShotIds:");
            sb.AppendLine(new JArray(LockedShotIds(current)).ToString(Formatting.None));
            return sb.ToString();
        }

        internal static string CatalogText(Brief brief, StudioCatalog catalog) =>
            "STUDIO CATALOG (the only ids you may use):\n" + StudioCatalogPrompt.ToPromptJson(catalog, brief.setId, brief.castActorIds);

        internal static JObject ScreenplaySchema() => BuildScreenplaySchema(Math.Max(1, FilmStudioSettings.instance.maxShots));

        private static async UniTask<DirectorResult> DispatchAsync(Brief brief, StudioCatalog catalog, string userText, CancellationToken ct)
        {
            switch (FilmStudioSettings.instance.backend)
            {
                case DirectorBackend.ClaudeCodeCli:
                {
                    string stdin = CatalogText(brief, catalog) + "\n\n" + userText;
                    LastRequestJson = stdin;
                    ClaudeCodeCli.Result cli = await ClaudeCodeCli.RunStructuredAsync(LoadSystemPrompt(), stdin, ScreenplaySchema(), ct);
                    LastResponseJson = cli.stdout;
                    if (!cli.ok) return Fail(cli.error);
                    DirectorResult r = FromScreenplayToken(cli.structuredOutput, cli.stdout);
                    r.inputTokens = cli.inputTokens;
                    r.outputTokens = cli.outputTokens;
                    return r;
                }
                case DirectorBackend.AnthropicApi:
                    return await SendAsync(BuildRequestBody(brief, catalog, userText), ct);
                default:
                    return Fail("The Bezi backend is driven from Bezi chat, not from this client.");
            }
        }

        // ------------------------------------------------------------------ Request building

        private static bool Preflight(Brief brief, StudioCatalog catalog, out DirectorResult fail)
        {
            fail = null;
            FilmStudioSettings settings = FilmStudioSettings.instance;
            if (settings.backend == DirectorBackend.AnthropicApi)
            {
                if (string.IsNullOrWhiteSpace(settings.model))
                {
                    fail = Fail("No Claude model set. Enter a current Claude model id in Film Studio settings.");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(FilmStudioSettings.ApiKey))
                {
                    fail = Fail("Missing or invalid Anthropic API key. Set it in Film Studio settings or the ANTHROPIC_API_KEY environment variable.");
                    return false;
                }
            }
            else if (settings.backend == DirectorBackend.ClaudeCodeCli && ClaudeCodeCli.ResolveExecutable() == null)
            {
                fail = Fail("Claude Code CLI not found. Install it (https://docs.claude.com/en/docs/claude-code) and run `claude` once to log in, or set its path in Film Studio settings.");
                return false;
            }
            if (brief == null)
            {
                fail = Fail("Brief is null.");
                return false;
            }
            if (catalog == null)
            {
                fail = Fail("No StudioCatalog loaded. Rebuild the catalog first.");
                return false;
            }
            return true;
        }

        private static JObject BriefJson(Brief brief)
        {
            FilmStudioSettings settings = FilmStudioSettings.instance;
            return new JObject
            {
                ["premise"] = brief.premise ?? "",
                ["setId"] = brief.setId ?? "",
                ["castActorIds"] = new JArray(brief.castActorIds ?? new List<string>()),
                ["targetLengthSec"] = brief.targetLengthSec,
                ["maxDurationSec"] = settings.maxDurationSec,
                ["maxShots"] = settings.maxShots,
                ["fps"] = settings.fps,
                ["tone"] = brief.tone ?? "",
                ["hookIdea"] = brief.hookIdea ?? "",
                ["aspect"] = $"{settings.outputWidth}x{settings.outputHeight} (vertical 9:16)"
            };
        }

        private static JObject BuildRequestBody(Brief brief, StudioCatalog catalog, string userText)
        {
            FilmStudioSettings settings = FilmStudioSettings.instance;
            bool cache = settings.usePromptCaching;

            var systemBlock = new JObject { ["type"] = "text", ["text"] = LoadSystemPrompt() };
            if (cache) systemBlock["cache_control"] = new JObject { ["type"] = "ephemeral" };

            string catalogJson = StudioCatalogPrompt.ToPromptJson(catalog, brief.setId, brief.castActorIds);
            var catalogBlock = new JObject { ["type"] = "text", ["text"] = "STUDIO CATALOG (the only ids you may use):\n" + catalogJson };
            if (cache) catalogBlock["cache_control"] = new JObject { ["type"] = "ephemeral" };

            var userBlock = new JObject { ["type"] = "text", ["text"] = userText };

            return new JObject
            {
                ["model"] = settings.model.Trim(),
                ["max_tokens"] = Math.Max(1024, settings.maxTokens),
                ["temperature"] = Mathf.Clamp01(settings.temperature),
                ["system"] = new JArray(systemBlock),
                ["messages"] = new JArray(new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray(catalogBlock, userBlock)
                }),
                ["tools"] = new JArray(new JObject
                {
                    ["name"] = ToolName,
                    ["description"] = "Submit the complete short-film screenplay. All times inside a shot are relative to the shot start.",
                    ["input_schema"] = BuildScreenplaySchema(Math.Max(1, settings.maxShots))
                }),
                ["tool_choice"] = new JObject { ["type"] = "tool", ["name"] = ToolName }
            };
        }

        internal static string LoadSystemPrompt()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("DirectorSystemPrompt t:TextAsset");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    string text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Log} Could not load DirectorSystemPrompt.txt, using embedded default: {e.Message}");
            }
            return DefaultSystemPrompt;
        }

        // ------------------------------------------------------------------ Schema

        private static JObject BuildScreenplaySchema(int maxShots)
        {
            JObject camera = Obj(new JObject
            {
                ["framing"] = EnumOf(ScreenplayVocabulary.Framings, "Shot size, measured on the subject's real height (insert = the targetId object). Prefer medium/close_up/extreme_close_up/insert/over_shoulder for vertical video."),
                ["subjectActorId"] = Str("Cast actorId the camera frames. May be empty for insert shots that use targetId."),
                ["secondaryActorId"] = Str("Optional second actorId (e.g. the shoulder in over_shoulder)."),
                ["angle"] = EnumOf(ScreenplayVocabulary.Angles, "Camera height vs the subject's face: eye_level (default), low (looking up: imposing), high (looking down: small, vulnerable), overhead, ground."),
                ["spotId"] = Str("Optional cameraSpots id of the set: the camera stands exactly there (validated sight line, may be through glass) and the lens is fitted to the framing."),
                ["targetId"] = Str("Optional pointId or markId to frame instead of an actor (prop inserts: the bell, the shutter, the posters)."),
                ["foregroundId"] = Str("Optional pointId placed between camera and subject: shoot through the window glass, past the bell, from behind the counter."),
                ["fov"] = Num("Vertical FOV override in degrees (15-100). 0 = fitted automatically to the framing (recommended)."),
                ["move"] = EnumOf(ScreenplayVocabulary.Moves, "Camera move during the shot."),
                ["moveAmount"] = Num("Move strength 0..1.", 0, 1),
                ["dutch"] = Num("Dutch roll angle in degrees."),
                ["shake"] = Num("Handheld noise gain 0..1.", 0, 1),
                ["blendIn"] = EnumOf(ScreenplayVocabulary.Blends, "Transition into this shot.")
            }, "framing", "subjectActorId", "move", "blendIn");

            JObject action = Obj(new JObject
            {
                ["actorId"] = Str("Cast actorId performing the action."),
                ["t"] = Num("Seconds from shot start; must be < shot duration.", 0),
                ["type"] = EnumOf(ScreenplayVocabulary.ActionTypes, "anim: play clipId. move: walk to toMarkId. face: turn toward faceTargetId (actorId, markId or pointId). variant_swap: toggle base/mutated. hide/show."),
                ["clipId"] = Str("For anim: a catalog clipId of this actor, or the id of one of this actor's customAnims."),
                ["toMarkId"] = Str("For move: a markId of the set."),
                ["faceTargetId"] = Str("For face: an actorId, markId or pointId."),
                ["moveSpeed"] = Num("For move: metres per second; 0 = default walk speed.", 0)
            }, "actorId", "t", "type");

            JObject sfx = Obj(new JObject
            {
                ["t"] = Num("Seconds from shot start; must be < shot duration.", 0),
                ["sfxId"] = Str("An sfxId from the catalog."),
                ["volume"] = Num("0..1 volume.", 0, 1)
            }, "t", "sfxId");

            JObject shot = Obj(new JObject
            {
                ["id"] = Str("Shot id: s01, s02, ..."),
                ["duration"] = Num("Shot length in seconds (typically 0.6-3).", 0.4, 8),
                ["camera"] = camera,
                ["actions"] = Arr(action),
                ["sfx"] = Arr(sfx),
                ["caption"] = Str("Optional short on-screen text; empty for none."),
                ["speed"] = Num("Playback speed multiplier (1 = normal, <1 slow motion).", 0.1, 3),
                ["notes"] = Str("Director's intent for this shot.")
            }, "id", "duration", "camera", "actions", "sfx");

            JObject cast = Obj(new JObject
            {
                ["actorId"] = Str("An actorId from the catalog."),
                ["role"] = Str("Role in this short."),
                ["startMarkId"] = Str("markId of the set where the actor starts."),
                ["variant"] = EnumOf(ScreenplayVocabulary.Variants, "Starting variant. 'mutated' only if hasMutatedVariant.")
            }, "actorId", "role", "startMarkId", "variant");

            JObject shotsArr = Arr(shot);
            shotsArr["minItems"] = 1;
            shotsArr["maxItems"] = maxShots;

            JObject customAnim = Obj(new JObject
            {
                ["id"] = Str("Unique id (letters/digits/_/-), used as clipId in anim actions. Must not clash with the actor's catalog clipIds."),
                ["actorId"] = Str("Cast actorId this animation is made for (it is baked on that actor's own rig)."),
                ["variant"] = EnumOf(ScreenplayVocabulary.AnimVariants, "Rig to bake for: base, mutated (needs hasMutatedVariant) or both. Must match the actor's variant when played."),
                ["description"] = Str("What the animation shows, beat by beat."),
                ["plan"] = new JObject
                {
                    ["type"] = "object",
                    ["description"] = "AnimPlan (see CUSTOM ANIMATION in the rules): name, duration, fps, loop, basePose {clip: catalog clipId, follow}, " +
                                      "plantFeet, handSpace, tracks {hips, spine, chest, neck, head, shoulders, hands, elbows, fingers, feet, knees, root}, " +
                                      "bones [{bone, chain, space, wave, keys}], blendShapes [{renderer, shape, keys}]. Character space, rig meters, degrees."
                }
            }, "id", "actorId", "variant", "plan");

            return Obj(new JObject
            {
                ["title"] = Str("Short, catchy title."),
                ["logline"] = Str("One-sentence logline."),
                ["hook"] = Str("What grabs the viewer in the first 1.5 seconds."),
                ["setId"] = Str("setId from the catalog."),
                ["fps"] = new JObject { ["type"] = "integer", ["description"] = "Frames per second (30)." },
                ["durationSec"] = Num("Sum of shot durations in seconds."),
                ["cast"] = Arr(cast),
                ["customAnims"] = Arr(customAnim),
                ["shots"] = shotsArr
            }, "title", "logline", "hook", "setId", "cast", "shots");
        }

        private static JObject Str(string desc) => new JObject { ["type"] = "string", ["description"] = desc };

        private static JObject Num(string desc, double? min = null, double? max = null)
        {
            var o = new JObject { ["type"] = "number", ["description"] = desc };
            if (min.HasValue) o["minimum"] = min.Value;
            if (max.HasValue) o["maximum"] = max.Value;
            return o;
        }

        private static JObject EnumOf(string[] values, string desc) =>
            new JObject { ["type"] = "string", ["enum"] = new JArray(values), ["description"] = desc };

        private static JObject Arr(JObject items) => new JObject { ["type"] = "array", ["items"] = items };

        private static JObject Obj(JObject props, params string[] required) => new JObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JArray(required)
        };

        // ------------------------------------------------------------------ Transport

        private static async UniTask<DirectorResult> SendAsync(JObject body, CancellationToken ct)
        {
            string json = body.ToString(Formatting.None);
            LastRequestJson = json;
            byte[] payload = Encoding.UTF8.GetBytes(json);

            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                long code;
                string responseText;
                string transportError;

                using (var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
                {
                    req.uploadHandler = new UploadHandlerRaw(payload) { contentType = "application/json" };
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.timeout = 300;
                    req.SetRequestHeader("x-api-key", FilmStudioSettings.ApiKey);
                    req.SetRequestHeader("anthropic-version", AnthropicVersion);
                    req.SetRequestHeader("content-type", "application/json");

                    try
                    {
                        await req.SendWebRequest().ToUniTask(cancellationToken: ct);
                    }
                    catch (UnityWebRequestException)
                    {
                        // HTTP / network error; body and code are read below.
                    }

                    code = req.responseCode;
                    responseText = req.downloadHandler?.text ?? "";
                    transportError = req.error;
                }

                LastResponseJson = responseText;

                if (code == 200)
                    return ParseResponse(responseText);

                bool retryable = code == 0 || code == 429 || code == 529 || code >= 500;
                if (retryable && attempt < RetryDelaysMs.Length)
                {
                    Debug.LogWarning($"{Log} Request failed (HTTP {code}{(string.IsNullOrEmpty(transportError) ? "" : ", " + transportError)}); retrying in {RetryDelaysMs[attempt] / 1000}s...");
                    await UniTask.Delay(RetryDelaysMs[attempt], DelayType.Realtime, PlayerLoopTiming.Update, ct);
                    continue;
                }

                string apiMessage = ExtractApiError(responseText);
                string error = code switch
                {
                    401 => "Missing or invalid Anthropic API key",
                    0 => $"Network error: {transportError}",
                    _ => $"Anthropic API error (HTTP {code}): {apiMessage ?? transportError ?? "unknown error"}"
                };
                Debug.LogError($"{Log} {error}");
                return new DirectorResult { ok = false, error = error, rawJson = responseText };
            }
        }

        private static string ExtractApiError(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                JObject o = JObject.Parse(text);
                return o["error"]?["message"]?.ToString();
            }
            catch
            {
                return text.Length > 300 ? text.Substring(0, 300) : text;
            }
        }

        private static DirectorResult ParseResponse(string text)
        {
            var result = new DirectorResult { rawJson = text };
            JObject root;
            try
            {
                root = JObject.Parse(text);
            }
            catch (Exception e)
            {
                result.error = $"Could not parse Anthropic response: {e.Message}";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            result.inputTokens = root["usage"]?["input_tokens"]?.Value<int?>() ?? 0;
            result.outputTokens = root["usage"]?["output_tokens"]?.Value<int?>() ?? 0;

            if (root["stop_reason"]?.ToString() == "max_tokens")
            {
                result.error = "Screenplay truncated - raise Max Tokens in Film Studio settings";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            JArray content = root["content"] as JArray;
            JToken toolUse = content?.FirstOrDefault(b => b?["type"]?.ToString() == "tool_use");
            if (toolUse == null || toolUse["input"] == null)
            {
                string raw = content == null
                    ? text
                    : string.Join("\n", content.Where(b => b?["type"]?.ToString() == "text").Select(b => b["text"]?.ToString()));
                result.error = $"Director did not return a screenplay tool call. Response: {raw}";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            DirectorResult parsed = FromScreenplayToken(toolUse["input"], text);
            parsed.inputTokens = result.inputTokens;
            parsed.outputTokens = result.outputTokens;
            return parsed;
        }

        /// <summary>Turns a screenplay JSON token (tool input / CLI structured_output / Bezi submission) into a result.</summary>
        internal static DirectorResult FromScreenplayToken(JToken input, string raw)
        {
            var result = new DirectorResult { rawJson = raw };
            if (input == null || input.Type == JTokenType.Null)
            {
                result.error = "Director returned no screenplay.";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }
            try
            {
                if (input.Type == JTokenType.String) input = JToken.Parse(input.ToString());
                result.rawJson = input.ToString(Formatting.Indented);
                result.screenplay = input.ToObject<Screenplay>();
                if (result.screenplay == null)
                {
                    result.error = "Screenplay was empty.";
                    return result;
                }
                result.screenplay.cast ??= new List<CastEntry>();
                result.screenplay.shots ??= new List<Shot>();
                result.screenplay.RecomputeTiming();
                result.ok = true;
                Debug.Log($"{Log} Screenplay received: '{result.screenplay.title}', {result.screenplay.shots.Count} shots, " +
                          $"{result.screenplay.durationSec:0.##}s.");
            }
            catch (Exception e)
            {
                result.error = $"Could not deserialize screenplay: {e.Message}";
                Debug.LogError($"{Log} {result.error}");
            }
            return result;
        }

        // ------------------------------------------------------------------ Locked shots

        internal static void RestoreLockedShots(Screenplay current, Screenplay revised)
        {
            if (current?.shots == null || revised?.shots == null) return;

            for (int i = 0; i < current.shots.Count; i++)
            {
                Shot original = current.shots[i];
                if (original == null || !original.locked) continue;

                Shot copy = JsonConvert.DeserializeObject<Shot>(JsonConvert.SerializeObject(original));
                int idx = revised.shots.FindIndex(s => s != null && s.id == original.id);
                if (idx >= 0)
                {
                    revised.shots[idx] = copy;
                }
                else
                {
                    revised.shots.Insert(Mathf.Clamp(i, 0, revised.shots.Count), copy);
                    Debug.LogWarning($"{Log} Locked shot '{original.id}' was missing from the revision; re-inserted.");
                }

                // Keep the custom anims a locked shot plays, byte-identical to the originals.
                if (current.customAnims == null) continue;
                revised.customAnims ??= new List<CustomAnim>();
                foreach (ActorAction a in original.actions)
                {
                    if (a == null || a.type != "anim") continue;
                    CustomAnim ca = current.customAnims.Find(x => x != null && x.id == a.clipId && x.actorId == a.actorId);
                    if (ca == null) continue;
                    revised.customAnims.RemoveAll(x => x != null && x.id == ca.id && x.actorId == ca.actorId);
                    revised.customAnims.Add(JsonConvert.DeserializeObject<CustomAnim>(JsonConvert.SerializeObject(ca)));
                }
            }
        }

        private static DirectorResult Fail(string error)
        {
            Debug.LogError($"{Log} {error}");
            return new DirectorResult { ok = false, error = error };
        }
    }
}
