using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fit Review tab: steps through every localized text whose pseudo version doesn't fit, previews English and pseudo in the real box,
/// and records a decision per key: Fits (no limit) or Limit (writes max_chars). Decisions live in FitReview.json.
/// </summary>
public partial class LocalizationWindow
{
    private enum FrFilter { ToReview, Approved, Limited, All }

    private const int FrTexW = 640, FrTexH = 288;

    private List<CharFitItem> _fr;
    private List<CharFitItem> _frView = new List<CharFitItem>();
    private FitReviewStore _frStore;
    private FrFilter _frFilter = FrFilter.ToReview;
    private int _frIndex;
    private float _frReadable = CharLimitActions.DefaultReadableRatio;
    private float _frBelow = CharLimitActions.DefaultPseudoScaleBelow;
    private int _frLimitEdit;
    private string _frLimitKey;
    private string _frScanInfo = "";

    private PreviewRenderUtility _frPru;
    private GameObject _frGo;
    private TextMeshPro _frTmp;
    private Texture2D _frEnTex, _frPsTex;
    private string _frTexKey;
    private FrRender _frEn, _frPs;

    private struct FrRender
    {
        public float size;      // font size TMP ends up using
        public bool overflow;   // text spills outside the box
        public bool cut;        // Truncate / Ellipsis hid characters
        public float ppu;       // texture pixels per box unit
    }

    // ---------------------------------------------------------------- data

    private void FrScan()
    {
        CommitPendingSource();
        SaveDirty();
        _fr = CharLimitActions.Collect("Assets/_GoodCopBadCop", _frReadable, _frBelow, _table, out var st);
        _frStore = FitReviewStore.Load();
        _frScanInfo = st.measured + " localized texts measured, " + _fr.Count + " don't fit" +
                      (st.unknownKey > 0 ? " (" + _fr.Count(i => !i.inSheet) + " not in a sheet yet)" : "") + ".";
        _frIndex = 0;
        _frTexKey = null;
        FrRebuildView();
        _status = "Fit Review: " + _frScanInfo;
    }

    private FitReviewStore.Decision FrDecision(CharFitItem it)
    {
        return _frStore != null ? _frStore.Get(it.key, it.source) : null;
    }

    private bool FrMatches(CharFitItem it, FrFilter f)
    {
        var d = FrDecision(it);
        switch (f)
        {
            case FrFilter.ToReview: return d == null;
            case FrFilter.Approved: return d != null && d.decision == FitReviewStore.Approved;
            case FrFilter.Limited: return d != null && d.decision == FitReviewStore.Limit;
            default: return true;
        }
    }

    private void FrRebuildView()
    {
        _frView = _fr == null ? new List<CharFitItem>() : _fr.Where(i => FrMatches(i, _frFilter)).ToList();
        _frIndex = Mathf.Clamp(_frIndex, 0, Mathf.Max(0, _frView.Count - 1));
    }

    private CharFitItem FrCurrent => _frView.Count > 0 ? _frView[_frIndex] : null;

    private void FrMove(int delta)
    {
        if (_frView.Count == 0) return;
        _frIndex = (_frIndex + delta + _frView.Count) % _frView.Count;
        GUI.FocusControl(null);
    }

    private void FrDecide(CharFitItem it, string decision)
    {
        var prev = FrDecision(it);
        _table.TryGet(it.key, out var entry);

        if (decision == null)
        {
            _frStore.Clear(it.key);
            if (entry != null && prev != null && prev.decision == FitReviewStore.Limit && entry.maxChars == prev.limit) FrSetMaxChars(entry, 0);
        }
        else if (decision == FitReviewStore.Approved)
        {
            _frStore.Set(it.key, it.source, decision, 0);
            // Only undo a limit this review set; hand-entered limits stay.
            if (entry != null && prev != null && prev.decision == FitReviewStore.Limit && entry.maxChars == prev.limit) FrSetMaxChars(entry, 0);
        }
        else
        {
            int limit = Mathf.Max(1, _frLimitEdit);
            _frStore.Set(it.key, it.source, decision, limit);
            if (entry != null) FrSetMaxChars(entry, limit);
        }
        _frStore.Save();

        string what = decision == null ? "reset" : decision == FitReviewStore.Approved ? "approved (fits)" : "limit " + Mathf.Max(1, _frLimitEdit) + (entry == null ? " (saved; written to the sheet once the key is scanned)" : "");
        _status = it.key + ": " + what + ".";

        // In "To review" the item drops out of the list, which already advances to the next one.
        bool stays = FrMatches(it, _frFilter);
        FrRebuildView();
        if (stays) FrMove(1);
        GUI.FocusControl(null);
    }

