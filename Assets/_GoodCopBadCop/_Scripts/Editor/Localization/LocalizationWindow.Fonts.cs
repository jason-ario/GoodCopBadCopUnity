using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// Fonts tab: per-language font swap, font library (download + preview + TMP asset creation) and glyph coverage.
public partial class LocalizationWindow
{
    // ---------------------------------------------------------------- catalog

    private class FontCatalogItem
    {
        public string name, role, scripts, fileName, url, licenseUrl, licenseFile;
    }

    private const string FallbackFolder = "Assets/_GoodCopBadCop/_Fonts/Fallback";
    private const string TmpFolder = FallbackFolder + "/TMP";
    private const string NotoCjk = "https://github.com/notofonts/noto-cjk/raw/main/";
    private const string GoogleOfl = "https://github.com/google/fonts/raw/main/ofl/";

    private static FontCatalogItem GoogleFont(string name, string role, string scripts, string dir, string file)
    {
        return new FontCatalogItem
        {
            name = name, role = role, scripts = scripts, fileName = file, url = GoogleOfl + dir + "/" + file,
            licenseUrl = GoogleOfl + dir + "/OFL.txt", licenseFile = "OFL-" + Path.GetFileNameWithoutExtension(file).Split('-')[0] + ".txt"
        };
    }

    private static FontCatalogItem NotoFont(string name, string role, string scripts, string path, string licenseDir)
    {
        return new FontCatalogItem
        {
            name = name, role = role, scripts = scripts, fileName = Path.GetFileName(path), url = NotoCjk + path,
            licenseUrl = NotoCjk + licenseDir + "/LICENSE", licenseFile = "OFL-Noto" + licenseDir + "CJK.txt"
        };
    }

    private static readonly List<FontCatalogItem> Catalog = new List<FontCatalogItem>
    {
        NotoFont("Noto Sans SC", "Sans", "Simplified Chinese", "Sans/SubsetOTF/SC/NotoSansSC-Regular.otf", "Sans"),
        NotoFont("Noto Sans TC", "Sans", "Traditional Chinese", "Sans/SubsetOTF/TC/NotoSansTC-Regular.otf", "Sans"),
        NotoFont("Noto Sans JP", "Sans", "Japanese", "Sans/SubsetOTF/JP/NotoSansJP-Regular.otf", "Sans"),
        NotoFont("Noto Sans KR", "Sans", "Korean", "Sans/SubsetOTF/KR/NotoSansKR-Regular.otf", "Sans"),
        NotoFont("Noto Serif SC", "Serif", "Simplified Chinese", "Serif/SubsetOTF/SC/NotoSerifSC-Regular.otf", "Serif"),
        NotoFont("Noto Serif TC", "Serif", "Traditional Chinese", "Serif/SubsetOTF/TC/NotoSerifTC-Regular.otf", "Serif"),
        NotoFont("Noto Serif JP", "Serif", "Japanese", "Serif/SubsetOTF/JP/NotoSerifJP-Regular.otf", "Serif"),
        NotoFont("Noto Serif KR", "Serif", "Korean", "Serif/SubsetOTF/KR/NotoSerifKR-Regular.otf", "Serif"),
        new FontCatalogItem
        {
            name = "Noto Sans Georgian", role = "Georgian", scripts = "Georgian", fileName = "NotoSansGeorgian-Regular.ttf",
            url = "https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSansGeorgian/hinted/ttf/NotoSansGeorgian-Regular.ttf",
            licenseUrl = "https://github.com/notofonts/georgian/raw/main/OFL.txt", licenseFile = "OFL-NotoSansGeorgian.txt"
        },
        GoogleFont("Ma Shan Zheng", "Handwriting", "Simplified Chinese", "mashanzheng", "MaShanZheng-Regular.ttf"),
        GoogleFont("ZCOOL KuaiLe", "Handwriting", "Simplified Chinese", "zcoolkuaile", "ZCOOLKuaiLe-Regular.ttf"),
        GoogleFont("Zhi Mang Xing", "Handwriting", "Simplified Chinese", "zhimangxing", "ZhiMangXing-Regular.ttf"),
        GoogleFont("Liu Jian Mao Cao", "Handwriting", "Simplified Chinese", "liujianmaocao", "LiuJianMaoCao-Regular.ttf"),
        GoogleFont("Iansui", "Handwriting", "Traditional Chinese", "iansui", "Iansui-Regular.ttf"),
        GoogleFont("Yomogi", "Handwriting", "Japanese", "yomogi", "Yomogi-Regular.ttf"),
        GoogleFont("Klee One", "Handwriting", "Japanese", "kleeone", "KleeOne-Regular.ttf"),
        GoogleFont("Zen Kurenaido", "Handwriting", "Japanese", "zenkurenaido", "ZenKurenaido-Regular.ttf"),
        GoogleFont("Nanum Pen Script", "Handwriting", "Korean", "nanumpenscript", "NanumPenScript-Regular.ttf"),
        GoogleFont("Gaegu", "Handwriting", "Korean", "gaegu", "Gaegu-Regular.ttf"),
        GoogleFont("Gamja Flower", "Handwriting", "Korean", "gamjaflower", "GamjaFlower-Regular.ttf"),
        GoogleFont("DotGothic16", "Pixel", "Japanese", "dotgothic16", "DotGothic16-Regular.ttf"),
    };

