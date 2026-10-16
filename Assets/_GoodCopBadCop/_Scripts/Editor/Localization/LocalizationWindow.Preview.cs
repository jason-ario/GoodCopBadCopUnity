using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private struct BoxInfo
    {
        public Vector2 size;
        public float fontSize;
        public TMP_FontAsset font;
        public bool wrap;
        public bool autosize;
        public bool measured;
    }

    private string _previewLang = "pseudo-latincyr";
    private string _boxKey;
    private BoxInfo _box;
    private PreviewRenderUtility _pru;
    private GameObject _previewGo;
    private TextMeshPro _previewTmp;
    private bool _cycleLive;
    private double _nextCycle;
    private int _cycleIndex;

    private void OnEditorUpdate()
    {
        PollDownloads();
        if (_cycleLive && Application.isPlaying && EditorApplication.timeSinceStartup > _nextCycle)
        {
            var langs = Loc.AvailableLanguages();
            _cycleIndex = (_cycleIndex + 1) % langs.Count;
            Loc.SetLanguage(langs[_cycleIndex]);
            _nextCycle = EditorApplication.timeSinceStartup + 2.5;
            Repaint();
        }
    }

    private void DisposePreview()
    {
        if (_previewGo != null) DestroyImmediate(_previewGo);
        if (_pru != null) _pru.Cleanup();
        _previewGo = null;
        _pru = null;
        _previewTmp = null;
    }

    private BoxInfo MeasureBox(string key)
    {
        var info = new BoxInfo { size = new Vector2(320, 64), fontSize = 24, font = TMP_Settings.defaultFontAsset, wrap = true };
        var b = LocBindings.For(key).FirstOrDefault(x => x.kind == "TMP");
        if (b == null) return info;

        var tmp = LocalizationApply.ResolveTmp(b, out var prefabRoot, out var _);
        if (tmp == null) return info;

        var r = tmp.rectTransform.rect;
        if (r.width > 1 && r.height > 1) { info.size = r.size; info.measured = true; }
        info.fontSize = tmp.fontSize;
        info.font = tmp.font != null ? tmp.font : info.font;
        info.wrap = tmp.textWrappingMode != TextWrappingModes.NoWrap;
        info.autosize = tmp.enableAutoSizing;
        if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
        return info;
    }

    private string TextFor(LocEntry e, string lang)
    {
        if (lang == LocalizationTable.SourceLanguage) return e.source;
        if (PseudoLocalizer.IsPseudo(lang)) return PseudoLocalizer.Generate(lang, e.source, e.key, _settings.pseudoExpansion);
        string v = e.GetValue(lang);
        return string.IsNullOrEmpty(v) ? e.source : v;
    }

    private TMP_FontAsset FontFor(string lang, TMP_FontAsset sourceFont, string roleOverride = null)
    {
        var l = _settings.Find(lang);
        // Pseudo CJK has no configured font unless the user adds a language entry with the same code.
        if (l == null) return sourceFont;
        var f = _settings.ResolveFont(l, sourceFont, roleOverride);
        return f != null ? f : sourceFont;
    }

    /// <summary>Dialogue strings (no TMP label) preview in the Dialogue role font when that role exists.</summary>
    private string DialogueRoleFor(string key)
    {
        if (!_settings.HasRole("Dialogue")) return null;
        return LocBindings.For(key).Any(b => b.kind != "TMP") ? "Dialogue" : null;
    }

    private void EnsureRig()
    {
        if (_pru == null)
        {
            _pru = new PreviewRenderUtility();
            _pru.camera.orthographic = true;
            _pru.camera.clearFlags = CameraClearFlags.SolidColor;
            _pru.camera.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
            _pru.camera.nearClipPlane = 0.1f;
            _pru.camera.farClipPlane = 50f;
        }
        if (_previewGo == null)
        {
            _previewGo = EditorUtility.CreateGameObjectWithHideFlags("LocPreviewText", HideFlags.HideAndDontSave, typeof(TextMeshPro));
            _previewTmp = _previewGo.GetComponent<TextMeshPro>();
            _previewTmp.isOrthographic = true;
            _previewTmp.alignment = TextAlignmentOptions.TopLeft;
            _previewTmp.overflowMode = TextOverflowModes.Overflow;
            _previewTmp.color = Color.white;
            _pru.AddSingleGO(_previewGo);
        }
    }

    /// <summary>Renders the text in a box of the original element's size. Returns the texture and the pixels-per-unit scale.</summary>
    private Texture RenderPreview(Rect rect, string text, TMP_FontAsset font, out float pixelsPerUnit)
    {
        EnsureRig();
        _previewTmp.font = font;
        _previewTmp.fontSize = _box.fontSize;
        _previewTmp.textWrappingMode = _box.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        _previewTmp.enableAutoSizing = false;
        _previewTmp.rectTransform.sizeDelta = _box.size;
        _previewTmp.rectTransform.position = Vector3.zero;
        _previewTmp.text = text;
        _previewTmp.ForceMeshUpdate();

        float aspect = rect.width / Mathf.Max(1f, rect.height);
        float half = Mathf.Max(_box.size.y * 0.5f * 1.5f, _box.size.x * 0.5f * 1.15f / aspect);
        _pru.camera.orthographicSize = half;
        _pru.camera.transform.position = new Vector3(0, 0, -10);
        _pru.camera.transform.rotation = Quaternion.identity;
        pixelsPerUnit = rect.height / (2f * half);

        _pru.BeginPreview(rect, GUIStyle.none);
        _pru.camera.Render();
        return _pru.EndPreview();
    }

    private Texture2D RenderPreviewStatic(Rect rect, string text, TMP_FontAsset font)
    {
        EnsureRig();
        _previewTmp.font = font;
        _previewTmp.fontSize = _box.fontSize;
        _previewTmp.textWrappingMode = _box.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        _previewTmp.enableAutoSizing = false;
        _previewTmp.rectTransform.sizeDelta = _box.size;
        _previewTmp.rectTransform.position = Vector3.zero;
        _previewTmp.text = text;
        _previewTmp.ForceMeshUpdate();

        float aspect = rect.width / Mathf.Max(1f, rect.height);
        _pru.camera.orthographicSize = Mathf.Max(_box.size.y * 0.5f * 1.5f, _box.size.x * 0.5f * 1.15f / aspect);
        _pru.camera.transform.position = new Vector3(0, 0, -10);
        _pru.camera.transform.rotation = Quaternion.identity;

        _pru.BeginStaticPreview(rect);
        _pru.camera.Render();
        return _pru.EndStaticPreview();
    }

    private void DrawPreviewTab()
    {
        var langs = new System.Collections.Generic.List<string> { LocalizationTable.SourceLanguage };
        langs.AddRange(_table.languages.Where(l => !PseudoLocalizer.IsPseudo(l)));
        langs.AddRange(PseudoLocalizer.Codes);

        using (new EditorGUILayout.HorizontalScope())
        {
            int li = Mathf.Max(0, langs.IndexOf(_previewLang));
            li = EditorGUILayout.Popup("Language", li, langs.ToArray());
            _previewLang = langs[li];

            if (_selectedKey == null && _table.entries.Count > 0) _selectedKey = _table.entries[0].key;
            EditorGUILayout.LabelField("Key", GUILayout.Width(30));

            int idx = _selectedKey == null ? -1 : _table.entries.FindIndex(e => e.key == _selectedKey);
            GUI.enabled = idx > 0;
            if (GUILayout.Button("\u25C0", EditorStyles.miniButtonLeft, GUILayout.Width(24))) { _selectedKey = _table.entries[idx - 1].key; GUI.FocusControl(null); }
            GUI.enabled = idx >= 0 && idx < _table.entries.Count - 1;
            if (GUILayout.Button("\u25B6", EditorStyles.miniButtonRight, GUILayout.Width(24))) { _selectedKey = _table.entries[idx + 1].key; GUI.FocusControl(null); }
            GUI.enabled = true;

            var label = new GUIContent(string.IsNullOrEmpty(_selectedKey) ? "Select a string..." : _selectedKey, "Browse by sheet, or type to search keys and English text");
            var keyRect = GUILayoutUtility.GetRect(label, EditorStyles.popup, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true));
            if (EditorGUI.DropdownButton(keyRect, label, FocusType.Keyboard, EditorStyles.popup))
            {
                new LocKeyDropdown(_table.entries, k =>
                {
                    _selectedKey = k;
                    GUI.FocusControl(null);
                    Repaint();
                }).Show(keyRect);
            }
        }

        if (_selectedKey == null || !_table.TryGet(_selectedKey, out var entry))
        {
            EditorGUILayout.HelpBox("Pick a string from the Key dropdown (type to search), or use the Preview button in the Strings tab.", MessageType.Info);
            DrawLiveControls(langs);
            return;
        }

        if (_boxKey != _selectedKey) { _box = MeasureBox(_selectedKey); _boxKey = _selectedKey; }

        string text = TextFor(entry, _previewLang);
        var font = FontFor(_previewLang, _box.font, _box.measured ? null : DialogueRoleFor(_selectedKey));
        EditorGUILayout.LabelField("Source", entry.source, EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField(_previewLang, text, EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("Box " + _box.size.x.ToString("0") + " x " + _box.size.y.ToString("0")
                                   + " | size " + _box.fontSize.ToString("0.#") + " | " + (_box.wrap ? "wrap" : "no wrap")
                                   + (_box.autosize ? " | autosize" : "") + (_box.measured ? "" : " | no origin box, using default"),
            EditorStyles.miniLabel);

        // Warnings
        if (font == null) { EditorGUILayout.HelpBox("No TMP font available.", MessageType.Warning); }
        else
        {
            EnsureRig();
            _previewTmp.font = font;
            _previewTmp.fontSize = _box.fontSize;
            _previewTmp.textWrappingMode = _box.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            _previewTmp.enableAutoSizing = false;
            var pref = _previewTmp.GetPreferredValues(text, _box.wrap ? _box.size.x : 100000f, 100000f);
            if (pref.y > _box.size.y + 0.5f || (!_box.wrap && pref.x > _box.size.x + 0.5f))
                EditorGUILayout.HelpBox("Overflow: needs " + pref.x.ToString("0") + " x " + pref.y.ToString("0") + ", box is " + _box.size.x.ToString("0") + " x " + _box.size.y.ToString("0") + ".", MessageType.Warning);
            string missing = LocalizationValidator.MissingGlyphs(font, text);
            if (missing.Length > 0) EditorGUILayout.HelpBox("Tofu risk, no glyph for: " + missing, MessageType.Warning);
        }
        if (_previewLang != LocalizationTable.SourceLanguage && !PseudoLocalizer.IsPseudo(_previewLang))
            foreach (var issue in LocalizationValidator.Validate(entry.source, text, _settings))
                EditorGUILayout.HelpBox(issue.ToString(), issue.severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning);
        if (PseudoLocalizer.IsPseudo(_previewLang))
            foreach (var v in PseudoLocalizer.Get(_previewLang).Verify(entry.source, text, entry.key, _settings.pseudoExpansion))
                EditorGUILayout.HelpBox("Pseudolocale self-check: " + v, MessageType.Error);

        // Preview image
        Rect r = GUILayoutUtility.GetRect(100, 260, GUILayout.ExpandWidth(true));
        if (font != null && Event.current.type == EventType.Repaint)
        {
            try
            {
                var tex = RenderPreview(r, text, font, out float ppu);
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
                var boxRect = new Rect(r.center.x - _box.size.x * ppu * 0.5f, r.center.y - _box.size.y * ppu * 0.5f, _box.size.x * ppu, _box.size.y * ppu);
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                Handles.DrawSolidRectangleWithOutline(boxRect, Color.clear, Handles.color);
                Handles.EndGUI();
            }
            catch (System.Exception ex) { _status = "Preview render failed: " + ex.Message; }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Capture reference (preview)")) CaptureReference(entry, text, font);
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Capture reference (Game view)")) CaptureGameView(entry);
            GUI.enabled = true;
        }

        DrawLiveControls(langs);
    }

    private void DrawLiveControls(System.Collections.Generic.List<string> langs)
    {
        EditorGUILayout.Space();
        LocUI.Section("Live (Play Mode)");
        GUI.enabled = Application.isPlaying;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Set '" + _previewLang + "' live")) Loc.SetLanguage(_previewLang);
            _cycleLive = GUILayout.Toggle(_cycleLive, "Cycle through all locales (2.5 s)");
        }
        GUI.enabled = true;
        if (!Application.isPlaying) EditorGUILayout.LabelField("Enter Play Mode to switch the running game.", EditorStyles.miniLabel);
        else EditorGUILayout.LabelField("Current runtime language: " + Loc.CurrentLanguage, EditorStyles.miniLabel);
    }

    private void CaptureReference(LocEntry entry, string text, TMP_FontAsset font)
    {
        if (font == null) return;
        LocalizationPaths.EnsureFolders();
        string rel = LocalizationPaths.Refs + "/" + entry.key + ".png";

        // Always capture the English source box, which is what translators need to see.
        var saved = _previewLang;
        int w = 800;
        int h = Mathf.Max(120, Mathf.RoundToInt(w * (_box.size.y * 1.6f) / Mathf.Max(1f, _box.size.x * 1.25f)));
        var rect = new Rect(0, 0, w, h);
        var png = RenderPreviewStatic(rect, entry.source, FontFor(LocalizationTable.SourceLanguage, _box.font));
        if (png == null) { _status = "Reference capture failed."; return; }
        File.WriteAllBytes(LocalizationPaths.Abs(rel), png.EncodeToPNG());
        DestroyImmediate(png);
        _previewLang = saved;

        AttachRefNote(entry, "Refs/" + entry.key + ".png");
        AssetDatabase.Refresh();
        _status = "Saved reference " + rel;
    }

    private void CaptureGameView(LocEntry entry)
    {
        LocalizationPaths.EnsureFolders();
        string rel = LocalizationPaths.Refs + "/" + entry.key + "_game.png";
        ScreenCapture.CaptureScreenshot(LocalizationPaths.Abs(rel));
        AttachRefNote(entry, "Refs/" + entry.key + "_game.png");
        _status = "Game view capture requested: " + rel + " (written at end of frame; refresh the project).";
    }

    private void AttachRefNote(LocEntry entry, string refPath)
    {
        string tag = "Ref: " + refPath;
        if ((entry.notes ?? "").Contains(tag)) return;
        entry.notes = string.IsNullOrEmpty(entry.notes) ? tag : entry.notes + " | " + tag;
        MarkDirty(entry);
        SaveDirty();
    }
}