    private void FrSetMaxChars(LocEntry e, int value)
    {
        if (e.maxChars == value) return;
        e.maxChars = value;
        LocalizationSheetIO.SaveSheet(_table, e.sheet);
    }

    /// <summary>Writes every "limit" decision whose key now exists in a sheet. Used after a Scan adds the missing keys.</summary>
    private int FrSyncLimits()
    {
        if (_frStore == null) _frStore = FitReviewStore.Load();
        var sheets = new HashSet<string>();
        foreach (var d in _frStore.items)
        {
            if (d.decision != FitReviewStore.Limit || d.limit <= 0 || !_table.TryGet(d.key, out var e)) continue;
            if (d.sourceHash != LocalizationTable.Hash(e.source) || e.maxChars == d.limit) continue;
            e.maxChars = d.limit;
            sheets.Add(e.sheet);
        }
        if (sheets.Count > 0) LocalizationSheetIO.SaveSheets(_table, sheets);
        return sheets.Count;
    }

    // ---------------------------------------------------------------- rendering

    private void FrEnsureRig()
    {
        if (_frPru == null)
        {
            _frPru = new PreviewRenderUtility();
            _frPru.camera.orthographic = true;
            _frPru.camera.clearFlags = CameraClearFlags.SolidColor;
            _frPru.camera.backgroundColor = new Color(0.13f, 0.13f, 0.15f);
            _frPru.camera.nearClipPlane = 0.01f;
            _frPru.camera.farClipPlane = 50f;
        }
        if (_frGo == null)
        {
            _frGo = EditorUtility.CreateGameObjectWithHideFlags("LocFitReviewText", HideFlags.HideAndDontSave, typeof(TextMeshPro));
            _frTmp = _frGo.GetComponent<TextMeshPro>();
            _frTmp.isOrthographic = true;
            _frTmp.color = Color.white;
            _frPru.AddSingleGO(_frGo);
        }
    }

    private void DisposeFitReview()
    {
        if (_frEnTex != null) DestroyImmediate(_frEnTex);
        if (_frPsTex != null) DestroyImmediate(_frPsTex);
        if (_frGo != null) DestroyImmediate(_frGo);
        if (_frPru != null) _frPru.Cleanup();
        _frEnTex = _frPsTex = null;
        _frGo = null;
        _frTmp = null;
        _frPru = null;
        _frTexKey = null;
    }