    private static readonly string[] RoleFilters = { "All", "Sans", "Serif", "Handwriting", "Pixel", "Georgian" };

    private static readonly string[] SampleNames = { "Latin / Cyrillic", "Chinese (Simplified)", "Chinese (Traditional)", "Japanese", "Korean", "Georgian", "Custom" };
    private static readonly string[] SampleTexts =
    {
        "H\u00E9llo W\u00F6rld \u2014 \u041F\u0440\u0438\u0432\u0435\u0442 \u043C\u0438\u0440 0123456789",
        "\u7B80\u4F53\u4E2D\u6587\uFF1A\u6B22\u8FCE\u6765\u5230\u8FB9\u5883\u68C0\u67E5\u7AD9\uFF0C\u8BF7\u51FA\u793A\u8BC1\u4EF6\u3002",
        "\u7E41\u9AD4\u4E2D\u6587\uFF1A\u6B61\u8FCE\u4F86\u5230\u908A\u5883\u6AA2\u67E5\u7AD9\uFF0C\u8ACB\u51FA\u793A\u8B49\u4EF6\u3002",
        "\u65E5\u672C\u8A9E\uFF1A\u56FD\u5883\u691C\u554F\u6240\u3078\u3088\u3046\u3053\u305D\u3002\u3072\u3089\u304C\u306A \u30AB\u30BF\u30AB\u30CA",
        "\uD55C\uAD6D\uC5B4: \uAD6D\uACBD \uAC80\uBB38\uC18C\uC5D0 \uC624\uC2E0 \uAC83\uC744 \uD658\uC601\uD569\uB2C8\uB2E4.",
        "\u10E5\u10D0\u10E0\u10D7\u10E3\u10DA\u10D8: \u10D9\u10D4\u10D7\u10D8\u10DA\u10D8 \u10D8\u10E7\u10DD\u10E1 \u10D7\u10E5\u10D5\u10D4\u10DC\u10D8 \u10DB\u10DD\u10D1\u10E0\u10EB\u10D0\u10DC\u10D4\u10D1\u10D0.",
        ""
    };

    private class DlJob
    {
        public string label, dest, key;
        public UnityWebRequest req;
        public bool isLicense;
    }

    private readonly List<DlJob> _jobs = new List<DlJob>();
    private int _fontSub;
    private int _sampleIndex;
    private string _customSample = "Type any text here";
    private float _sampleSize = 22f;
    private string _roleFilter = "All";
    private string _customName = "", _customUrl = "", _customLicenseUrl = "";
    private string _fontLang = "en";
    private TMP_FontAsset _previewAsset;
    private Vector2 _fontsScroll;
    private SerializedObject _settingsSO;
    private bool _rawSettings;
    private string _coverageLang = "pseudo-cjk";
    private List<string> _coverageReport = new List<string>();
    private string _newFontLangCode = "";

    private string SampleText { get { return _sampleIndex == SampleTexts.Length - 1 ? _customSample : SampleTexts[_sampleIndex]; } }

    // ---------------------------------------------------------------- downloads

    private void StartDownload(string label, string url, string dest, bool isLicense, string key)
    {
        var req = UnityWebRequest.Get(url);
        req.timeout = 180;
        req.SendWebRequest();
        _jobs.Add(new DlJob { label = label, dest = dest, req = req, isLicense = isLicense, key = key });
    }

