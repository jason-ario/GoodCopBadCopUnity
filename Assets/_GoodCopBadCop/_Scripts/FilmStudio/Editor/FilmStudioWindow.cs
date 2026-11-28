using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Tools/Film Studio — brief → Claude screenplay → Timeline build → preview → MP4.
    /// Claude acts as an iterative director: leave per-shot notes, lock shots you like, revise.
    /// </summary>
    public class FilmStudioWindow : EditorWindow
    {
        private enum Tab { Brief, ShotList, Preview, Record, History, Settings }

        private static readonly string[] TabNames = { "Brief", "Shot List", "Preview", "Record", "History", "Settings" };
        private static readonly string[] Tones = { "Horror reveal", "Deadpan comedy", "Mockumentary", "POV", "Trend format", "Thriller", "Wholesome-then-wrong" };
        private static readonly float[] Lengths = { 15f, 30f, 45f, 60f };

        // ── Serialized window state (survives domain reload) ──────────────────────
        [SerializeField] private Tab _tab;
        [SerializeField] private Brief _brief = new();
        [SerializeField] private string _screenplayJson;
        [SerializeField] private string _slug = "";
        [SerializeField] private int _revision;
        [SerializeField] private string _globalNote = "";
        [SerializeField] private string _status = "";
        [SerializeField] private string _lastReport = "";
        [SerializeField] private bool _autoBuild = true;
        [SerializeField] private string _castFilter = "";

        // ── Runtime ───────────────────────────────────────────────────────────────
        private Screenplay _screenplay;
        private StudioCatalog _catalog;
        private CancellationTokenSource _cts;
        private bool _busy;
        private Vector2 _scroll;
        private float _previewTime;
        private bool _previewPlaying;
        private double _previewStartRealtime;
        private float _previewStartTime;
        private string _apiKeyField;
        private string _cliVersion;

        /// <summary>The open Film Studio window (used by the Bezi director bridge).</summary>
        internal static FilmStudioWindow ActiveWindow { get; private set; }
        internal Screenplay CurrentScreenplay => _screenplay;
        internal Brief CurrentBrief => _brief;
        internal string CurrentSlug => _slug;

        [MenuItem("Tools/Film Studio/Open Film Studio", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<FilmStudioWindow>("Film Studio");
            w.minSize = new Vector2(480, 560);
            w.ApplyTitle();
        }

        private void ApplyTitle()
        {
            Texture icon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_Animation.Record" : "Animation.Record").image;
            titleContent = new GUIContent("Film Studio", icon, "Good Cop Bad Cop — Film Studio");
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_screenplayJson))
            {
                try { _screenplay = JsonConvert.DeserializeObject<Screenplay>(_screenplayJson); }
                catch (Exception) { _screenplay = null; }
            }
            _catalog = AssetDatabase.LoadAssetAtPath<StudioCatalog>(FilmStudioSettings.instance.catalogPath);
            if (_catalog != null)
            {
                // Deferred so it never runs mid domain-reload; a cheap GUID fingerprint makes it a no-op when nothing changed.
                EditorApplication.delayCall += () =>
                {
                    if (this == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    int before = _catalog != null ? _catalog.actors.Count : 0;
                    _catalog = StudioCatalogBuilder.LoadOrCreate();
                    if (_catalog.actors.Count != before) _status = $"Cast synced: {_catalog.actors.Count} actors.";
                    Repaint();
                };
            }
            EditorApplication.update += OnEditorUpdate;
            FilmRecorder.RecordingFinished += OnRecordingFinished;
            ActiveWindow = this;
            BeziDirectorBridge.WindowReceiver = ReceiveBeziScreenplay;
            AdoptUnclaimedBeziResponse();
            ApplyTitle();
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            FilmRecorder.RecordingFinished -= OnRecordingFinished;
            if (ActiveWindow == this) { ActiveWindow = null; BeziDirectorBridge.WindowReceiver = null; }
            _cts?.Cancel();
        }

        // ── GUI ───────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            FilmStudioStyles.Ensure();
            if (Event.current.type == EventType.MouseMove) Repaint();

            FilmStudioStyles.DrawHeader(DirectorLabel, FilmRecorder.IsRecording);
            _tab = (Tab)FilmStudioStyles.DrawTabs((int)_tab, TabNames);
            DrawModeBar();
            DrawStatusBar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(2);
            using (new EditorGUI.DisabledScope(_busy && _tab != Tab.Settings))
            {
                switch (_tab)
                {
                    case Tab.Brief: DrawBrief(); break;
                    case Tab.ShotList: DrawShotList(); break;
                    case Tab.Preview: DrawPreview(); break;
                    case Tab.Record: DrawRecord(); break;
                    case Tab.History: DrawHistory(); break;
                    case Tab.Settings: DrawSettings(); break;
                }
            }
            GUILayout.Space(6);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Film Mode / Play Game switch. Film Mode = Main.unity + film overlay, Play runs the film with gameplay
        /// suppressed (what recordings use). Play Game = overlay unloaded, Play runs the normal game.
        /// </summary>
        private void DrawModeBar()
        {
            bool film = StudioSceneUtility.IsFilmMode;
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUILayout.HorizontalScope(FilmStudioStyles.Strip))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(film ? "FILM MODE" : "PLAY GAME MODE", FilmStudioStyles.CardTitle);
                    GUILayout.Label(film
                            ? (playing ? "Playing the film: gameplay is switched off, Main.unity is untouched."
                                       : "Main.unity + film overlay. Play / Record runs the film with gameplay switched off.")
                            : "Film overlay unloaded. Play runs the normal game.",
                        FilmStudioStyles.Meta);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(playing || FilmRecorder.IsRecording || _busy))
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(210)))
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool toFilm = GUILayout.Toggle(film, "\u25CF Film Mode", film ? FilmStudioStyles.Primary : EditorStyles.miniButtonLeft, GUILayout.Height(24));
                        bool toGame = GUILayout.Toggle(!film, "\u25B6 Play Game", EditorStyles.miniButtonRight, GUILayout.Height(24));
                        if (toFilm && !film)
                        {
                            _status = StudioSceneUtility.EnterFilmMode(out _) ? "Film Mode on: Main.unity + film overlay loaded." : "Film Mode cancelled.";
                            GUIUtility.ExitGUI();
                        }
                        else if (toGame && film)
                        {
                            _status = StudioSceneUtility.ExitFilmMode() ? "Play Game mode: film overlay unloaded." : "Could not exit Film Mode.";
                            GUIUtility.ExitGUI();
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(FilmStudioStyles.Strip))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    if (_screenplay != null)
                    {
                        GUILayout.Label(_screenplay.title, FilmStudioStyles.FilmTitle);
                        GUILayout.Label($"{_slug}  \u00B7  v{_revision}  \u00B7  {_screenplay.shots.Count} shots  \u00B7  {_screenplay.durationSec:0.0}s", FilmStudioStyles.Meta);
                    }
                    else
                    {
                        GUILayout.Label("No screenplay yet", FilmStudioStyles.FilmTitle);
                        GUILayout.Label("Write a brief and generate, or load a revision from History.", FilmStudioStyles.Meta);
                    }
                }
                GUILayout.FlexibleSpace();
                if (_busy)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(140)))
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            GUILayout.Label("WORKING\u2026", FilmStudioStyles.Pill);
                            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _cts?.Cancel();
                        }
                        GUILayout.FlexibleSpace();
                    }
                }
            }
            if (!string.IsNullOrEmpty(_status)) FilmStudioStyles.DrawStatus(_status);

            BeziDirectorBridge.PendingRequest pending = FilmStudioSettings.instance.backend == DirectorBackend.Bezi ? BeziDirectorBridge.GetPending() : null;
            if (pending != null)
            {
                using (FilmStudioStyles.BeginCard("Waiting for Bezi", $"request {pending.requestId} ({pending.kind})", FilmStudioStyles.PoliceBlue))
                {
                    EditorGUILayout.LabelField($"In Bezi chat, say: \"{BeziDirectorBridge.ChatPrompt}\"", EditorStyles.wordWrappedLabel);
                    GUILayout.Space(2);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Copy prompt", FilmStudioStyles.Primary, GUILayout.Height(22))) EditorGUIUtility.systemCopyBuffer = BeziDirectorBridge.ChatPrompt;
                        if (GUILayout.Button("Cancel request", GUILayout.Width(110), GUILayout.Height(22))) { BeziDirectorBridge.ClearPending(); _status = "Bezi request cancelled."; }
                    }
                }
            }
        }

        private static string DirectorLabel => FilmStudioSettings.instance.backend switch
        {
            DirectorBackend.Bezi => "Bezi",
            DirectorBackend.ClaudeCodeCli => "Claude (CLI)",
            _ => "Claude (API)",
        };

        private void DrawBrief()
        {
            if (_catalog == null || _catalog.actors.Count == 0)
            {
                using (FilmStudioStyles.BeginCard("Catalog"))
                {
                    EditorGUILayout.HelpBox("No catalog yet. Rebuild it to scan actors, clips, sets and SFX.", MessageType.Warning);
                    if (GUILayout.Button("Rebuild Catalog", FilmStudioStyles.Primary, GUILayout.Height(28))) RebuildCatalog();
                }
                return;
            }
            if (_catalog.sets.Count == 0)
            {
                using (FilmStudioStyles.BeginCard("Sets"))
                {
                    EditorGUILayout.HelpBox("No sets yet. Use Tools/Film Studio/Import Set… to register a Main.unity environment as a set.", MessageType.Warning);
                    if (GUILayout.Button("Import Set…", GUILayout.Height(24))) StudioSetImportWindow.Open();
                }
            }

            using (FilmStudioStyles.BeginCard("Premise", "what happens in the short"))
            {
                _brief.premise = EditorGUILayout.TextArea(_brief.premise, GUILayout.MinHeight(70));
                _brief.hookIdea = EditorGUILayout.TextField("Hook idea", _brief.hookIdea);
            }

            using (FilmStudioStyles.BeginCard("Setup", "set · tone · length"))
            {
                string[] setIds = _catalog.sets.Select(s => s.setId).ToArray();
                int setIdx = Mathf.Max(0, Array.IndexOf(setIds, _brief.setId));
                if (setIds.Length > 0) _brief.setId = setIds[EditorGUILayout.Popup("Set", setIdx, setIds)];

                int toneIdx = Mathf.Max(0, Array.IndexOf(Tones, _brief.tone));
                _brief.tone = Tones[EditorGUILayout.Popup("Tone", toneIdx, Tones)];
                int lenIdx = Mathf.Max(0, Array.IndexOf(Lengths, _brief.targetLengthSec));
                _brief.targetLengthSec = Lengths[EditorGUILayout.Popup("Length", lenIdx, Lengths.Select(l => $"{l:0}s").ToArray())];
            }

            using (FilmStudioStyles.BeginCard("Cast", $"{_brief.castActorIds.Count} selected of {_catalog.actors.Count} — empty lets Claude choose"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _castFilter = EditorGUILayout.TextField("Filter", _castFilter);
                    if (GUILayout.Button(new GUIContent("Sync cast", "Add newly created characters (SuspectData prefabs and character prefabs " +
                                                                     "in the cast search folders) and drop deleted ones. Existing actors are untouched."),
                                         GUILayout.Width(80)))
                    {
                        StudioCatalogBuilder.SyncCast(_catalog, out _status);
                        _brief.castActorIds.RemoveAll(id => _catalog.FindActor(id) == null);
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.Space(2);
                int row = 0;
                foreach (StudioCatalog.ActorEntry a in _catalog.actors)
                {
                    if (!string.IsNullOrEmpty(_castFilter) &&
                        a.actorId.IndexOf(_castFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (a.displayName ?? "").IndexOf(_castFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool on = _brief.castActorIds.Contains(a.actorId);
                    string label = $"{a.displayName} ({a.actorId}) — {a.clips.Count} clips{(a.hasMutatedVariant ? ", MUTANT" : "")}";
                    Rect rowRect = EditorGUILayout.GetControlRect();
                    Rect bg = new(rowRect.x - 4, rowRect.y - 1, rowRect.width + 8, rowRect.height + 2);
                    if (on) FilmStudioStyles.DrawRowBackground(bg, FilmStudioStyles.RowHighlight);
                    else if (row % 2 == 1) FilmStudioStyles.DrawRowBackground(bg, FilmStudioStyles.RowStripe);
                    row++;
                    bool now = EditorGUI.ToggleLeft(rowRect, label, on);
                    if (now && !on) _brief.castActorIds.Add(a.actorId);
                    else if (!now && on) _brief.castActorIds.Remove(a.actorId);
                }
            }

            using (FilmStudioStyles.BeginCard("Direct"))
            {
                _autoBuild = EditorGUILayout.ToggleLeft("Build Timeline automatically after the director responds", _autoBuild);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Generate with {DirectorLabel}", FilmStudioStyles.Primary, GUILayout.Height(32))) GenerateAsync().Forget();
                    if (GUILayout.Button("Sample (no Claude)", GUILayout.Height(32), GUILayout.Width(140))) BuildSample();
                }
            }
        }

        private void DrawShotList()
        {
            if (_screenplay == null)
            {
                using (FilmStudioStyles.BeginCard("Shot List")) EditorGUILayout.HelpBox("Generate a screenplay first.", MessageType.Info);
                return;
            }

            using (FilmStudioStyles.BeginCard())
            {
                EditorGUILayout.LabelField(_screenplay.title, FilmStudioStyles.BigTitle);
                EditorGUILayout.LabelField(_screenplay.logline, EditorStyles.wordWrappedLabel);
                GUILayout.Space(3);
                EditorGUILayout.LabelField($"Hook: {_screenplay.hook}", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField($"Set: {_screenplay.setId}   Cast: {string.Join(", ", _screenplay.cast.Select(c => $"{c.actorId} as {c.role}{(c.variant == "mutated" ? " [M]" : "")}"))}", EditorStyles.wordWrappedMiniLabel);
            }

            foreach (Shot shot in _screenplay.shots)
            {
                using (FilmStudioStyles.BeginCard(stripe: shot.locked ? FilmStudioStyles.Accent : null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(shot.id, FilmStudioStyles.Pill);
                        GUILayout.Label($"{shot.start:0.0}s +{shot.duration:0.0}s", EditorStyles.boldLabel, GUILayout.Width(84));
                        ShotCamera c = shot.camera;
                        GUILayout.Label($"{c.framing} · {c.move} · {c.subjectActorId}{(string.IsNullOrEmpty(c.secondaryActorId) ? "" : " / " + c.secondaryActorId)}{(c.shake > 0 ? " · handheld" : "")}{(Math.Abs(shot.speed - 1f) > 0.01f ? $" · x{shot.speed:0.##}" : "")}",
                            FilmStudioStyles.Meta, GUILayout.MinWidth(40));
                        GUILayout.FlexibleSpace();
                        shot.locked = GUILayout.Toggle(shot.locked, "Lock", "Button", GUILayout.Width(50));
                        if (GUILayout.Button("Jump", GUILayout.Width(50))) JumpTo(shot.start + 0.01f);
                    }
                    GUILayout.Space(2);
                    foreach (ActorAction a in shot.actions)
                    {
                        string what = a.type switch
                        {
                            "anim" => a.clipId,
                            "move" => "→ " + a.toMarkId,
                            "face" => "faces " + a.faceTargetId,
                            _ => "",
                        };
                        EditorGUILayout.LabelField($"   {a.t:0.0}s  {a.actorId}  {a.type}  {what}", EditorStyles.miniLabel);
                    }
                    foreach (SfxCue s in shot.sfx)
                        EditorGUILayout.LabelField($"   {s.t:0.0}s  SFX {s.sfxId}", EditorStyles.miniLabel);
                    if (!string.IsNullOrWhiteSpace(shot.caption))
                        EditorGUILayout.LabelField($"   \"{shot.caption}\"", FilmStudioStyles.Caption);
                    shot.notes = EditorGUILayout.TextField("Note", shot.notes ?? "");
                }
            }

            using (FilmStudioStyles.BeginCard("Global note", "applies to the whole film"))
            {
                _globalNote = EditorGUILayout.TextArea(_globalNote, GUILayout.MinHeight(40));
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Revise with {DirectorLabel}", FilmStudioStyles.Primary, GUILayout.Height(30))) ReviseAsync().Forget();
                    if (GUILayout.Button("Rebuild Timeline", GUILayout.Height(30), GUILayout.Width(140))) BuildCurrent();
                }
            }
            if (GUI.changed) PersistScreenplay();

            if (!string.IsNullOrEmpty(_lastReport))
            {
                using (FilmStudioStyles.BeginCard("Validation"))
                    EditorGUILayout.HelpBox(_lastReport, MessageType.None);
            }
        }

        private void DrawPreview()
        {
            FilmInstance film = CurrentFilm();
            if (film == null)
            {
                using (FilmStudioStyles.BeginCard("Preview"))
                {
                    EditorGUILayout.HelpBox("No built film for this screenplay in the studio scene.", MessageType.Info);
                    if (_screenplay != null && GUILayout.Button("Build Timeline", FilmStudioStyles.Primary, GUILayout.Height(28))) BuildCurrent();
                }
                return;
            }

            using (FilmStudioStyles.BeginCard("Transport"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{film.timelinePath}", FilmStudioStyles.Meta, GUILayout.MinWidth(40));
                    GUILayout.Label($"{_previewTime:0.00}s / {film.duration:0.00}s", FilmStudioStyles.Timecode, GUILayout.Width(130));
                }
                EditorGUI.BeginChangeCheck();
                float t = EditorGUILayout.Slider("Time", _previewTime, 0f, Mathf.Max(0.1f, film.duration));
                if (EditorGUI.EndChangeCheck()) { _previewPlaying = false; JumpTo(t); }
                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("|<", GUILayout.Width(40), GUILayout.Height(28))) { _previewPlaying = false; JumpTo(0f); }
                    if (GUILayout.Button(_previewPlaying ? "Pause" : "Play (edit mode)", FilmStudioStyles.Primary, GUILayout.Height(28)))
                    {
                        _previewPlaying = !_previewPlaying;
                        _previewStartRealtime = EditorApplication.timeSinceStartup;
                        _previewStartTime = _previewTime >= film.duration - 0.05f ? 0f : _previewTime;
                    }
                    if (GUILayout.Button("Open in Timeline", GUILayout.Height(28), GUILayout.Width(130)))
                    {
                        Selection.activeGameObject = film.root;
                        EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
                    }
                }
            }
            using (FilmStudioStyles.BeginCard("Notes"))
            {
                EditorGUILayout.HelpBox("Set the Game view to a 9:16 resolution (e.g. 1080x1920) to see final framing. Edit-mode preview skips speed ramps; Record plays the real thing.", MessageType.None);
                if (film.warnings.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", film.warnings), MessageType.Warning);
            }
        }

        private void DrawRecord()
        {
            FilmStudioSettings st = FilmStudioSettings.instance;
            FilmInstance film = CurrentFilm();
            using (FilmStudioStyles.BeginCard("Output"))
            {
                EditorGUILayout.LabelField("Output", $"{st.outputWidth}x{st.outputHeight} @ {st.fps} fps, H.264 MP4 + SFX audio");
                EditorGUILayout.LabelField("Folder", Path.GetFullPath(st.recordingsFolder));
            }
            if (film == null)
            {
                using (FilmStudioStyles.BeginCard("Record")) EditorGUILayout.HelpBox("Build the film first.", MessageType.Info);
                return;
            }

            using (FilmStudioStyles.BeginCard("Record", FilmRecorder.IsRecording ? "recording…" : null, FilmRecorder.IsRecording ? FilmStudioStyles.PoliceRed : null))
            {
                EditorGUILayout.LabelField("File", $"{film.OutputBaseName}.mp4 (+ .srt captions)");
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || FilmRecorder.IsRecording))
                {
                    if (GUILayout.Button("\u25CF  Record MP4", FilmStudioStyles.Record, GUILayout.Height(36)))
                    {
                        _previewPlaying = false;
                        _status = "Recording… (enters Play Mode, exits automatically)";
                        FilmRecorder.Record(film.DirectorPath, film.duration, film.OutputBaseName);
                    }
                }
                if (FilmRecorder.IsRecording && GUILayout.Button("Stop recording", GUILayout.Height(24))) FilmRecorder.Cancel();
            }
            if (!string.IsNullOrEmpty(FilmRecorder.LastOutputPath))
            {
                using (FilmStudioStyles.BeginCard("Last recording"))
                {
                    EditorGUILayout.LabelField("Last", FilmRecorder.LastOutputPath, EditorStyles.miniLabel);
                    if (File.Exists(FilmRecorder.LastOutputPath) && GUILayout.Button("Reveal last recording"))
                        EditorUtility.RevealInFinder(FilmRecorder.LastOutputPath);
                }
            }
        }

        private void DrawHistory()
        {
            string filmsFolder = FilmStudioSettings.instance.filmsFolder.TrimEnd('/');
            if (!Directory.Exists(filmsFolder))
            {
                using (FilmStudioStyles.BeginCard("History")) EditorGUILayout.HelpBox("No films yet.", MessageType.Info);
                return;
            }

            foreach (string dir in Directory.GetDirectories(filmsFolder).OrderBy(d => d))
            {
                string slug = Path.GetFileName(dir);
                bool current = slug == _slug;
                using (FilmStudioStyles.BeginCard(slug, current ? "current film" : null, current ? FilmStudioStyles.Accent : null))
                {
                    int row = 0;
                    foreach (string json in Directory.GetFiles(dir, "*.json").OrderBy(RevisionOf))
                    {
                        Rect rowRect = EditorGUILayout.BeginHorizontal();
                        if (row++ % 2 == 1) FilmStudioStyles.DrawRowBackground(new Rect(rowRect.x - 4, rowRect.y, rowRect.width + 8, rowRect.height), FilmStudioStyles.RowStripe);
                        GUILayout.Label($"v{RevisionOf(json)}", EditorStyles.boldLabel, GUILayout.Width(36));
                        GUILayout.Label($"{File.GetLastWriteTime(json):g}", FilmStudioStyles.Meta);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Load", GUILayout.Width(60))) LoadRevision(slug, json, build: false);
                        if (GUILayout.Button("Load + Build", GUILayout.Width(100))) LoadRevision(slug, json, build: true);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        private void DrawSettings()
        {
            FilmStudioSettings st = FilmStudioSettings.instance;
            EditorGUI.BeginChangeCheck();
            using (FilmStudioStyles.BeginCard("Director", DirectorLabel))
            {
                st.backend = (DirectorBackend)EditorGUILayout.EnumPopup(new GUIContent("Backend",
                    "Claude Code CLI: your Claude login/subscription via `claude -p`.\nAnthropic API: API key.\nBezi: Bezi chat writes the screenplay and can preview frames."), st.backend);

                switch (st.backend)
                {
                    case DirectorBackend.ClaudeCodeCli:
                        st.cliPath = EditorGUILayout.TextField(new GUIContent("CLI path", "Empty = auto-detect."), st.cliPath);
                        st.cliModel = EditorGUILayout.TextField(new GUIContent("Model", "Alias (opus, sonnet, haiku) or full id. Empty = CLI default."), st.cliModel);
                        st.cliForceSubscription = EditorGUILayout.Toggle(new GUIContent("Use subscription login",
                            "Strip ANTHROPIC_API_KEY from the CLI environment so usage counts against your Claude plan, not an API key."), st.cliForceSubscription);
                        st.cliTimeoutSec = EditorGUILayout.IntSlider("Timeout (s)", st.cliTimeoutSec, 60, 1800);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Detected", ClaudeCodeCli.ResolveExecutable() ?? "not found", EditorStyles.miniLabel);
                            if (GUILayout.Button("Check", GUILayout.Width(60))) CheckCliAsync().Forget();
                        }
                        if (!string.IsNullOrEmpty(_cliVersion)) EditorGUILayout.LabelField("Version", _cliVersion, EditorStyles.miniLabel);
                        EditorGUILayout.HelpBox("Runs the official Claude Code CLI headless with no tools. Log in once by running `claude` in a terminal.", MessageType.None);
                        break;
                    case DirectorBackend.AnthropicApi:
                        st.model = EditorGUILayout.TextField(new GUIContent("Model id", "A current Claude model id from Anthropic's docs."), st.model);
                        _apiKeyField ??= string.IsNullOrEmpty(FilmStudioSettings.ApiKey) ? "" : "••••••••";
                        string key = EditorGUILayout.PasswordField(new GUIContent("API key", "Stored in EditorPrefs (or ANTHROPIC_API_KEY env var). Never saved to assets."), _apiKeyField);
                        if (key != _apiKeyField) { _apiKeyField = key; if (!key.StartsWith("•")) FilmStudioSettings.ApiKey = key; }
                        st.maxTokens = EditorGUILayout.IntField("Max tokens", st.maxTokens);
                        st.temperature = EditorGUILayout.Slider("Temperature", st.temperature, 0f, 1f);
                        st.usePromptCaching = EditorGUILayout.Toggle("Prompt caching", st.usePromptCaching);
                        break;
                    case DirectorBackend.Bezi:
                        EditorGUILayout.HelpBox("Generate/Revise post a request; then in Bezi chat say:\n\"" + BeziDirectorBridge.ChatPrompt +
                                                "\"\nBezi can also scrub the built film and look at frames before handing it back.", MessageType.None);
                        break;
                }
                GUILayout.Space(2);
                st.maxShots = EditorGUILayout.IntSlider("Max shots", st.maxShots, 3, 40);
                st.maxDurationSec = EditorGUILayout.Slider("Max duration (s)", st.maxDurationSec, 10f, 90f);
            }

            using (FilmStudioStyles.BeginCard("Output"))
            {
                st.outputWidth = EditorGUILayout.IntField("Width", st.outputWidth);
                st.outputHeight = EditorGUILayout.IntField("Height", st.outputHeight);
                st.fps = EditorGUILayout.IntSlider("FPS", st.fps, 24, 60);
                st.recordingsFolder = EditorGUILayout.TextField("Recordings folder", st.recordingsFolder);
                st.filmsFolder = EditorGUILayout.TextField("Films folder", st.filmsFolder);
            }
            if (EditorGUI.EndChangeCheck()) st.SaveSettings();

            using (FilmStudioStyles.BeginCard("Studio", "scene · sets · catalog"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Enter Film Mode", GUILayout.Height(24))) StudioSceneUtility.EnterFilmMode(out _);
                    if (GUILayout.Button("Import Set…", GUILayout.Height(24))) StudioSetImportWindow.Open();
                }
                if (GUILayout.Button("Migrate copied sets to Main overlay", GUILayout.Height(22)))
                    _status = "Migrated: " + StudioSetImporter.MigrateCopiedSets().Replace("\n", " | ");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Rebuild Catalog (slow)", GUILayout.Height(24))) RebuildCatalog();
                    if (GUILayout.Button("Refresh Sets In Catalog", GUILayout.Height(24))) { StudioCatalogBuilder.RefreshSets(); _catalog = StudioCatalogBuilder.LoadOrCreate(); }
                }
                if (GUILayout.Button("Log last director request/response", GUILayout.Height(22)))
                {
                    Debug.Log($"[FilmStudio] Last request:\n{ClaudeDirectorClient.LastRequestJson}");
                    Debug.Log($"[FilmStudio] Last response:\n{ClaudeDirectorClient.LastResponseJson}");
                }
            }
        }

        // ── Director calls ────────────────────────────────────────────────────────

        private async UniTaskVoid GenerateAsync()
        {
            if (!EnsureCatalog()) return;
            if (FilmStudioSettings.instance.backend == DirectorBackend.Bezi)
            {
                BeziDirectorBridge.Post("generate", _brief, null, null, null, null);
                _status = "Request posted to Bezi.";
                return;
            }
            BeginBusy($"{DirectorLabel} is writing the screenplay…");
            try
            {
                DirectorResult r = await ClaudeDirectorClient.GenerateAsync(_brief, _catalog, _cts.Token);
                r = await RepairIfNeeded(r);
                if (!r.ok) { _status = $"Generate failed: {r.error}"; return; }

                _slug = UniqueSlug(StudioCatalogBuilder.Slug(string.IsNullOrWhiteSpace(r.screenplay.title) ? "short" : r.screenplay.title));
                _revision = NextRevision(_slug);
                AcceptScreenplay(r, "Generated");
                _tab = Tab.ShotList;
            }
            catch (OperationCanceledException) { _status = "Cancelled."; }
            finally { EndBusy(); }
        }

        private async UniTaskVoid ReviseAsync()
        {
            if (_screenplay == null || !EnsureCatalog()) return;
            var notes = _screenplay.shots.Where(s => !string.IsNullOrWhiteSpace(s.notes))
                                         .Select(s => new ShotNote { shotId = s.id, note = s.notes.Trim() }).ToList();
            if (notes.Count == 0 && string.IsNullOrWhiteSpace(_globalNote))
            {
                _status = "Add a note to at least one shot (or a global note) before revising.";
                return;
            }

            if (FilmStudioSettings.instance.backend == DirectorBackend.Bezi)
            {
                PersistScreenplay();
                BeziDirectorBridge.Post("revise", _brief, _slug, _screenplay, notes, _globalNote);
                _status = $"Revision request posted to Bezi ({notes.Count} shot notes).";
                return;
            }

            BeginBusy($"{DirectorLabel} is revising ({notes.Count} shot notes)…");
            try
            {
                DirectorResult r = await ClaudeDirectorClient.ReviseAsync(_brief, _screenplay, notes, _globalNote, _catalog, _cts.Token);
                r = await RepairIfNeeded(r);
                if (!r.ok) { _status = $"Revise failed: {r.error}"; return; }
                foreach (Shot s in r.screenplay.shots) s.notes = "";
                _globalNote = "";
                _revision = NextRevision(_slug);
                AcceptScreenplay(r, "Revised");
            }
            catch (OperationCanceledException) { _status = "Cancelled."; }
            finally { EndBusy(); }
        }

        /// <summary>Validates; on hard errors asks Claude for exactly one automatic fix-up round.</summary>
        private async UniTask<DirectorResult> RepairIfNeeded(DirectorResult r)
        {
            if (!r.ok) return r;
            ValidationReport report = ScreenplayValidator.Validate(r.screenplay, _catalog, FilmStudioSettings.instance.maxDurationSec);
            if (!report.HasErrors) { _lastReport = report.ToNotes(); return r; }

            _status = $"Fixing {report.errors.Count} validation errors with Claude…";
            Repaint();
            DirectorResult fixedResult = await ClaudeDirectorClient.ReviseAsync(_brief, r.screenplay, Array.Empty<ShotNote>(),
                "Fix these problems; use only catalog ids.\n" + report.ToNotes(), _catalog, _cts.Token);
            if (!fixedResult.ok) return fixedResult;

            ValidationReport second = ScreenplayValidator.Validate(fixedResult.screenplay, _catalog, FilmStudioSettings.instance.maxDurationSec);
            _lastReport = second.ToNotes();
            if (second.HasErrors)
                Debug.LogWarning($"[FilmStudio] Screenplay still has validation errors after repair; building what is valid.\n{_lastReport}");
            fixedResult.inputTokens += r.inputTokens;
            fixedResult.outputTokens += r.outputTokens;
            return fixedResult;
        }

        private void AcceptScreenplay(DirectorResult r, string verb)
        {
            _screenplay = r.screenplay;
            _screenplay.RecomputeTiming();
            PersistScreenplay();
            SaveRevisionJson();
            string tokens = r.inputTokens + r.outputTokens > 0 ? $" (tokens in {r.inputTokens} / out {r.outputTokens})" : "";
            _status = $"{verb} v{_revision}: {_screenplay.shots.Count} shots, {_screenplay.durationSec:0.0}s{tokens}.";
            if (_autoBuild) BuildCurrent();
        }

        // ── Bezi backend ──────────────────────────────────────────────────────────

        /// <summary>Called synchronously by FilmStudioSubmitScreenplay (already validated, locked shots restored).</summary>
        private string ReceiveBeziScreenplay(DirectorResult r, BeziDirectorBridge.PendingRequest req)
        {
            bool revise = req.kind == "revise" && !string.IsNullOrEmpty(_slug);
            if (!revise)
                _slug = UniqueSlug(StudioCatalogBuilder.Slug(string.IsNullOrWhiteSpace(r.screenplay.title) ? "short" : r.screenplay.title));
            else
            {
                foreach (Shot s in r.screenplay.shots) s.notes = "";
                _globalNote = "";
            }
            _revision = NextRevision(_slug);
            bool wasAuto = _autoBuild;
            _autoBuild = true; // Bezi wants to preview what it submitted
            AcceptScreenplay(r, revise ? "Bezi revised" : "Bezi generated");
            _autoBuild = wasAuto;
            _tab = Tab.ShotList;
            Repaint();

            FilmInstance film = CurrentFilm();
            string filmInfo = film != null ? $"slug '{_slug}' v{_revision}, {film.duration:0.0}s, director '{film.DirectorPath}'\n{string.Join("\n", film.warnings)}" : "build failed (see Console)";
            return $"Accepted. {_status}\n{filmInfo}";
        }

        private void AdoptUnclaimedBeziResponse()
        {
            BeziDirectorBridge.SubmittedResponse resp = BeziDirectorBridge.TakeUnclaimedResponse(null);
            if (resp == null) return;
            try
            {
                _screenplay = JsonConvert.DeserializeObject<Screenplay>(resp.screenplayJson);
                _slug = resp.slug;
                _revision = resp.revision;
                PersistScreenplay();
                _status = $"Adopted Bezi screenplay {_slug} v{_revision} (built while this window was closed).";
                _tab = Tab.ShotList;
            }
            catch (Exception e) { Debug.LogWarning($"[FilmStudio] Could not adopt Bezi response: {e.Message}"); }
        }

        private async UniTaskVoid CheckCliAsync()
        {
            _cliVersion = "checking…";
            Repaint();
            try { _cliVersion = await ClaudeCodeCli.GetVersionAsync(CancellationToken.None); }
            catch (Exception e) { _cliVersion = "error: " + e.Message; }
            Repaint();
        }

        // ── Build / preview ───────────────────────────────────────────────────────

        private void BuildCurrent()
        {
            if (_screenplay == null || !EnsureCatalog()) return;
            if (string.IsNullOrEmpty(_slug)) _slug = UniqueSlug(StudioCatalogBuilder.Slug(_screenplay.title));
            if (_revision <= 0) _revision = NextRevision(_slug);
            ValidationReport report = ScreenplayValidator.Validate(_screenplay, _catalog, FilmStudioSettings.instance.maxDurationSec);
            _lastReport = report.ToNotes();
            FilmInstance film = FilmBuilder.Build(_screenplay, _catalog, _slug, _revision);
            if (film == null) { _status = "Build failed — see Console."; return; }
            PersistScreenplay();
            _previewTime = 0f;
            _status = $"Built {film.OutputBaseName} ({film.warnings.Count} warnings). Preview it or open it in Timeline.";
            _tab = _tab == Tab.Brief ? Tab.ShotList : _tab;
        }

        private void BuildSample()
        {
            if (!EnsureCatalog()) return;
            StudioCatalog.SetEntry set = _catalog.FindSet(_brief.setId) ?? _catalog.sets.FirstOrDefault();
            if (set == null || set.marks.Count < 2) { _status = "Sample needs an imported set with at least 2 marks."; return; }

            var actors = (_brief.castActorIds.Count > 0
                    ? _brief.castActorIds.Select(_catalog.FindActor).Where(a => a != null)
                    : _catalog.actors.Where(a => a.hasMutatedVariant))
                .Take(2).ToList();
            if (actors.Count == 0) actors = _catalog.actors.Take(1).ToList();
            StudioCatalog.ActorEntry lead = actors[0];
            StudioCatalog.ActorEntry other = actors.Count > 1 ? actors[1] : null;
            string Clip(StudioCatalog.ActorEntry a, string tag) =>
                (a.clips.Find(c => c.tags.Contains(tag) && c.variant != "mutated") ?? a.clips.Find(c => c.variant != "mutated"))?.clipId ?? "";

            var s = new Screenplay
            {
                title = "Sample - Checkpoint Smile",
                logline = "A resident smiles a little too long.",
                hook = "Close-up stare",
                setId = set.setId,
            };
            s.cast.Add(new CastEntry { actorId = lead.actorId, role = "Resident", startMarkId = set.marks[0].markId });
            if (other != null) s.cast.Add(new CastEntry { actorId = other.actorId, role = "Bystander", startMarkId = set.marks[1].markId });

            s.shots.Add(new Shot { id = "s01", duration = 1.5f, caption = "he's been standing there for 3 hours",
                camera = new ShotCamera { framing = "close_up", subjectActorId = lead.actorId, move = "dolly_in", moveAmount = 0.6f },
                actions = { new ActorAction { actorId = lead.actorId, t = 0f, type = "anim", clipId = Clip(lead, "idle") } } });
            s.shots.Add(new Shot { id = "s02", duration = 3f,
                camera = new ShotCamera { framing = "wide", subjectActorId = lead.actorId, move = "truck_right", moveAmount = 0.5f, shake = 0.3f },
                actions = { new ActorAction { actorId = lead.actorId, t = 0.2f, type = "move", toMarkId = set.marks[1 % set.marks.Count].markId } } });
            if (other != null)
                s.shots.Add(new Shot { id = "s03", duration = 2f,
                    camera = new ShotCamera { framing = "over_shoulder", subjectActorId = lead.actorId, secondaryActorId = other.actorId, move = "static" },
                    actions = { new ActorAction { actorId = lead.actorId, t = 0f, type = "face", faceTargetId = other.actorId },
                                new ActorAction { actorId = lead.actorId, t = 0.4f, type = "anim", clipId = Clip(lead, "talk") } } });
            var reveal = new Shot { id = "s04", duration = 2f, speed = 0.6f, caption = "wait for it",
                camera = new ShotCamera { framing = "medium", subjectActorId = lead.actorId, move = "crash_zoom", moveAmount = 0.8f, dutch = 8f } };
            if (lead.hasMutatedVariant) reveal.actions.Add(new ActorAction { actorId = lead.actorId, t = 0.6f, type = "variant_swap" });
            if (_catalog.sfx.Count > 0) reveal.sfx.Add(new SfxCue { t = 0.6f, sfxId = (_catalog.sfx.Find(x => x.tags.Contains("sting") || x.tags.Contains("impact")) ?? _catalog.sfx[0]).sfxId });
            s.shots.Add(reveal);
            s.RecomputeTiming();

            _screenplay = s;
            _slug = "sample";
            _revision = NextRevision(_slug);
            PersistScreenplay();
            SaveRevisionJson();
            BuildCurrent();
            _tab = Tab.ShotList;
        }

        private FilmInstance CurrentFilm() => string.IsNullOrEmpty(_slug) ? null : FilmBuilder.Find(_slug);

        private void JumpTo(float t)
        {
            _previewTime = t;
            FilmInstance film = CurrentFilm();
            if (film == null || EditorApplication.isPlaying) return;
            StudioSceneUtility.EvaluatePreview(film.director, t);
        }

        private void OnEditorUpdate()
        {
            if (!_previewPlaying) return;
            FilmInstance film = CurrentFilm();
            if (film == null || EditorApplication.isPlaying) { _previewPlaying = false; return; }
            float t = _previewStartTime + (float)(EditorApplication.timeSinceStartup - _previewStartRealtime);
            if (t >= film.duration) { t = film.duration; _previewPlaying = false; }
            JumpTo(t);
            Repaint();
        }

        private void OnRecordingFinished(string path)
        {
            if (string.IsNullOrEmpty(path)) { _status = "Recording failed — see Console."; Repaint(); return; }
            FilmInstance film = CurrentFilm();
            if (film != null && File.Exists(film.srtPath))
            {
                try { File.Copy(film.srtPath, Path.ChangeExtension(path, ".srt"), true); }
                catch (Exception e) { Debug.LogWarning($"[FilmStudio] Could not copy captions: {e.Message}"); }
            }
            _status = $"Recorded {path}";
            Repaint();
        }

        // ── Persistence helpers ───────────────────────────────────────────────────

        private void PersistScreenplay() => _screenplayJson = _screenplay != null ? JsonConvert.SerializeObject(_screenplay) : null;

        private void SaveRevisionJson()
        {
            if (_screenplay == null || string.IsNullOrEmpty(_slug)) return;
            string folder = $"{FilmStudioSettings.instance.filmsFolder.TrimEnd('/')}/{_slug}";
            FilmBuilder.EnsureFolder(folder);
            string path = $"{folder}/{_slug}_v{_revision}.json";
            File.WriteAllText(path, JsonConvert.SerializeObject(_screenplay, Formatting.Indented));
            AssetDatabase.ImportAsset(path);
        }

        private void LoadRevision(string slug, string jsonPath, bool build)
        {
            try
            {
                _screenplay = JsonConvert.DeserializeObject<Screenplay>(File.ReadAllText(jsonPath));
                _slug = slug;
                _revision = RevisionOf(jsonPath);
                PersistScreenplay();
                _status = $"Loaded {slug} v{_revision}.";
                if (build) BuildCurrent();
                _tab = Tab.ShotList;
            }
            catch (Exception e) { _status = $"Load failed: {e.Message}"; }
        }

        private static int RevisionOf(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            int i = n.LastIndexOf("_v", StringComparison.Ordinal);
            return i >= 0 && int.TryParse(n.Substring(i + 2), out int r) ? r : 0;
        }

        internal static int NextRevision(string slug)
        {
            string folder = $"{FilmStudioSettings.instance.filmsFolder.TrimEnd('/')}/{slug}";
            if (!Directory.Exists(folder)) return 1;
            int max = Directory.GetFiles(folder, "*.json").Select(RevisionOf).DefaultIfEmpty(0).Max();
            return max + 1;
        }

        internal static string UniqueSlug(string slug)
        {
            if (string.IsNullOrEmpty(slug)) slug = "short";
            if (slug.Length > 40) slug = slug.Substring(0, 40).TrimEnd('_');
            string folder = FilmStudioSettings.instance.filmsFolder.TrimEnd('/');
            string candidate = slug;
            for (int i = 2; Directory.Exists($"{folder}/{candidate}"); i++) candidate = $"{slug}_{i}";
            return candidate;
        }

        private bool EnsureCatalog()
        {
            _catalog = _catalog != null ? _catalog : StudioCatalogBuilder.LoadOrCreate();
            if (_catalog.actors.Count > 0) return true;
            _status = "Catalog is empty — rebuild it in Settings.";
            return false;
        }

        private void RebuildCatalog()
        {
            _catalog = StudioCatalogBuilder.Rebuild();
            _status = $"Catalog rebuilt: {_catalog.actors.Count} actors, {_catalog.sets.Count} sets, {_catalog.sfx.Count} SFX.";
        }

        private void BeginBusy(string status)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _busy = true;
            _status = status;
            Repaint();
        }

        private void EndBusy()
        {
            _busy = false;
            Repaint();
        }
    }
}