    /// <summary>Renders the text with the original element's settings (auto-size, wrap, overflow) inside a box of its size.</summary>
    private Texture2D FrRender_(CharFitItem it, string text, out FrRender info)
    {
        FrEnsureRig();
        var t = _frTmp;
        t.isOrthographic = it.isOrthographic;
        t.font = it.font != null ? it.font : TMP_Settings.defaultFontAsset;
        t.fontSharedMaterial = it.material != null && it.font != null ? it.material : t.font.material;
        t.fontStyle = it.fontStyle;
        t.alignment = it.alignment;
        t.textWrappingMode = it.wrapping;
        t.overflowMode = it.overflow;
        t.characterSpacing = it.characterSpacing;
        t.wordSpacing = it.wordSpacing;
        t.lineSpacing = it.lineSpacing;
        t.paragraphSpacing = it.paragraphSpacing;
        t.margin = it.margin;
        t.enableAutoSizing = it.autoSize;
        t.fontSizeMin = it.fontSizeMin;
        t.fontSizeMax = it.fontSizeMax;
        t.fontSize = it.autoSize ? it.fontSizeMax : it.fontSize;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = it.box;
        t.rectTransform.position = Vector3.zero;
        t.text = text;
        t.ForceMeshUpdate(true, true);

        info = new FrRender { size = t.fontSize };

        // Does the text fit at the size TMP settled on? Measured with auto-size off at that size (same render).
        t.enableAutoSizing = false;
        t.fontSize = info.size;
        float aw = it.box.x - it.margin.x - it.margin.z, ah = it.box.y - it.margin.y - it.margin.w;
        bool wrap = it.wrapping == TextWrappingModes.Normal || it.wrapping == TextWrappingModes.PreserveWhitespace;
        var pref = t.GetPreferredValues(text, wrap ? aw : Mathf.Infinity, Mathf.Infinity);
        float tol = Mathf.Max(0.01f, it.box.y * 0.01f);
        bool fits = pref.x <= aw + tol && pref.y <= ah + tol;
        info.overflow = !fits && it.overflow == TextOverflowModes.Overflow;
        info.cut = !fits && it.overflow != TextOverflowModes.Overflow;
        t.ForceMeshUpdate(true, true);

        var b = t.textBounds;
        bool hasText = t.textInfo != null && t.textInfo.characterCount > 0 && b.size.x > 0f;

        // Frame the box and anything spilling out of it.
        float ex = it.box.x * 0.5f, ey = it.box.y * 0.5f;
        if (hasText)
        {
            ex = Mathf.Max(ex, Mathf.Abs(b.min.x), Mathf.Abs(b.max.x));
            ey = Mathf.Max(ey, Mathf.Abs(b.min.y), Mathf.Abs(b.max.y));
        }
        float aspect = FrTexW / (float)FrTexH;
        float half = Mathf.Max(ey * 1.2f, ex * 1.1f / aspect, 0.001f);
        _frPru.camera.orthographicSize = half;
        _frPru.camera.transform.position = new Vector3(0, 0, -10);
        _frPru.camera.transform.rotation = Quaternion.identity;
        info.ppu = FrTexH / (2f * half);

        _frPru.BeginStaticPreview(new Rect(0, 0, FrTexW, FrTexH));
        _frPru.camera.Render();
        return _frPru.EndStaticPreview();
    }

    private void FrEnsureTextures(CharFitItem it)
    {
        if (it == null || _frTexKey == it.key) return;
        if (_frEnTex != null) DestroyImmediate(_frEnTex);
        if (_frPsTex != null) DestroyImmediate(_frPsTex);
        _frEnTex = FrRender_(it, it.source, out _frEn);
        _frPsTex = FrRender_(it, it.pseudo, out _frPs);
        _frTexKey = it.key;
    }

    // ---------------------------------------------------------------- source ping

    private static void FrSelectSource(CharFitItem it)
    {
        int slash = it.path.IndexOf('/');
        string rootName = slash < 0 ? it.path : it.path.Substring(0, slash);
        string rest = slash < 0 ? "" : it.path.Substring(slash + 1);
        GameObject root = null;

        if (it.asset.EndsWith(".unity"))
        {
            var scene = SceneManager.GetSceneByPath(it.asset);
            if (scene.IsValid()) root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
        }
        else
        {
            var stage = PrefabStageUtility.OpenPrefab(it.asset);
            if (stage != null) root = stage.prefabContentsRoot;
        }
        if (root == null) { EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(it.asset)); return; }