    private void PollDownloads()
    {
        if (_jobs.Count == 0) return;
        for (int i = _jobs.Count - 1; i >= 0; i--)
        {
            var job = _jobs[i];
            if (!job.req.isDone) continue;
            _jobs.RemoveAt(i);

            if (job.req.result != UnityWebRequest.Result.Success)
                _status = "Download failed: " + job.label + " (" + job.req.error + ")";
            else
            {
                byte[] data = job.req.downloadHandler.data;
                Directory.CreateDirectory(LocalizationPaths.Abs(FallbackFolder));
                File.WriteAllBytes(LocalizationPaths.Abs(job.dest), data);
                AssetDatabase.ImportAsset(job.dest);
                if (!job.isLicense) _status = "Downloaded " + job.label + " (" + (data.Length / 1024) + " KB) to " + job.dest;
            }
            job.req.Dispose();
        }
        Repaint();
    }

    private bool IsDownloading(string key) { return _jobs.Any(j => j.key == key); }

    private void DownloadCatalogItem(FontCatalogItem item)
    {
        StartDownload(item.name, item.url, FallbackFolder + "/" + item.fileName, false, item.name);
        if (!string.IsNullOrEmpty(item.licenseUrl) && !File.Exists(LocalizationPaths.Abs(FallbackFolder + "/" + item.licenseFile)))
            StartDownload(item.name + " license", item.licenseUrl, FallbackFolder + "/" + item.licenseFile, true, item.name);
    }

    // ---------------------------------------------------------------- tab

    private static readonly string[] FontSubTabs = { "Languages", "Library", "Coverage" };

    private void DrawFontsTab()
    {
        _fontSub = LocUI.Tabs(_fontSub, FontSubTabs);
        _fontsScroll = EditorGUILayout.BeginScrollView(_fontsScroll);
        GUILayout.Space(4);
        switch (_fontSub)
        {
            case 0: DrawFontLanguages(); break;
            case 1: DrawFontLibrary(); break;
            case 2: DrawFontCoverage(); break;
        }
        EditorGUILayout.EndScrollView();
    }

    // ---------------------------------------------------------------- Languages (swap)

