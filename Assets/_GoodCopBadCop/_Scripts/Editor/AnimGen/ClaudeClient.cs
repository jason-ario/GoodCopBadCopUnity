using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GoodCopBadCop.AnimGen
{
    public enum ClaudeBackend { ClaudeCodeCli = 0, AnthropicApi = 1 }

    /// <summary>Per-user AnimGen settings stored in EditorPrefs (never committed to the repo).</summary>
    public static class AnimGenSettings
    {
        const string Prefix = "GoodCopBadCop.AnimGen.";
        public const string CustomLabel = "Custom...";

        /// <summary>Claude Code accepts aliases (latest of each family) or full model names. Empty alias = CLI default.</summary>
        public static readonly string[] CliModels = { "(CLI default)", "fable", "opus", "sonnet", "haiku", CustomLabel };
        public static readonly string[] ApiModels = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", CustomLabel };
        public static readonly string[] Efforts = { "(default)", "low", "medium", "high", "xhigh", "max" };

        public static ClaudeBackend Backend
        {
            get => (ClaudeBackend)EditorPrefs.GetInt(Prefix + "Backend", (int)ClaudeBackend.ClaudeCodeCli);
            set => EditorPrefs.SetInt(Prefix + "Backend", (int)value);
        }

        // ---- Claude Code CLI ----
        public static string CliPath
        {
            get => EditorPrefs.GetString(Prefix + "CliPath", DefaultCliPath());
            set => EditorPrefs.SetString(Prefix + "CliPath", string.IsNullOrWhiteSpace(value) ? DefaultCliPath() : value.Trim());
        }
        /// <summary>Popup entry from <see cref="CliModels"/>.</summary>
        public static string CliModelChoice
        {
            get => EditorPrefs.GetString(Prefix + "CliModelChoice", "sonnet");
            set => EditorPrefs.SetString(Prefix + "CliModelChoice", value ?? "sonnet");
        }
        public static string CliCustomModel
        {
            get => EditorPrefs.GetString(Prefix + "CliCustomModel", "");
            set => EditorPrefs.SetString(Prefix + "CliCustomModel", value?.Trim() ?? "");
        }
        public static string CliEffort
        {
            get => EditorPrefs.GetString(Prefix + "CliEffort", Efforts[0]);
            set => EditorPrefs.SetString(Prefix + "CliEffort", value ?? Efforts[0]);
        }
        /// <summary>Strip ANTHROPIC_API_KEY from the child env so the CLI bills the logged-in subscription.</summary>
        public static bool CliForceSubscription
        {
            get => EditorPrefs.GetBool(Prefix + "CliForceSubscription", true);
            set => EditorPrefs.SetBool(Prefix + "CliForceSubscription", value);
        }
        public static int TimeoutSeconds
        {
            get => EditorPrefs.GetInt(Prefix + "TimeoutSeconds", 600);
            set => EditorPrefs.SetInt(Prefix + "TimeoutSeconds", Mathf.Clamp(value, 30, 3600));
        }

        // ---- Anthropic API ----
        public static string ApiKey
        {
            get => EditorPrefs.GetString(Prefix + "ApiKey", "");
            set => EditorPrefs.SetString(Prefix + "ApiKey", value ?? "");
        }
        /// <summary>Popup entry from <see cref="ApiModels"/>. (Key kept as "Model" for backward compatibility.)</summary>
        public static string ApiModelChoice
        {
            get => EditorPrefs.GetString(Prefix + "Model", "claude-sonnet-5-5");
            set => EditorPrefs.SetString(Prefix + "Model", value ?? "claude-sonnet-5-5");
        }
        public static string ApiCustomModel
        {
            get => EditorPrefs.GetString(Prefix + "ApiCustomModel", "");
            set => EditorPrefs.SetString(Prefix + "ApiCustomModel", value?.Trim() ?? "");
        }
        public static string ApiVersion
        {
            get => EditorPrefs.GetString(Prefix + "ApiVersion", "2023-06-01");
            set => EditorPrefs.SetString(Prefix + "ApiVersion", string.IsNullOrWhiteSpace(value) ? "2023-06-01" : value.Trim());
        }
        public static int MaxTokens
        {
            get => EditorPrefs.GetInt(Prefix + "MaxTokens", 8000);
            set => EditorPrefs.SetInt(Prefix + "MaxTokens", Mathf.Clamp(value, 1000, 64000));
        }
        public static bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

        // ---- Resolved values ----
        /// <summary>Model id to send; null for the CLI default.</summary>
        public static string ResolvedModel
        {
            get
            {
                if (Backend == ClaudeBackend.ClaudeCodeCli)
                {
                    string c = CliModelChoice;
                    if (c == CustomLabel) return string.IsNullOrWhiteSpace(CliCustomModel) ? null : CliCustomModel;
                    return c == CliModels[0] ? null : c;
                }
                string a = ApiModelChoice;
                return a == CustomLabel ? (string.IsNullOrWhiteSpace(ApiCustomModel) ? "claude-sonnet-5-5" : ApiCustomModel) : a;
            }
        }
        public static string ModelLabel => (ResolvedModel ?? "Claude Code default model") + (Backend == ClaudeBackend.ClaudeCodeCli ? " (Claude Code)" : " (API)");

        /// <summary>Null when a request can be sent; otherwise the reason it can't.</summary>
        public static string NotReadyReason
        {
            get
            {
                if (Backend == ClaudeBackend.AnthropicApi)
                    return HasKey ? null : "No Anthropic API key set (Claude Settings).";
                string p = CliPath;
                bool bare = p.IndexOfAny(new[] { '/', '\\' }) < 0;
                return bare || File.Exists(p) ? null : $"Claude Code CLI not found at '{p}' (Claude Settings).";
            }
        }
        public static bool Ready => NotReadyReason == null;

        public static string DefaultCliPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool win = Application.platform == RuntimePlatform.WindowsEditor;
            string local = Path.Combine(home, ".local", "bin", win ? "claude.exe" : "claude");
            if (File.Exists(local)) return local;
            if (!win)
                foreach (var p in new[] { "/opt/homebrew/bin/claude", "/usr/local/bin/claude" })
                    if (File.Exists(p)) return p;
            return "claude"; // resolve from PATH
        }
    }

    /// <summary>
    /// Editor Claude client with two backends, polled on EditorApplication.update (never blocks the editor):
    /// Claude Code CLI in print mode (uses the logged-in subscription) or the Anthropic Messages API (API key).
    /// </summary>
    public sealed class ClaudeClient
    {
        const string Endpoint = "https://api.anthropic.com/v1/messages";
        const int MaxSystemArgChars = 24000; // stay well under the Windows 32K command-line limit

        public sealed class Result
        {
            public bool ok;
            public string text;
            public string error;   // set alongside ok=true for warnings (e.g. truncation)
            public string stopReason;
            public string meta;    // e.g. cost / duration from the CLI
        }

        UnityWebRequest _request;
        Process _proc;
        Task<string> _stdout, _stderr;
        Task _stdin;
        double _deadline;
        Action<Result> _onDone;

        public bool Busy => _request != null || _proc != null;

        /// <param name="imagePath">Optional absolute PNG path (e.g. a preview contact sheet).</param>
        public void Send(string system, string userText, string imagePath, Action<Result> onDone)
        {
            if (Busy) throw new InvalidOperationException("A Claude request is already in flight.");
            string why = AnimGenSettings.NotReadyReason;
            if (why != null) { onDone(new Result { error = why }); return; }
            if (!string.IsNullOrEmpty(imagePath) && !File.Exists(imagePath)) imagePath = null;

            _onDone = onDone;
            try
            {
                if (AnimGenSettings.Backend == ClaudeBackend.ClaudeCodeCli) StartCli(system, userText, imagePath);
                else StartApi(system, userText, imagePath);
            }
            catch (Exception e)
            {
                Cleanup();
                var cb = _onDone; _onDone = null;
                cb?.Invoke(new Result { error = "Could not start request: " + e.Message });
                return;
            }
            _deadline = EditorApplication.timeSinceStartup + AnimGenSettings.TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        public void Cancel()
        {
            if (!Busy) return;
            Finish(new Result { error = "Cancelled." });
        }

        // ------------------------------------------------------------------ CLI

        void StartCli(string system, string userText, string imagePath)
        {
            var args = new StringBuilder("-p --output-format json --no-session-persistence --disable-slash-commands --strict-mcp-config");
            string model = AnimGenSettings.ResolvedModel;
            if (!string.IsNullOrEmpty(model)) args.Append(" --model ").Append(QuoteArg(model));
            string effort = AnimGenSettings.CliEffort;
            if (!string.IsNullOrEmpty(effort) && effort != AnimGenSettings.Efforts[0]) args.Append(" --effort ").Append(QuoteArg(effort));

            string stdinText = userText;
            if (imagePath != null)
            {
                // Read-only image access only: Claude Code loads PNGs through its Read tool.
                args.Append(" --tools Read --allowedTools Read");
                stdinText = $"Before answering, use the Read tool to view the preview contact sheet image at:\n{Path.GetFullPath(imagePath)}\n\n{userText}";
            }
            else args.Append(" --tools \"\"");

            if (!string.IsNullOrEmpty(system))
            {
                if (system.Length <= MaxSystemArgChars) args.Append(" --system-prompt ").Append(QuoteArg(system));
                else stdinText = $"<instructions>\n{system}\n</instructions>\n\n{stdinText}";
            }

            string workDir = imagePath != null ? Path.GetDirectoryName(Path.GetFullPath(imagePath)) : Path.GetFullPath("Library");
            var psi = new ProcessStartInfo
            {
                FileName = AnimGenSettings.CliPath,
                Arguments = args.ToString(),
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (AnimGenSettings.CliForceSubscription)
            {
                psi.EnvironmentVariables.Remove("ANTHROPIC_API_KEY");
                psi.EnvironmentVariables.Remove("ANTHROPIC_AUTH_TOKEN");
            }

            _proc = Process.Start(psi) ?? throw new Exception("Process.Start returned null.");
            _stdout = _proc.StandardOutput.ReadToEndAsync();
            _stderr = _proc.StandardError.ReadToEndAsync();
            var proc = _proc;
            byte[] input = new UTF8Encoding(false).GetBytes(stdinText);
            _stdin = Task.Run(() =>
            {
                try
                {
                    var s = proc.StandardInput.BaseStream;
                    s.Write(input, 0, input.Length);
                    s.Flush();
                    proc.StandardInput.Close();
                }
                catch { /* process exited early; stderr will explain */ }
            });
        }

        Result ParseCli()
        {
            string raw = _stdout.Result ?? "";
            string err = (_stderr.Result ?? "").Trim();
            int code = _proc.ExitCode;
            var r = new Result();
            JObject json = null;
            try
            {
                // Output is a single JSON object; tolerate stray lines before it.
                int a = raw.IndexOf('{');
                if (a >= 0) json = JObject.Parse(raw.Substring(a));
            }
            catch { /* not JSON */ }

            if (json == null)
            {
                r.error = $"Claude Code exited with code {code}. {Truncate(err.Length > 0 ? err : raw, 600)}";
                if (err.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
                    r.error += "\nRun `claude` once in a terminal and log in with your subscription.";
                return r;
            }

            string text = (string)json["result"] ?? "";
            bool isError = (bool?)json["is_error"] ?? false;
            string subtype = (string)json["subtype"];
            double? cost = (double?)json["total_cost_usd"];
            double? ms = (double?)json["duration_ms"];
            r.meta = (ms.HasValue ? $"{ms.Value / 1000.0:0.0}s" : "") + (cost.HasValue ? $", ${cost.Value:0.0000} equiv." : "");

            if (isError || (subtype != null && subtype != "success"))
            {
                r.error = $"Claude Code error ({subtype ?? "error"}): {Truncate(text.Length > 0 ? text : err, 600)}";
                return r;
            }
            r.ok = true;
            r.text = text;
            r.stopReason = subtype;
            return r;
        }

        /// <summary>Quotes one argument for the Windows CreateProcess / .NET argument parser (also fine for Mono).</summary>
        static string QuoteArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char ch in s)
            {
                if (ch == '\\') { backslashes++; continue; }
                if (ch == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(ch); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ API

        void StartApi(string system, string userText, string imagePath)
        {
            var content = new JArray();
            if (imagePath != null)
                content.Add(new JObject
                {
                    ["type"] = "image",
                    ["source"] = new JObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = Convert.ToBase64String(File.ReadAllBytes(imagePath)) },
                });
            content.Add(new JObject { ["type"] = "text", ["text"] = userText });

            var body = new JObject
            {
                ["model"] = AnimGenSettings.ResolvedModel,
                ["max_tokens"] = AnimGenSettings.MaxTokens,
                ["system"] = system,
                ["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = content } },
            };

            _request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = AnimGenSettings.TimeoutSeconds,
            };
            _request.SetRequestHeader("content-type", "application/json");
            _request.SetRequestHeader("x-api-key", AnimGenSettings.ApiKey.Trim());
            _request.SetRequestHeader("anthropic-version", AnimGenSettings.ApiVersion);
            _request.SendWebRequest();
        }

        Result ParseApi()
        {
            var r = new Result();
            string raw = _request.downloadHandler?.text;
            try
            {
                if (_request.result != UnityWebRequest.Result.Success)
                {
                    string apiMsg = null;
                    try { apiMsg = JObject.Parse(raw ?? "{}")["error"]?["message"]?.ToString(); } catch { /* not JSON */ }
                    r.error = $"HTTP {_request.responseCode}: {apiMsg ?? _request.error}";
                }
                else
                {
                    var json = JObject.Parse(raw);
                    var sb = new StringBuilder();
                    foreach (var block in json["content"] ?? new JArray())
                        if ((string)block["type"] == "text") sb.Append((string)block["text"]);
                    r.text = sb.ToString();
                    r.stopReason = (string)json["stop_reason"];
                    r.ok = true;
                    if (r.stopReason == "max_tokens") r.error = "Response hit max_tokens; the plan may be truncated (raise Max Tokens).";
                }
            }
            catch (Exception e) { r.error = "Could not parse response: " + e.Message; }
            return r;
        }

        // ------------------------------------------------------------------ common

        void Poll()
        {
            if (!Busy) { EditorApplication.update -= Poll; return; }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                Finish(new Result { error = $"Timed out after {AnimGenSettings.TimeoutSeconds}s." });
                return;
            }
            if (_request != null)
            {
                if (_request.isDone) Finish(ParseApi());
                return;
            }
            bool exited;
            try { exited = _proc.HasExited; } catch { exited = true; }
            if (!exited || !_stdout.IsCompleted || !_stderr.IsCompleted) return;
            Result r;
            try { r = ParseCli(); }
            catch (Exception e) { r = new Result { error = "Could not read Claude Code output: " + e.Message }; }
            Finish(r);
        }

        void Finish(Result r)
        {
            EditorApplication.update -= Poll;
            Cleanup();
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke(r);
        }

        void Cleanup()
        {
            if (_request != null)
            {
                if (!_request.isDone) _request.Abort();
                _request.Dispose();
                _request = null;
            }
            if (_proc != null)
            {
                try { if (!_proc.HasExited) _proc.Kill(); } catch { /* already gone */ }
                _proc.Dispose();
                _proc = null;
            }
            _stdout = _stderr = null;
            _stdin = null;
        }

        static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "...";

        /// <summary>Splits a reply into (notes, plan JSON) using the ```json fence, falling back to the outermost braces.</summary>
        public static bool ExtractPlan(string reply, out string notes, out string planJson)
        {
            notes = reply?.Trim() ?? "";
            planJson = null;
            if (string.IsNullOrEmpty(reply)) return false;
            int fence = reply.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (fence >= 0)
            {
                int start = reply.IndexOf('\n', fence);
                int end = start >= 0 ? reply.IndexOf("```", start, StringComparison.Ordinal) : -1;
                if (start >= 0 && end > start)
                {
                    planJson = reply.Substring(start + 1, end - start - 1).Trim();
                    notes = reply.Substring(0, fence).Trim();
                }
            }
            if (planJson == null)
            {
                int a = reply.IndexOf('{'), b = reply.LastIndexOf('}');
                if (a < 0 || b <= a) return false;
                planJson = reply.Substring(a, b - a + 1);
                notes = reply.Substring(0, a).Trim();
            }
            try { planJson = JToken.Parse(planJson).ToString(Formatting.Indented); return true; }
            catch { return false; }
        }
    }
}