        var target = rest.Length == 0 ? root.transform : root.transform.Find(rest);
        if (target == null) return;
        Selection.activeGameObject = target.gameObject;
        EditorGUIUtility.PingObject(target.gameObject);
    }

    // ---------------------------------------------------------------- UI

    private void DrawFitReviewTab()
    {
        if (_frStore == null) _frStore = FitReviewStore.Load();

        LocUI.Section("Fit Review", "Step through every localized text whose pseudo version doesn't fit. Approve it if it still reads fine, or give it a character limit (max_chars).");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (LocUI.Button(_fr == null ? "Scan" : "Rescan", true, GUILayout.Width(90))) { FrScan(); GUIUtility.ExitGUI(); }
            GUILayout.Space(8);
            EditorGUIUtility.labelWidth = 92;
            _frReadable = EditorGUILayout.Slider(new GUIContent("Readable floor", "Smallest size, as a share of the design size, still considered readable. Limits are measured at this size."), _frReadable, 0.3f, 1f, GUILayout.Width(260));
            GUILayout.Space(8);
            _frBelow = EditorGUILayout.Slider(new GUIContent("Flag below", "Also list texts that fit only when pseudo shrinks below this share of the design size."), _frBelow, 0.3f, 1f, GUILayout.Width(260));
            EditorGUIUtility.labelWidth = 0;
            GUILayout.FlexibleSpace();
            if (LocUI.Button("Sync limits to sheets", false, GUILayout.Width(150)))
            {
                int n = FrSyncLimits();
                _status = n > 0 ? "Wrote reviewed limits to " + n + " sheet(s)." : "All reviewed limits are already in the sheets (or their keys aren't scanned yet).";
            }
        }

        if (_fr == null)
        {
            LocUI.Hint("Press Scan to measure every localized text under Assets/_GoodCopBadCop and the loaded scenes. It takes a little while (it opens each prefab). Decisions you make are saved to " + FitReviewStore.FilePath + " and survive rescans.");
            return;
        }
        if (!string.IsNullOrEmpty(_frScanInfo)) LocUI.Muted_(_frScanInfo);

        // Filter chips with counts.
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (FrFilter f in System.Enum.GetValues(typeof(FrFilter)))
            {
                int count = _fr.Count(i => FrMatches(i, f));
                string label = (f == FrFilter.ToReview ? "To review" : f == FrFilter.Limited ? "Limit set" : f.ToString()) + "  " + count;
                Color c = f == FrFilter.ToReview ? LocUI.Warn : f == FrFilter.Approved ? LocUI.Ok : f == FrFilter.Limited ? LocUI.Violet : LocUI.Neutral;
                if (LocUI.Chip(_frFilter == f, label, c) && _frFilter != f) { _frFilter = f; _frIndex = 0; FrRebuildView(); }
            }
            GUILayout.FlexibleSpace();
        }

        var it = FrCurrent;
        if (it == null)
        {
            LocUI.Hint(_frFilter == FrFilter.ToReview ? "Nothing left to review." : "No texts in this list.");
            return;
        }

        FrHandleKeys(it);
        FrEnsureTextures(it);
        var d = FrDecision(it);
        _table.TryGet(it.key, out var entry);
        if (_frLimitKey != it.key)
        {
            _frLimitKey = it.key;
            _frLimitEdit = d != null && d.decision == FitReviewStore.Limit ? d.limit : entry != null && entry.maxChars > 0 ? entry.maxChars : it.limit;
        }

        // Navigation and identity.
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("< Prev", EditorStyles.miniButtonLeft, GUILayout.Width(60))) FrMove(-1);
            GUILayout.Label((_frIndex + 1) + " / " + _frView.Count, EditorStyles.miniButtonMid, GUILayout.Width(70));
            if (GUILayout.Button("Next >", EditorStyles.miniButtonRight, GUILayout.Width(60))) FrMove(1);
            GUILayout.Space(8);
            EditorGUILayout.SelectableLabel(it.key, EditorStyles.boldLabel, GUILayout.Height(18), GUILayout.ExpandWidth(true));
            LocUI.Pill(entry != null ? entry.sheet : "NOT IN SHEET", entry != null ? LocUI.Neutral : LocUI.Warn);
            if (it.enOver) LocUI.Pill("EN TOO TIGHT", LocUI.Bad);
            if (d != null) LocUI.Pill(d.decision == FitReviewStore.Approved ? "FITS" : "LIMIT " + d.limit, d.decision == FitReviewStore.Approved ? LocUI.Ok : LocUI.Violet);
            if (GUILayout.Button("Select source", EditorStyles.miniButton, GUILayout.Width(90))) FrSelectSource(it);
        }
        LocUI.Muted_(it.asset + "  :  " + it.path);

        // Side-by-side previews.
        float colW = (position.width - 56f) * 0.5f;
        using (new EditorGUILayout.HorizontalScope())
        {
            FrDrawPreview("English", it, it.source, _frEnTex, _frEn, colW);
            GUILayout.Space(8);
            FrDrawPreview("Pseudo", it, it.pseudo, _frPsTex, _frPs, colW);
        }

        // Facts.
        string sizing = it.autoSize ? "auto " + it.fontSizeMin.ToString("0.#") + "-" + it.fontSizeMax.ToString("0.#") + " pt" : it.fontSize.ToString("0.#") + " pt fixed";
        LocUI.Muted_("Box " + it.box.x.ToString("0") + " x " + it.box.y.ToString("0") + (it.layoutLine ? " (layout, one line)" : "") +
                     "  |  " + sizing + "  |  " + it.wrapping + ", " + it.overflow +
                     "  |  readable floor " + it.floorSize.ToString("0.#") + " pt (" + (it.floorRatio * 100f).ToString("0") + "%)" +
                     "  |  chars: English " + it.enLen + ", pseudo " + it.pseudoLen + ", fit at floor " + it.limit +
                     (entry != null && entry.maxChars > 0 ? "  |  current max_chars " + entry.maxChars : ""));

        // Decision.
        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = LocUI.Ok;
            if (GUILayout.Button(new GUIContent("Fits (A)", "Readable as is. No character limit."), GUILayout.Height(28), GUILayout.Width(120))) FrDecide(it, FitReviewStore.Approved);
            GUI.backgroundColor = bg;
            GUILayout.Space(16);
            GUILayout.Label("Max chars", GUILayout.Width(66));
            _frLimitEdit = Mathf.Max(1, EditorGUILayout.IntField(_frLimitEdit, GUILayout.Width(56), GUILayout.Height(20)));
            if (GUILayout.Button("Measured " + it.limit, EditorStyles.miniButton, GUILayout.Width(96))) { _frLimitEdit = it.limit; GUI.FocusControl(null); }
            if (GUILayout.Button("English " + it.enLen, EditorStyles.miniButton, GUILayout.Width(86))) { _frLimitEdit = it.enLen; GUI.FocusControl(null); }
            GUI.backgroundColor = LocUI.Violet;
            if (GUILayout.Button(new GUIContent("Set limit (L)", "Too small: store this many characters as the key's max_chars."), GUILayout.Height(28), GUILayout.Width(120))) FrDecide(it, FitReviewStore.Limit);
            GUI.backgroundColor = bg;
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(d == null))
                if (GUILayout.Button(new GUIContent("Reset (U)", "Back to 'to review'. Removes a limit this review set."), GUILayout.Height(28), GUILayout.Width(90))) FrDecide(it, null);
        }
        LocUI.Muted_("Keys: Left / Right to move, A = fits, L = set limit, U = reset. Click an empty area first if a number field has focus.");
    }

    private void FrDrawPreview(string title, CharFitItem it, string text, Texture2D tex, FrRender info, float width)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                float pct = it.design > 0f ? info.size / it.design * 100f : 100f;
                string state = info.cut ? "cut off" : info.overflow ? "spills out" : "fits";
                Color c = info.cut || info.overflow ? LocUI.Bad : info.size < it.floorSize - 0.05f ? LocUI.Warn : LocUI.Ok;
                LocUI.Pill(state + " at " + info.size.ToString("0.#") + " pt (" + pct.ToString("0") + "%)", c);
            }

            Rect r = GUILayoutUtility.GetAspectRect(FrTexW / (float)FrTexH, GUILayout.Width(width));
            if (tex != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
                float k = info.ppu * (r.width / FrTexW);
                var box = new Rect(r.center.x - it.box.x * k * 0.5f, r.center.y - it.box.y * k * 0.5f, it.box.x * k, it.box.y * k);
                Handles.BeginGUI();
                Handles.DrawSolidRectangleWithOutline(box, Color.clear, info.overflow || info.cut ? new Color(1f, 0.4f, 0.35f, 0.95f) : new Color(1f, 0.8f, 0.2f, 0.9f));
                Handles.EndGUI();
            }
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void FrHandleKeys(CharFitItem it)
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
        switch (e.keyCode)
        {
            case KeyCode.LeftArrow: FrMove(-1); break;
            case KeyCode.RightArrow: FrMove(1); break;
            case KeyCode.A: FrDecide(it, FitReviewStore.Approved); break;
            case KeyCode.L: FrDecide(it, FitReviewStore.Limit); break;
            case KeyCode.U: if (FrDecision(it) != null) FrDecide(it, null); break;
            default: return;
        }
        e.Use();
        Repaint();
    }
}