    private void DrawFontLanguages()
    {
        if (_settingsSO == null || _settingsSO.targetObject != _settings) _settingsSO = new SerializedObject(_settings);

        LocUI.Hint("1. In <b>Font roles</b>, assign each project font to a role (for example UI or Dialogue). 2. Pick a language and choose <b>one font per role</b>, plus the <b>fallback chain</b> for missing glyphs. Labels keep their own font for any role left empty. Create TMP font assets in the <b>Library</b> sub-tab. A language entry with a pseudolocale code (for example pseudo-cjk) lets you test fonts before translations exist.");

        var codes = _settings.languages.Where(l => l != null).Select(l => l.code).ToList();
        using (LocUI.Card())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (codes.Count > 0)
                {
                    int li = Mathf.Max(0, codes.IndexOf(_fontLang));
                    li = EditorGUILayout.Popup("Language", li, codes.ToArray());
                    _fontLang = codes[li];
                }

                var candidates = _table.languages.Concat(PseudoLocalizer.Codes).Where(c => !codes.Contains(c)).Distinct().ToList();
                if (candidates.Count > 0)
                {
                    int ci = Mathf.Max(0, candidates.IndexOf(_newFontLangCode));
                    ci = EditorGUILayout.Popup(ci, candidates.ToArray(), GUILayout.Width(130));
                    _newFontLangCode = candidates[ci];
                    if (GUILayout.Button("Add entry", GUILayout.Width(80)))
                    {
                        Undo.RecordObject(_settings, "Add language entry");
                        _settings.languages.Add(new LocalizationLanguage { code = _newFontLangCode, displayName = PseudoLocalizer.IsPseudo(_newFontLangCode) ? PseudoLocalizer.Get(_newFontLangCode).DisplayName : _newFontLangCode });
                        EditorUtility.SetDirty(_settings);
                        _fontLang = _newFontLangCode;
                    }
                }
            }
        }

        var lang = _settings.Find(_fontLang);
        if (lang == null) { LocUI.Muted_("No language entry selected."); return; }

        var tmpAssets = FindTmpAssets();
        DrawFontRoles(tmpAssets);

        using (LocUI.Card(LocUI.Accent))
        {
            LocUI.Section("Fonts for '" + lang.code + "'", "One font per role. Empty keeps each label's own font for that role.");
            foreach (var role in _settings.fontRoles.Where(r => r != null && !string.IsNullOrEmpty(r.name)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var f = (TMP_FontAsset)EditorGUILayout.ObjectField(role.name, lang.FontForRole(role.name), typeof(TMP_FontAsset), false);
                    if (tmpAssets.Count > 0)
                    {
                        int pick = EditorGUILayout.Popup(0, new[] { "Quick pick..." }.Concat(tmpAssets.Select(a => a.name)).ToArray(), GUILayout.Width(130));
                        if (pick > 0) f = tmpAssets[pick - 1];
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, "Set role font");
                        lang.SetRoleFont(role.name, f);
                        EditorUtility.SetDirty(_settings);
                    }
                    if (GUILayout.Button(_previewRole == role.name ? "Previewing" : "Preview", EditorStyles.miniButton, GUILayout.Width(70)))
                        _previewRole = role.name;
                }
            }

            EditorGUI.BeginChangeCheck();
            var main = (TMP_FontAsset)EditorGUILayout.ObjectField(new GUIContent("Unassigned fonts", "Used for labels whose font is not assigned to any role. Empty keeps the label's own font."), lang.font, typeof(TMP_FontAsset), false);
            float scale = EditorGUILayout.Slider("Size scale", lang.sizeScale, 0.5f, 1.5f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, "Edit language font");
                lang.font = main;
                lang.sizeScale = scale;
                EditorUtility.SetDirty(_settings);
            }
        }

        using (LocUI.Card(LocUI.Violet))
        {
            LocUI.Section("Fallback chain", "Tried in order when the main font has no glyph.");
            int remove = -1, up = -1;
            for (int i = 0; i < lang.fallbackFonts.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label((i + 1) + ".", GUILayout.Width(22));
                    EditorGUI.BeginChangeCheck();
                    var f = (TMP_FontAsset)EditorGUILayout.ObjectField(lang.fallbackFonts[i], typeof(TMP_FontAsset), false);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_settings, "Edit fallback"); lang.fallbackFonts[i] = f; EditorUtility.SetDirty(_settings); }
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("\u25B2", GUILayout.Width(26))) up = i;
                    GUI.enabled = true;
                    if (GUILayout.Button("\u2715", GUILayout.Width(26))) remove = i;
                }
            }
            if (up > 0 || remove >= 0)
            {
                Undo.RecordObject(_settings, "Edit fallback chain");
                if (up > 0) { var t = lang.fallbackFonts[up]; lang.fallbackFonts[up] = lang.fallbackFonts[up - 1]; lang.fallbackFonts[up - 1] = t; }
                else lang.fallbackFonts.RemoveAt(remove);
                EditorUtility.SetDirty(_settings);
            }

            var add = (TMP_FontAsset)EditorGUILayout.ObjectField("Add fallback", null, typeof(TMP_FontAsset), false);
            if (add != null) { Undo.RecordObject(_settings, "Add fallback"); lang.fallbackFonts.Add(add); EditorUtility.SetDirty(_settings); }

            if (GUILayout.Button("Write chain into this language's fonts (persistent)"))
            {
                var targets = lang.roleFonts.Where(r => r != null && r.font != null).Select(r => r.font)
                    .Concat(lang.font != null ? new[] { lang.font } : new TMP_FontAsset[0]).Distinct().ToList();
                if (targets.Count == 0) _status = "Set a role font or the unassigned font first.";
                else
                {
                    int added = 0;
                    foreach (var target in targets)
                    {
                        Undo.RecordObject(target, "Apply fallback chain");
                        foreach (var fb in lang.fallbackFonts)
                            if (fb != null && fb != target && !target.fallbackFontAssetTable.Contains(fb)) { target.fallbackFontAssetTable.Add(fb); added++; }
                        EditorUtility.SetDirty(target);
                    }
                    AssetDatabase.SaveAssets();
                    _status = "Added " + added + " fallback link(s) across " + targets.Count + " font(s).";
                }
            }
        }

        var previewFont = (!string.IsNullOrEmpty(_previewRole) ? lang.FontForRole(_previewRole) : null) ?? lang.font;
        LocUI.Section("Preview" + (string.IsNullOrEmpty(_previewRole) ? "" : ": " + _previewRole), "Rendered with the role's font plus the chain (applied temporarily, nothing is saved).");
        DrawSampleControls();
        DrawTmpPreview(previewFont, lang.fallbackFonts, 90f);

        _rawSettings = EditorGUILayout.Foldout(_rawSettings, "All localization settings", true);
        if (_rawSettings)
        {
            _settingsSO.Update();
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("defaultLanguage"));
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("pseudoExpansion"));
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("maxLengthRatio"));
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("forbiddenCharacters"));
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("dialogueAssetTypes"), true);
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("onlyUsedDialogueAssets"));
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("dialogueFieldRules"), true);
            EditorGUILayout.PropertyField(_settingsSO.FindProperty("fieldDenyList"), true);
            if (_settingsSO.ApplyModifiedProperties()) EditorUtility.SetDirty(_settings);
        }
    }

    private void DrawSampleControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _sampleIndex = EditorGUILayout.Popup("Sample text", _sampleIndex, SampleNames);
            _sampleSize = EditorGUILayout.Slider(_sampleSize, 12f, 60f, GUILayout.Width(220));
        }
        if (_sampleIndex == SampleTexts.Length - 1) _customSample = EditorGUILayout.TextField(_customSample);
    }

    private void DrawTmpPreview(TMP_FontAsset font, List<TMP_FontAsset> chain, float height)
    {
        var r = GUILayoutUtility.GetRect(100, height, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return;
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) return;

        var original = font.fallbackFontAssetTable.ToList();
        var savedBox = _box;
        try
        {
            if (chain != null)
                foreach (var c in chain) if (c != null && c != font && !font.fallbackFontAssetTable.Contains(c)) font.fallbackFontAssetTable.Add(c);
            _box = new BoxInfo { size = new Vector2(Mathf.Max(100, r.width - 24), Mathf.Max(30, r.height - 16)), fontSize = _sampleSize, font = font, wrap = true };
            var tex = RenderPreview(r, SampleText, font, out _);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
        }
        catch (System.Exception ex) { _status = "Preview failed: " + ex.Message; }
        finally
        {
            font.fallbackFontAssetTable.Clear();
            font.fallbackFontAssetTable.AddRange(original);
            _box = savedBox;
        }
    }

    // ---------------------------------------------------------------- Font roles

    private string _previewRole = "";
    private string _newRoleName = "";
    private bool _rolesFoldout = true;
    private bool _showUnusedFonts;
    private Dictionary<TMP_FontAsset, int> _fontUsage;

    /// <summary>Counts TMP labels per font across project scenes and prefabs by reading their YAML (no loading).</summary>
    private static Dictionary<TMP_FontAsset, int> CountFontUsage(List<TMP_FontAsset> fonts)
    {
        var byGuid = new Dictionary<string, TMP_FontAsset>();
        foreach (var f in fonts)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(f));
            if (!string.IsNullOrEmpty(guid)) byGuid[guid] = f;
        }
        var counts = fonts.ToDictionary(f => f, f => 0);
        var rx = new System.Text.RegularExpressions.Regex(@"m_fontAsset: \{fileID: 11400000, guid: ([0-9a-f]{32})", System.Text.RegularExpressions.RegexOptions.Compiled);
        var paths = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets/_GoodCopBadCop" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        for (int i = 0; i < paths.Count; i++)
        {
            if (i % 50 == 0) EditorUtility.DisplayProgressBar("Counting font usage", paths[i], (float)i / paths.Count);
            string text;
            try { text = File.ReadAllText(LocalizationPaths.Abs(paths[i])); } catch { continue; }
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
                if (byGuid.TryGetValue(m.Groups[1].Value, out var f)) counts[f]++;
        }
        EditorUtility.ClearProgressBar();
        return counts;
    }

    private void DrawFontRoles(List<TMP_FontAsset> tmpAssets)
    {
        using (LocUI.Card(LocUI.Violet))
        {
            _rolesFoldout = EditorGUILayout.Foldout(_rolesFoldout, "Font roles (shared by all languages)", true);
            if (!_rolesFoldout) return;
            LocUI.Muted_("Assign each project font to a role. A label keeps its own font unless its role has a font for the active language. " +
                         "A label can force a role with LocalizedText > Font Role. Subtitles default to the Dialogue role. " +
                         "Double-click or right-click a role to rename it.");

            var roles = _settings.fontRoles;
            int removeRole = -1;
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    if (roles[i] == null) continue;
                    if (_renamingRole == i)
                    {
                        DrawRoleRenameField(i);
                        continue;
                    }
                    LocUI.Pill(roles[i].name + " (" + roles[i].sourceFonts.Count(f => f != null) + ")", LocUI.Violet);
                    HandleRolePillClick(i, GUILayoutUtility.GetLastRect());
                    if (GUILayout.Button("\u2715", EditorStyles.miniButton, GUILayout.Width(20))) removeRole = i;
                }
                GUILayout.FlexibleSpace();
                _newRoleName = EditorGUILayout.TextField(_newRoleName, GUILayout.Width(110));
                GUI.enabled = !string.IsNullOrWhiteSpace(_newRoleName) && !_settings.HasRole(_newRoleName.Trim());
                if (GUILayout.Button("Add role", GUILayout.Width(70)))
                {
                    Undo.RecordObject(_settings, "Add font role");
                    roles.Add(new FontRole { name = _newRoleName.Trim() });
                    _newRoleName = "";
                    EditorUtility.SetDirty(_settings);
                }
                GUI.enabled = true;
            }
            if (removeRole >= 0 && EditorUtility.DisplayDialog("Remove font role", "Remove role '" + roles[removeRole].name + "'? Its fonts become unassigned.", "Remove", "Cancel"))
            {
                Undo.RecordObject(_settings, "Remove font role");
                string removed = roles[removeRole].name;
                roles.RemoveAt(removeRole);
                _renamingRole = -1;
                foreach (var l in _settings.languages) if (l != null) l.roleFonts.RemoveAll(r => r == null || r.role == removed);
                EditorUtility.SetDirty(_settings);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_fontUsage == null ? "Count usage in scenes/prefabs" : "Recount usage", GUILayout.Width(200)))
                    _fontUsage = CountFontUsage(tmpAssets);
                if (_fontUsage != null) _showUnusedFonts = GUILayout.Toggle(_showUnusedFonts, "Show unused fonts", GUILayout.Width(140));
                GUILayout.FlexibleSpace();
            }

            var roleNames = new[] { "Unassigned" }.Concat(roles.Where(r => r != null).Select(r => r.name)).ToArray();
            var ordered = _fontUsage != null
                ? tmpAssets.Where(f => _showUnusedFonts || _fontUsage.TryGetValue(f, out int n) && n > 0 || _settings.RoleOf(f) != null)
                           .OrderByDescending(f => _fontUsage.TryGetValue(f, out int n) ? n : 0).ToList()
                : tmpAssets;

            foreach (var f in ordered)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(f.name, EditorStyles.label, GUILayout.MinWidth(180))) EditorGUIUtility.PingObject(f);
                    if (_fontUsage != null)
                        GUILayout.Label(_fontUsage.TryGetValue(f, out int n) ? n + " labels" : "-", EditorStyles.miniLabel, GUILayout.Width(70));
                    string current = _settings.RoleOf(f);
                    int idx = current == null ? 0 : System.Array.IndexOf(roleNames, current);
                    int next = EditorGUILayout.Popup(Mathf.Max(0, idx), roleNames, GUILayout.Width(130));
                    if (next != idx)
                    {
                        Undo.RecordObject(_settings, "Assign font role");
                        foreach (var r in roles) if (r != null) r.sourceFonts.Remove(f);
                        if (next > 0) roles.First(r => r != null && r.name == roleNames[next]).sourceFonts.Add(f);
                        EditorUtility.SetDirty(_settings);
                    }
                }
            }
            if (tmpAssets.Count == 0) LocUI.Muted_("No TMP font assets found under Assets/_GoodCopBadCop.");
        }
    }

    // ---------------------------------------------------------------- Library

    private static List<TMP_FontAsset> FindTmpAssets()
    {
        return AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/_GoodCopBadCop" })
            .Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null).OrderBy(a => a.name).ToList();
    }

    private void DrawFontLibrary()
    {
        LocUI.Section("Download fonts", "All catalog fonts are SIL Open Font License. The license file is saved next to each font.");
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var role in RoleFilters)
                if (LocUI.Chip(_roleFilter == role, role)) _roleFilter = role;
            GUILayout.FlexibleSpace();
        }

        foreach (var item in Catalog.Where(i => _roleFilter == "All" || i.role == _roleFilter))
        {
            bool installed = File.Exists(LocalizationPaths.Abs(FallbackFolder + "/" + item.fileName));
            bool busy = IsDownloading(item.name);
            using (LocUI.Card(installed ? LocUI.Ok : LocUI.Neutral))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(item.name, EditorStyles.boldLabel);
                    LocUI.Muted_(item.scripts + "  |  " + item.fileName);
                }
                GUILayout.FlexibleSpace();
                LocUI.Pill(item.role, LocUI.Violet);
                LocUI.Pill("OFL", LocUI.Neutral);
                LocUI.Pill(installed ? "INSTALLED" : "NOT INSTALLED", installed ? LocUI.Ok : LocUI.Warn);
                var job = _jobs.FirstOrDefault(j => j.key == item.name && !j.isLicense);
                if (busy && job != null) GUILayout.Label(Mathf.RoundToInt(job.req.downloadProgress * 100f) + "%", GUILayout.Width(40));
                GUI.enabled = !busy;
                if (GUILayout.Button(installed ? "Re-download" : "Download", GUILayout.Width(100))) DownloadCatalogItem(item);
                GUI.enabled = true;
            }
        }

        using (LocUI.Card(LocUI.Accent))
        {
            GUILayout.Label("Custom font URL", EditorStyles.boldLabel);
            LocUI.Muted_("Direct link to a .ttf or .otf file. Only use fonts whose license allows embedding in a game, and save the license file too.");
            _customName = EditorGUILayout.TextField("File name (with extension)", _customName);
            _customUrl = EditorGUILayout.TextField("Font URL", _customUrl);
            _customLicenseUrl = EditorGUILayout.TextField("License URL (optional)", _customLicenseUrl);
            GUI.enabled = !string.IsNullOrWhiteSpace(_customName) && !string.IsNullOrWhiteSpace(_customUrl) && !IsDownloading("custom");
            if (GUILayout.Button("Download custom font", GUILayout.Width(170)))
            {
                string file = _customName.Trim();
                StartDownload(file, _customUrl.Trim(), FallbackFolder + "/" + file, false, "custom");
                if (!string.IsNullOrWhiteSpace(_customLicenseUrl))
                    StartDownload(file + " license", _customLicenseUrl.Trim(), FallbackFolder + "/License-" + Path.GetFileNameWithoutExtension(file) + ".txt", true, "custom");
            }
            GUI.enabled = true;
        }

        LocUI.Section("Installed fonts", "Preview a sample in each font, check glyph coverage, and create a Dynamic TMP font asset.");
        DrawSampleControls();

        var fontGuids = AssetDatabase.FindAssets("t:Font", new[] { FallbackFolder });
        var tmpAssets = FindTmpAssets();
        foreach (var guid in fontGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) continue;

            string sample = SampleText;
            int total = 0, missing = 0;
            foreach (char c in sample.Distinct())
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                total++;
                if (!font.HasCharacter(c)) missing++;
            }
            var existing = tmpAssets.FirstOrDefault(a => a.sourceFontFile == font);

            using (LocUI.Card(missing == 0 ? LocUI.Ok : LocUI.Warn))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(font.name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    LocUI.Pill(missing == 0 ? "COVERS SAMPLE" : missing + "/" + total + " MISSING", missing == 0 ? LocUI.Ok : LocUI.Warn);
                    LocUI.Pill(existing != null ? "TMP READY" : "NO TMP ASSET", existing != null ? LocUI.Accent : LocUI.Neutral);
                    if (GUILayout.Button("Reveal", EditorStyles.miniButton, GUILayout.Width(52))) EditorGUIUtility.PingObject(font);
                    GUI.enabled = existing == null;
                    if (GUILayout.Button("Create TMP asset", GUILayout.Width(120))) CreateTmpAsset(font);
                    GUI.enabled = true;
                }
                var style = new GUIStyle(EditorStyles.label) { font = font, fontSize = Mathf.RoundToInt(_sampleSize), wordWrap = true, richText = false };
                GUILayout.Label(sample, style);
            }
        }
        if (fontGuids.Length == 0) LocUI.Muted_("No fonts in " + FallbackFolder + " yet.");

        LocUI.Section("TMP font assets", "Preview one, or assign it as the main font or a fallback for the language chosen in the Languages sub-tab.");
        var lang = _settings.Find(_fontLang);
        foreach (var a in tmpAssets)
        {
            bool selected = _previewAsset == a;
            using (LocUI.Card(selected ? LocUI.Accent : LocUI.Neutral))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(a.name, EditorStyles.boldLabel);
                    LocUI.Muted_((a.sourceFontFile != null ? a.sourceFontFile.name : "no source font") + "  |  " + a.atlasPopulationMode + "  |  " + a.characterTable.Count + " characters  |  " + a.fallbackFontAssetTable.Count + " built-in fallbacks");
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(selected ? "Previewing" : "Preview", GUILayout.Width(80))) _previewAsset = selected ? null : a;
                GUI.enabled = lang != null;
                if (GUILayout.Button(new GUIContent("Unassigned font", "Use as '" + _fontLang + "' font for labels whose font has no role."), GUILayout.Width(130)))
                {
                    Undo.RecordObject(_settings, "Set unassigned font"); lang.font = a; EditorUtility.SetDirty(_settings);
                }
                bool inChain = lang != null && lang.fallbackFonts.Contains(a);
                if (GUILayout.Button(inChain ? "Remove fallback" : "Add fallback", GUILayout.Width(110)))
                {
                    Undo.RecordObject(_settings, "Edit fallback");
                    if (inChain) lang.fallbackFonts.Remove(a); else lang.fallbackFonts.Add(a);
                    EditorUtility.SetDirty(_settings);
                }
                GUI.enabled = true;
            }
        }
        if (tmpAssets.Count == 0) LocUI.Muted_("No TMP font assets yet. Use 'Create TMP asset' on an installed font.");
        if (_previewAsset != null)
        {
            LocUI.Section("Preview: " + _previewAsset.name);
            DrawTmpPreview(_previewAsset, null, 90f);
        }
    }

    private void CreateTmpAsset(Font font)
    {
        Directory.CreateDirectory(LocalizationPaths.Abs(TmpFolder));
        string path = TmpFolder + "/" + font.name + " SDF.asset";
        if (File.Exists(LocalizationPaths.Abs(path))) { _status = "Already exists: " + path; return; }

        var asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        if (asset == null) { _status = "Failed to create a TMP font asset from " + font.name + "."; return; }

        asset.name = font.name + " SDF";
        AssetDatabase.CreateAsset(asset, path);
        if (asset.material != null)
        {
            asset.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
        }
        if (asset.atlasTextures != null)
            for (int i = 0; i < asset.atlasTextures.Length; i++)
            {
                if (asset.atlasTextures[i] == null) continue;
                asset.atlasTextures[i].name = font.name + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[i], asset);
            }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path);
        _status = "Created " + path + " (Dynamic, multi-atlas).";
    }

    // ---------------------------------------------------------------- Coverage

    private void DrawFontCoverage()
    {
        LocUI.Hint("Checks every character the selected language (or pseudolocale) would display against its main font and fallback chain, and lists those with no glyph.");
        var langs = new List<string> { LocalizationTable.SourceLanguage };
        langs.AddRange(_table.languages);
        langs.AddRange(PseudoLocalizer.Codes.Where(c => !langs.Contains(c)));
        using (LocUI.Card())
        {
            int li = Mathf.Max(0, langs.IndexOf(_coverageLang));
            li = EditorGUILayout.Popup("Language", li, langs.ToArray());
            _coverageLang = langs[li];
            if (LocUI.Button("Run coverage report", true, GUILayout.Width(160))) RunCoverage();
        }
        foreach (var line in _coverageReport.Take(120)) LocUI.Muted_(line);
    }

    private void RunCoverage()
    {
        _coverageReport.Clear();
        var lang = _settings.Find(_coverageLang);
        TMP_FontAsset font = lang != null && lang.font != null ? lang.font : TMP_Settings.defaultFontAsset;
        if (font == null) { _coverageReport.Add("No font available (assign one on the language entry or in TMP Settings)."); return; }

        var chars = new Dictionary<char, string>();
        foreach (var e in _table.entries)
        {
            string text = TextFor(e, _coverageLang);
            foreach (char c in text)
                if (!char.IsControl(c) && !char.IsWhiteSpace(c) && !chars.ContainsKey(c)) chars[c] = e.key;
        }

        var missing = new List<KeyValuePair<char, string>>();
        foreach (var kv in chars)
        {
            bool covered = font.HasCharacter(kv.Key, true);
            if (!covered && lang != null)
                foreach (var fb in lang.fallbackFonts)
                    if (fb != null && fb.HasCharacter(kv.Key, true)) { covered = true; break; }
            if (!covered) missing.Add(kv);
        }

        _coverageReport.Add($"{_coverageLang}: {chars.Count} distinct characters in {_table.entries.Count} strings, font '{font.name}', {missing.Count} without glyph.");
        foreach (var kv in missing.Take(100))
            _coverageReport.Add($"U+{(int)kv.Key:X4} '{kv.Key}'  first seen in {kv.Value}");
    }
}
