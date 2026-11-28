using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Runs the official Claude Code CLI headless (<c>claude -p</c>) so the director uses the user's own
    /// Claude login/subscription instead of an API key. Output is constrained with <c>--json-schema</c> and read from
    /// the <c>structured_output</c> field of <c>--output-format json</c>.
    /// The CLI runs with no tools, no MCP servers, user settings only, and an isolated working directory.
    /// </summary>
    public static class ClaudeCodeCli
    {
        private const string Log = "[FilmStudio]";
        private const int MaxCommandLineChars = 30000;
        private static readonly string WorkDir = Path.GetFullPath("Library/FilmStudio/cli");

        public class Result
        {
            public bool ok;
            public string error;
            public JToken structuredOutput;
            public string stdout;
            public int inputTokens;
            public int outputTokens;
        }

        /// <summary>Returns the claude executable path, or null when it can't be found.</summary>
        public static string ResolveExecutable()
        {
            string configured = FilmStudioSettings.instance.cliPath;
            if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string[] names = windows ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (string n in names)
                {
                    try
                    {
                        string p = Path.Combine(dir.Trim('"'), n);
                        if (File.Exists(p)) return p;
                    }
                    catch (ArgumentException) { }
                }
            }

            string[] fallbacks = windows
                ? new[]
                {
                    Path.Combine(home, ".local", "bin", "claude.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
                }
                : new[]
                {
                    Path.Combine(home, ".local", "bin", "claude"),
                    Path.Combine(home, ".claude", "local", "claude"),
                    "/opt/homebrew/bin/claude",
                    "/usr/local/bin/claude",
                };
            foreach (string p in fallbacks)
                if (File.Exists(p)) return p;
            return null;
        }

        /// <summary>Runs <c>claude --version</c>; returns the version line or an error message.</summary>
        public static async UniTask<string> GetVersionAsync(CancellationToken ct)
        {
            string exe = ResolveExecutable();
            if (exe == null) return "not found";
            var (code, stdout, stderr) = await RunProcessAsync(exe, "--version", null, 30, ct);
            return code == 0 ? stdout.Trim() : $"error ({code}): {stderr.Trim()}";
        }

        public static async UniTask<Result> RunStructuredAsync(string systemPrompt, string stdinText, JObject schema, CancellationToken ct)
        {
            FilmStudioSettings st = FilmStudioSettings.instance;
            string exe = ResolveExecutable();
            if (exe == null) return new Result { error = "Claude Code CLI not found." };

            Directory.CreateDirectory(WorkDir);
            string systemFile = Path.Combine(WorkDir, "director_system_prompt.txt");
            File.WriteAllText(systemFile, systemPrompt ?? "", new UTF8Encoding(false));

            var args = new StringBuilder();
            AppendArg(args, "-p");
            AppendArg(args, "--output-format"); AppendArg(args, "json");
            AppendArg(args, "--json-schema"); AppendArg(args, schema.ToString(Formatting.None));
            AppendArg(args, "--system-prompt-file"); AppendArg(args, systemFile);
            AppendArg(args, "--tools"); AppendArg(args, "");
            AppendArg(args, "--strict-mcp-config");
            AppendArg(args, "--setting-sources"); AppendArg(args, "user");
            AppendArg(args, "--no-session-persistence");
            if (!string.IsNullOrWhiteSpace(st.cliModel)) { AppendArg(args, "--model"); AppendArg(args, st.cliModel.Trim()); }

            if (args.Length > MaxCommandLineChars)
                return new Result { error = $"CLI command line too long ({args.Length} chars). Lower Max shots in settings." };

            var sw = Stopwatch.StartNew();
            var (exitCode, stdout, stderr) = await RunProcessAsync(exe, args.ToString(), stdinText, Math.Max(30, st.cliTimeoutSec), ct);
            sw.Stop();

            var result = new Result { stdout = stdout };
            JObject root = null;
            try { if (!string.IsNullOrWhiteSpace(stdout)) root = JObject.Parse(stdout.Trim()); }
            catch (JsonException) { }

            if (root == null)
            {
                string msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                if (msg.Contains("login", StringComparison.OrdinalIgnoreCase) || msg.Contains("auth", StringComparison.OrdinalIgnoreCase))
                    msg += "\nRun `claude` once in a terminal and log in with your Claude account.";
                result.error = $"Claude Code CLI failed (exit {exitCode}): {Truncate(msg, 800)}";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            result.inputTokens = (root["usage"]?["input_tokens"]?.Value<int?>() ?? 0)
                               + (root["usage"]?["cache_creation_input_tokens"]?.Value<int?>() ?? 0)
                               + (root["usage"]?["cache_read_input_tokens"]?.Value<int?>() ?? 0);
            result.outputTokens = root["usage"]?["output_tokens"]?.Value<int?>() ?? 0;

            if (root["is_error"]?.Value<bool?>() == true || root["subtype"]?.ToString() != "success")
            {
                result.error = $"Claude Code CLI error ({root["subtype"]}): {Truncate(root["result"]?.ToString() ?? stderr, 800)}";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            result.structuredOutput = root["structured_output"];
            if (result.structuredOutput == null || result.structuredOutput.Type == JTokenType.Null)
            {
                // Older CLIs: fall back to parsing the text result as JSON.
                string text = root["result"]?.ToString();
                try { result.structuredOutput = string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text); }
                catch (JsonException) { result.structuredOutput = null; }
            }
            if (result.structuredOutput == null)
            {
                result.error = "Claude Code CLI returned no structured output. Update the CLI (`claude update`).";
                Debug.LogError($"{Log} {result.error}");
                return result;
            }

            result.ok = true;
            Debug.Log($"{Log} Claude Code CLI ({st.cliModel}) answered in {sw.Elapsed.TotalSeconds:0.0}s " +
                      $"(tokens in {result.inputTokens} / out {result.outputTokens}).");
            return result;
        }

        // ── Process plumbing ──────────────────────────────────────────────────────

        private static async UniTask<(int code, string stdout, string stderr)> RunProcessAsync(
            string exe, string arguments, string stdinText, int timeoutSec, CancellationToken ct)
        {
            bool isCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = isCmd ? "cmd.exe" : exe,
                Arguments = isCmd ? $"/d /s /c \"{Quote(exe)} {arguments}\"" : arguments,
                WorkingDirectory = Directory.Exists(WorkDir) ? WorkDir : Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (FilmStudioSettings.instance.cliForceSubscription)
            {
                psi.EnvironmentVariables.Remove("ANTHROPIC_API_KEY");
                psi.EnvironmentVariables.Remove("ANTHROPIC_AUTH_TOKEN");
            }

            // Run blocking process I/O off the main thread; hop back for Unity APIs afterwards.
            var work = Task.Run(() =>
            {
                using var p = new Process { StartInfo = psi };
                p.Start();
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();
                using (var stdin = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)))
                {
                    if (!string.IsNullOrEmpty(stdinText)) stdin.Write(stdinText);
                }

                var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
                while (!p.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
                    {
                        try { p.Kill(); } catch (InvalidOperationException) { }
                        p.WaitForExit(2000);
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                        return (-1, outTask.Result, $"Timed out after {timeoutSec}s. " + errTask.Result);
                    }
                }
                p.WaitForExit();
                return (p.ExitCode, outTask.Result, errTask.Result);
            });

            var result = await work.AsUniTask();
            await UniTask.SwitchToMainThread();
            return result;
        }

        private static void AppendArg(StringBuilder sb, string arg)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Quote(arg));
        }

        /// <summary>Quotes one argument using the MSVCRT/CommandLineToArgvW rules (also understood by Mono on macOS).</summary>
        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }
                if (i == arg.Length) { sb.Append('\\', backslashes * 2); break; }
                if (arg[i] == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(arg[i]); }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s ?? "" : s.Substring(0, n) + "…";
    }
}
