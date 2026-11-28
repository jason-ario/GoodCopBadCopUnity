using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.AnimGen
{
    /// <summary>
    /// Editor window for AI-assisted Humanoid animation: prompt -> Claude AnimPlan -> bake .anim -> contact-sheet preview -> refine.
    /// Works without an API key too (edit the plan JSON by hand, or let Bezi write it).
    /// </summary>
    public sealed class AnimGenWindow : EditorWindow
    {
        public enum Mode { Body, Arms }

        const string PromptsFolder = "Assets/_GoodCopBadCop/_Scripts/Editor/AnimGen/Prompts/";
        const string DefaultBodyBase = "Assets/_GoodCopBadCop/_Animations/Character Animations/Breathing Idle.anim";
        const string DefaultArmsBase = "Assets/_GoodCopBadCop/_Umotion Projects/Umotion Animations/Idle_Arms.anim";
        static readonly string[] BodyViews = { "front", "side", "threeQuarter", "back", "top" };

        [SerializeField] Mode _mode = Mode.Body;
        [SerializeField] GameObject _rig;
        [SerializeField] AnimationClip _baseClip;
        [SerializeField] float _baseTime;
        [SerializeField] bool _follow = true;
        [SerializeField] string _clipName = "NewAnimation";
        [SerializeField] string _outputFolder = "Assets/_GoodCopBadCop/_Animations/Generated/Body";
        [SerializeField] string _prompt = "";
        [SerializeField] float _targetDuration;
        [SerializeField] bool _wantLoop;
        [SerializeField] string _feedback = "";
        [SerializeField] string _planJson = "";
        [SerializeField] string _notes = "";
        [SerializeField] string _report = "";
        [SerializeField] string _profileJson = "";
        [SerializeField] string _profileKey = "";
        [SerializeField] string _previewPath = "";
        [SerializeField] int _frameCount = 8;
        [SerializeField] int _tileSize = 256;
        [SerializeField] bool[] _viewOn = { true, true, false, false, false };
        [SerializeField] bool _armsSideView = true;
        [SerializeField] bool _autoBakePreview = true;
        [SerializeField] List<string> _history = new List<string>();
        [SerializeField] bool _foldSettings, _foldProfile, _foldRig = true, _foldPrompt = true, _foldPlan = true, _foldResult = true;

        [NonSerialized] ClaudeClient _client;
        [NonSerialized] Texture2D _previewTex;
        [NonSerialized] string _status = "";
        [NonSerialized] MessageType _statusType = MessageType.None;
        Vector2 _scroll, _planScroll;
        GUIStyle _wrap, _mono;

        [MenuItem("Tools/Good Cop Bad Cop/Animation Generator")]
        public static void Open()
        {
            var w = GetWindow<AnimGenWindow>("Animation Generator");
            w.minSize = new Vector2(460, 520);
        }

        void OnEnable()
        {
            _client = new ClaudeClient();
            ApplyModeDefaults(false);
            LoadPreview();
        }

        void OnDisable()
        {
            if (_client != null && _client.Busy) _client.Cancel();
            if (_previewTex != null) DestroyImmediate(_previewTex);
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            _wrap ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            _mono ??= new GUIStyle(EditorStyles.textArea) { wordWrap = false, font = EditorStyles.standardFont, fontSize = 11 };
            bool busy = _client != null && _client.Busy;

            using (var s = new EditorGUILayout.ScrollViewScope(_scroll)) { _scroll = s.scrollPosition; DrawBody(busy); }

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
        }

        void DrawBody(bool busy)
        {
            EditorGUI.BeginChangeCheck();
            var newMode = (Mode)GUILayout.Toolbar((int)_mode, new[] { "Full Body", "POV Arms" }, GUILayout.Height(24));
            if (EditorGUI.EndChangeCheck() && newMode != _mode) { _mode = newMode; ApplyModeDefaults(true); }

            DrawSettings();
            DrawRig();
            DrawPrompt(busy);
            DrawPlan(busy);
            DrawResult(busy);
        }

        void DrawSettings()
        {
            string header = AnimGenSettings.Ready ? $"Claude Settings  ({AnimGenSettings.ModelLabel})" : "Claude Settings (not ready)";
            _foldSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_foldSettings, header);
            if (_foldSettings)
            {
                EditorGUI.indentLevel++;
                AnimGenSettings.Backend = (ClaudeBackend)EditorGUILayout.Popup(
                    new GUIContent("Backend", "Claude Code CLI uses your logged-in Claude subscription. Anthropic API bills an API key."),
                    (int)AnimGenSettings.Backend, new[] { "Claude Code CLI (subscription)", "Anthropic API (API key)" });

                if (AnimGenSettings.Backend == ClaudeBackend.ClaudeCodeCli)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        AnimGenSettings.CliPath = EditorGUILayout.TextField(new GUIContent("claude Executable", "Full path, or 'claude' to resolve from PATH."), AnimGenSettings.CliPath);
                        if (GUILayout.Button("...", GUILayout.Width(28)))
                        {
                            string p = EditorUtility.OpenFilePanel("Claude Code executable", Path.GetDirectoryName(AnimGenSettings.DefaultCliPath()) ?? "", "");
                            if (!string.IsNullOrEmpty(p)) AnimGenSettings.CliPath = p;
                        }
                    }
                    ModelPopup(AnimGenSettings.CliModels, () => AnimGenSettings.CliModelChoice, v => AnimGenSettings.CliModelChoice = v,
                        () => AnimGenSettings.CliCustomModel, v => AnimGenSettings.CliCustomModel = v, "Alias (fable/opus/sonnet/haiku) or full model name.");
                    int ei = Mathf.Max(0, Array.IndexOf(AnimGenSettings.Efforts, AnimGenSettings.CliEffort));
                    AnimGenSettings.CliEffort = AnimGenSettings.Efforts[EditorGUILayout.Popup("Effort", ei, AnimGenSettings.Efforts)];
                    AnimGenSettings.CliForceSubscription = EditorGUILayout.Toggle(
                        new GUIContent("Force Subscription", "Remove ANTHROPIC_API_KEY / ANTHROPIC_AUTH_TOKEN from the CLI's environment so it uses your logged-in subscription."),
                        AnimGenSettings.CliForceSubscription);
                    EditorGUILayout.HelpBox("Runs `claude -p` with no tools (Read only, to view the preview on refine). Log in once by running `claude` in a terminal.", MessageType.None);
                }
                else
                {
                    AnimGenSettings.ApiKey = EditorGUILayout.PasswordField(new GUIContent("Anthropic API Key", "Stored in EditorPrefs on this machine only."), AnimGenSettings.ApiKey);
                    ModelPopup(AnimGenSettings.ApiModels, () => AnimGenSettings.ApiModelChoice, v => AnimGenSettings.ApiModelChoice = v,
                        () => AnimGenSettings.ApiCustomModel, v => AnimGenSettings.ApiCustomModel = v, "Full API model id.");
                    AnimGenSettings.MaxTokens = EditorGUILayout.IntField("Max Tokens", AnimGenSettings.MaxTokens);
                    AnimGenSettings.ApiVersion = EditorGUILayout.TextField("anthropic-version", AnimGenSettings.ApiVersion);
                }
                AnimGenSettings.TimeoutSeconds = EditorGUILayout.IntField("Timeout (s)", AnimGenSettings.TimeoutSeconds);
                if (!AnimGenSettings.Ready) EditorGUILayout.HelpBox(AnimGenSettings.NotReadyReason, MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    using (new EditorGUI.DisabledScope(_client == null || _client.Busy || !AnimGenSettings.Ready))
                        if (GUILayout.Button("Test Connection")) TestConnection();
                    if (GUILayout.Button("Edit System Prompt")) PingPrompt();
                    if (GUILayout.Button("Open Previews Folder")) EditorUtility.RevealInFinder(PreviewsFolder());
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static void ModelPopup(string[] options, Func<string> getChoice, Action<string> setChoice, Func<string> getCustom, Action<string> setCustom, string customTip)
        {
            int i = Array.IndexOf(options, getChoice());
            if (i < 0) i = 0;
            i = EditorGUILayout.Popup("Model", i, options);
            setChoice(options[i]);
            if (options[i] == AnimGenSettings.CustomLabel)
                setCustom(EditorGUILayout.TextField(new GUIContent("Custom Model", customTip), getCustom()));
        }

        void TestConnection()
        {
            SetStatus($"Testing {AnimGenSettings.ModelLabel}...", MessageType.Info);
            _client.Send("You are a connectivity check. Reply with exactly: OK", "Reply with OK.", null, r =>
            {
                if (r.ok) SetStatus($"Connected: {AnimGenSettings.ModelLabel} replied \"{(r.text ?? "").Trim()}\"{(string.IsNullOrEmpty(r.meta) ? "" : " (" + r.meta + ")")}.", MessageType.Info);
                else SetStatus(r.error, MessageType.Error);
                Repaint();
            });
        }

        void DrawRig()
        {
            _foldRig = EditorGUILayout.BeginFoldoutHeaderGroup(_foldRig, "Rig, Base Pose & Output");
            if (_foldRig)
            {
                EditorGUI.indentLevel++;
                _rig = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Rig", "Humanoid prefab or model asset."), _rig, typeof(GameObject), false);
                _baseClip = (AnimationClip)EditorGUILayout.ObjectField(new GUIContent("Base Clip", "Pose the plan is layered on (an idle or held-item pose). Clips inside FBX files work."), _baseClip, typeof(AnimationClip), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _baseTime = EditorGUILayout.FloatField("Base Time", _baseTime);
                    _follow = EditorGUILayout.ToggleLeft(new GUIContent("Play base along", "Layer the plan over the base clip playing in time (e.g. keep idle breathing)."), _follow, GUILayout.Width(130));
                }
                EditorGUILayout.Space(2);
                _clipName = EditorGUILayout.TextField("Clip Name", _clipName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
                    if (GUILayout.Button("...", GUILayout.Width(28)))
                    {
                        string abs = EditorUtility.OpenFolderPanel("Output folder", Path.GetFullPath(_outputFolder.Length > 0 ? _outputFolder : "Assets"), "");
                        if (!string.IsNullOrEmpty(abs)) _outputFolder = HumanoidRig.ToAssetPath(abs) ?? _outputFolder;
                    }
                }
                EditorGUILayout.LabelField(" ", OutputPath(), EditorStyles.miniLabel);

                _foldProfile = EditorGUILayout.Foldout(_foldProfile, "Rig Profile", true);
                if (_foldProfile)
                {
                    if (GUILayout.Button("Profile Rig")) TryRun(() => EnsureProfile(true), "Profiled rig.");
                    EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_profileJson) ? "(not profiled yet)" : _profileJson, _wrap, GUILayout.MinHeight(60), GUILayout.MaxHeight(160));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawPrompt(bool busy)
        {
            _foldPrompt = EditorGUILayout.BeginFoldoutHeaderGroup(_foldPrompt, "Describe the Animation");
            if (_foldPrompt)
            {
                _prompt = EditorGUILayout.TextArea(_prompt, _wrap, GUILayout.MinHeight(54));
                using (new EditorGUILayout.HorizontalScope())
                {
                    _targetDuration = EditorGUILayout.FloatField(new GUIContent("Duration (0 = auto)"), _targetDuration);
                    _wantLoop = EditorGUILayout.ToggleLeft("Loop", _wantLoop, GUILayout.Width(60));
                }
                _autoBakePreview = EditorGUILayout.ToggleLeft("Bake + preview automatically after Claude replies", _autoBakePreview);

                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(busy || !AnimGenSettings.Ready))
                {
                    if (GUILayout.Button(new GUIContent("Generate with Claude", AnimGenSettings.NotReadyReason ?? ""), GUILayout.Height(26))) Generate();
                }

                EditorGUILayout.LabelField("Feedback for refinement (optional)", EditorStyles.miniBoldLabel);
                _feedback = EditorGUILayout.TextArea(_feedback, _wrap, GUILayout.MinHeight(36));
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(busy || !AnimGenSettings.Ready || string.IsNullOrWhiteSpace(_planJson)))
                        if (GUILayout.Button(new GUIContent("Refine with Claude", "Sends the current plan, bake report, preview image and your feedback."), GUILayout.Height(24))) Refine();
                    if (busy && GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(24))) _client.Cancel();
                }
                if (busy) EditorGUILayout.HelpBox($"Waiting for {AnimGenSettings.ModelLabel}...", MessageType.Info);
                if (!string.IsNullOrWhiteSpace(_notes)) EditorGUILayout.HelpBox(_notes, MessageType.None);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawPlan(bool busy)
        {
            _foldPlan = EditorGUILayout.BeginFoldoutHeaderGroup(_foldPlan, "AnimPlan JSON");
            if (_foldPlan)
            {
                using (var s = new EditorGUILayout.ScrollViewScope(_planScroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(320)))
                {
                    _planScroll = s.scrollPosition;
                    _planJson = EditorGUILayout.TextArea(_planJson, _mono, GUILayout.ExpandHeight(true));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Format")) TryRun(() => _planJson = JToken.Parse(_planJson).ToString(Formatting.Indented), "Formatted.");
                    if (GUILayout.Button("Template")) { PushHistory(); _planJson = Template(); }
                    using (new EditorGUI.DisabledScope(_history.Count == 0))
                        if (GUILayout.Button($"Undo Plan ({_history.Count})")) { _planJson = _history[_history.Count - 1]; _history.RemoveAt(_history.Count - 1); GUI.FocusControl(null); }
                    if (GUILayout.Button("Copy")) EditorGUIUtility.systemCopyBuffer = _planJson;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawResult(bool busy)
        {
            _foldResult = EditorGUILayout.BeginFoldoutHeaderGroup(_foldResult, "Bake & Preview");
            if (_foldResult)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _frameCount = EditorGUILayout.IntSlider("Frames", _frameCount, 1, 12);
                    _tileSize = EditorGUILayout.IntPopup(_tileSize, new[] { "128", "192", "256", "384" }, new[] { 128, 192, 256, 384 }, GUILayout.Width(60));
                }
                if (_mode == Mode.Body)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("Views");
                        for (int i = 0; i < BodyViews.Length; i++) _viewOn[i] = GUILayout.Toggle(_viewOn[i], BodyViews[i], "Button");
                    }
                }
                else _armsSideView = EditorGUILayout.Toggle("Side View Row", _armsSideView);

                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(busy || string.IsNullOrWhiteSpace(_planJson)))
                {
                    if (GUILayout.Button("Bake", GUILayout.Height(26))) TryRun(Bake, null);
                    if (GUILayout.Button("Preview", GUILayout.Height(26))) TryRun(Preview, null);
                    if (GUILayout.Button("Bake + Preview", GUILayout.Height(26))) TryRun(() => { Bake(); Preview(); }, null);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath());
                    using (new EditorGUI.DisabledScope(clip == null))
                        if (GUILayout.Button("Select Clip", GUILayout.Height(26))) { Selection.activeObject = clip; EditorGUIUtility.PingObject(clip); }
                }

                if (!string.IsNullOrEmpty(_report))
                    EditorGUILayout.HelpBox(_report, _report.Contains("WARNING") ? MessageType.Warning : MessageType.Info);

                if (_previewTex != null)
                {
                    float w = EditorGUIUtility.currentViewWidth - 30f;
                    float h = w * _previewTex.height / Mathf.Max(1, _previewTex.width);
                    var r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(true));
                    GUI.DrawTexture(r, _previewTex, ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField(_mode == Mode.Body ? "Columns: time 0 to end. Rows: selected views." : "Columns: time 0 to end. Rows: eye camera, side view.", EditorStyles.centeredGreyMiniLabel);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ------------------------------------------------------------------ actions

        void ApplyModeDefaults(bool force)
        {
            bool body = _mode == Mode.Body;
            if (force || _rig == null)
                _rig = AssetDatabase.LoadAssetAtPath<GameObject>(body ? HumanoidRig.DefaultBodyModelPath : HumanoidRig.DefaultArmsPrefabPath);
            if (force || _baseClip == null)
                _baseClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(body ? DefaultBodyBase : DefaultArmsBase);
            if (force)
            {
                _follow = body;
                _outputFolder = body ? "Assets/_GoodCopBadCop/_Animations/Generated/Body" : "Assets/_GoodCopBadCop/_Animations/Generated/Arms";
                _profileJson = ""; _profileKey = "";
            }
        }

        string RigPath() => _rig != null ? AssetDatabase.GetAssetPath(_rig) : "";

        string BaseClipPath()
        {
            if (_baseClip == null) return "";
            string p = AssetDatabase.GetAssetPath(_baseClip);
            return p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ? p : $"{p} :: {_baseClip.name}";
        }

        string OutputPath()
        {
            string name = string.IsNullOrWhiteSpace(_clipName) ? "NewAnimation" : _clipName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return $"{_outputFolder.TrimEnd('/')}/{name}.anim";
        }

        void EnsureProfile(bool force)
        {
            string key = $"{_mode}|{RigPath()}|{BaseClipPath()}|{_baseTime}";
            if (!force && key == _profileKey && !string.IsNullOrEmpty(_profileJson)) return;
            _profileJson = _mode == Mode.Body
                ? AnimGenBeziActions.AnimGenProfileBodyRig(RigPath(), BaseClipPath(), _baseTime)
                : AnimGenBeziActions.AnimGenProfileArmsRig(RigPath(), BaseClipPath(), _baseTime);
            _profileKey = key;
        }

        string SystemPrompt()
        {
            string file = PromptsFolder + (_mode == Mode.Body ? "AnimGen_Body_SystemPrompt.md" : "AnimGen_Arms_SystemPrompt.md");
            if (!File.Exists(file)) throw new FileNotFoundException("System prompt missing: " + file);
            return File.ReadAllText(file);
        }

        void PingPrompt()
        {
            var a = AssetDatabase.LoadAssetAtPath<TextAsset>(PromptsFolder + (_mode == Mode.Body ? "AnimGen_Body_SystemPrompt.md" : "AnimGen_Arms_SystemPrompt.md"));
            if (a != null) { Selection.activeObject = a; EditorGUIUtility.PingObject(a); }
        }

        string Constraints()
        {
            var sb = new StringBuilder();
            if (_targetDuration > 0f) sb.AppendLine($"- Duration: {_targetDuration:0.##} s");
            sb.AppendLine($"- Loop: {(_wantLoop ? "yes (first and last pose must match)" : "no")}");
            sb.AppendLine($"- Base pose: '{(_baseClip ? _baseClip.name : "bind pose")}' at t={_baseTime:0.##}{(_follow ? ", playing along underneath" : ", frozen")}");
            return sb.ToString();
        }

        void Generate()
        {
            if (string.IsNullOrWhiteSpace(_prompt)) { SetStatus("Describe the animation first.", MessageType.Warning); return; }
            if (!TryRun(() => EnsureProfile(false), null)) return;
            string user =
                $"Animation request:\n{_prompt.Trim()}\n\nConstraints:\n{Constraints()}\nRig profile ({(_mode == Mode.Body ? "character space" : "view space")}):\n{_profileJson}";
            SendToClaude(user, null, "Generated plan.");
        }

        void Refine()
        {
            if (!TryRun(() => EnsureProfile(false), null)) return;
            string png = !string.IsNullOrEmpty(_previewPath) && File.Exists(_previewPath) ? Path.GetFullPath(_previewPath) : null;
            string views = _mode == Mode.Body ? string.Join(", ", SelectedViews()) : (_armsSideView ? "eye camera, side view" : "eye camera");
            string user =
                $"Original request:\n{(_prompt ?? "").Trim()}\n\nConstraints:\n{Constraints()}\n" +
                $"Animator feedback:\n{(string.IsNullOrWhiteSpace(_feedback) ? "(none: critique it yourself and improve timing, spacing, arcs and readability)" : _feedback.Trim())}\n\n" +
                $"Bake report:\n{(string.IsNullOrEmpty(_report) ? "(not baked yet)" : _report)}\n\n" +
                (png != null ? $"The attached image is the preview contact sheet: {_frameCount} columns evenly spaced from t=0 to the end; rows top to bottom: {views}.\n\n" : "No preview image is available.\n\n") +
                $"Current plan:\n```json\n{_planJson}\n```\n\nRig profile:\n{_profileJson}\n\nReturn the complete revised plan.";
            SendToClaude(user, png, "Refined plan.");
        }

        void SendToClaude(string user, string imagePath, string okMessage)
        {
            string system;
            try { system = SystemPrompt(); }
            catch (Exception e) { SetStatus(e.Message, MessageType.Error); return; }

            SetStatus($"Asking {AnimGenSettings.ModelLabel}...", MessageType.Info);
            _client.Send(system, user, imagePath, r =>
            {
                if (!r.ok) { SetStatus(r.error, MessageType.Error); Repaint(); return; }
                if (!string.IsNullOrEmpty(r.meta)) okMessage += $" ({r.meta})";
                if (!ClaudeClient.ExtractPlan(r.text, out var notes, out var plan))
                {
                    _notes = r.text;
                    SetStatus("Claude replied but no valid plan JSON was found (see notes).", MessageType.Error);
                    Repaint();
                    return;
                }
                PushHistory();
                _planJson = plan;
                _notes = notes;
                GUI.FocusControl(null);
                SetStatus(r.error ?? okMessage, r.error != null ? MessageType.Warning : MessageType.Info);
                if (_autoBakePreview) TryRun(() => { Bake(); Preview(); }, null);
                Repaint();
            });
        }

        string PreparedPlanJson()
        {
            var plan = JObject.Parse(_planJson);
            plan["name"] = string.IsNullOrWhiteSpace(_clipName) ? "NewAnimation" : _clipName.Trim();
            if (_baseClip == null) plan.Remove("basePose");
            else plan["basePose"] = new JObject { ["clip"] = BaseClipPath(), ["time"] = _baseTime, ["follow"] = _follow };
            return plan.ToString(Formatting.None);
        }

        void Bake()
        {
            var plan = HumanoidClipWriter.Parse(PreparedPlanJson());
            _report = _mode == Mode.Body
                ? BodyClipBaker.Bake(plan, OutputPath(), RigPath())
                : ArmsClipBaker.Bake(plan, OutputPath(), RigPath());
            SetStatus("Baked " + OutputPath(), MessageType.Info);
        }

        void Preview()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath()) == null) Bake();
            _previewPath = _mode == Mode.Body
                ? AnimPreviewRenderer.RenderBodySheet(OutputPath(), RigPath(), _frameCount, _tileSize, SelectedViews())
                : AnimPreviewRenderer.RenderArmsSheet(OutputPath(), RigPath(), _frameCount, _tileSize, _armsSideView, HumanoidRig.DefaultCameraOffset, 60f);
            LoadPreview();
            SetStatus("Preview rendered.", MessageType.Info);
        }

        string[] SelectedViews()
        {
            var list = new List<string>();
            for (int i = 0; i < BodyViews.Length && list.Count < 4; i++) if (_viewOn[i]) list.Add(BodyViews[i]);
            if (list.Count == 0) list.Add("front");
            return list.ToArray();
        }

        void LoadPreview()
        {
            if (_previewTex != null) DestroyImmediate(_previewTex);
            _previewTex = null;
            if (string.IsNullOrEmpty(_previewPath) || !File.Exists(_previewPath)) return;
            _previewTex = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            _previewTex.LoadImage(File.ReadAllBytes(_previewPath));
        }

        static string PreviewsFolder()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AnimGenPreviews"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        void PushHistory()
        {
            if (string.IsNullOrWhiteSpace(_planJson)) return;
            if (_history.Count > 0 && _history[_history.Count - 1] == _planJson) return;
            _history.Add(_planJson);
            if (_history.Count > 20) _history.RemoveAt(0);
        }

        string Template()
        {
            float d = _targetDuration > 0 ? _targetDuration : 1.5f;
            var tracks = _mode == Mode.Body
                ? new JObject
                {
                    ["hips"] = new JArray(Key(0, true, false), Key(d, true, false)),
                    ["chest"] = new JArray(Key(0, false, true), Key(d, false, true)),
                    ["head"] = new JArray(Key(0, false, true), Key(d, false, true)),
                    ["rightHand"] = new JArray(Key(0, true, true), Key(d, true, true)),
                }
                : new JObject
                {
                    ["rightHand"] = new JArray(Key(0, true, true), Key(d, true, true)),
                    ["leftHand"] = new JArray(Key(0, true, true), Key(d, true, true)),
                };
            var plan = new JObject { ["duration"] = d, ["fps"] = 30, ["loop"] = _wantLoop, ["space"] = "offset", ["tracks"] = tracks };
            if (_mode == Mode.Body) { plan["plantFeet"] = true; plan["handSpace"] = "chest"; }
            return plan.ToString(Formatting.Indented);
        }

        static JObject Key(float t, bool pos, bool rot)
        {
            var k = new JObject { ["t"] = t };
            if (pos) k["pos"] = new JArray(0, 0, 0);
            if (rot) k["rot"] = new JArray(0, 0, 0);
            return k;
        }

        bool TryRun(Action a, string ok)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Animation Generator", "Working...", 0.5f);
                a();
                if (ok != null) SetStatus(ok, MessageType.Info);
                return true;
            }
            catch (Exception e)
            {
                SetStatus(e.Message, MessageType.Error);
                Debug.LogException(e);
                return false;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void SetStatus(string msg, MessageType type) { _status = msg; _statusType = type; }
    }
}
