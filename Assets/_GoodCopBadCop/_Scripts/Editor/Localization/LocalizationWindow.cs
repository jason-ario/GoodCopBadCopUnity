using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Localization viewer and editor: Scan, Strings, Preview, Import / Export, Findings, Freeze / Delta and Fonts.
/// Split across partial files, one per tab.
/// </summary>
public partial class LocalizationWindow : EditorWindow
{
    private static readonly string[] TabNames = { "Scan", "Strings", "Preview", "Import / Export", "Findings", "Freeze / Delta", "Fonts", "Stats", "Text Fit" };

    private LocalizationTable _table;
    private LocalizationSettings _settings;
    private int _tab;
    private readonly HashSet<string> _dirtySheets = new HashSet<string>();
    private string _pendingSourceKey;
    private string _lastFocus = "";
    private string _status = "";

    [MenuItem("Tools/Localization")]
    public static void Open()
    {
        var w = GetWindow<LocalizationWindow>("Localization");
        w.minSize = new Vector2(900, 520);
        w.Show();
    }

    private void OnEnable()
    {
        Reload();
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        CommitPendingSource();
        SaveDirty();
        DisposePreview();
    }

    private void OnLostFocus()
    {
        CommitPendingSource();
        SaveDirty();
    }

    private void Reload()
    {
        _settings = LocalizationSheetIO.LoadOrCreateSettings();
        _table = LocalizationSheetIO.LoadAll();
        LocBindings.Load();
        _dirtySheets.Clear();
        _filterDirty = true;
        _stats = null;
        LoadChecklist();
        if (string.IsNullOrEmpty(_sheetFilter) || (_sheetFilter != AllSheets && !_table.SheetNames().Contains(_sheetFilter)))
            _sheetFilter = AllSheets;
    }

    private void SaveDirty()
    {
        if (_table == null) return;
        foreach (var sheet in _dirtySheets) LocalizationSheetIO.SaveSheet(_table, sheet);
        if (_dirtySheets.Count > 0) AssetDatabase.Refresh();
        _dirtySheets.Clear();
    }

    private void MarkDirty(LocEntry e) { _dirtySheets.Add(e.sheet); }

    private void OnGUI()
    {
        if (_table == null) Reload();
        LocUI.EnsureStyles();

        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(0, 0, position.width, position.height), LocUI.Solid(LocUI.PageBg));

        DrawHeader();
        DrawQuickStart();

        int newTab = LocUI.Tabs(_tab, TabNames);
        if (newTab != _tab) { _tab = newTab; _status = ""; _stats = null; GUI.FocusControl(null); }

        using (new EditorGUILayout.VerticalScope(LocUI.BodyStyle, GUILayout.ExpandHeight(true)))
        {
            switch (_tab)
            {
                case 0: DrawScanTab(); break;
                case 1: DrawStringsTab(); break;
                case 2: DrawPreviewTab(); break;
                case 3: DrawImportExportTab(); break;
                case 4: DrawFindingsTab(); break;
                case 5: DrawFreezeTab(); break;
                case 6: DrawFontsTab(); break;
                case 7: DrawStatsTab(); break;
                case 8: DrawTextFitTab(); break;
            }
        }

        if (!string.IsNullOrEmpty(_status))
        {
            bool bad = _status.IndexOf("fail", System.StringComparison.OrdinalIgnoreCase) >= 0 || _status.IndexOf("error", System.StringComparison.OrdinalIgnoreCase) >= 0;
            LocUI.StatusBar(_status, bad ? LocUI.Bad : LocUI.Accent);
        }

        HandleFocusChange();
    }

    private void DrawHeader()
    {
        int sheets = _table.SheetNames().Count();
        string runtime = Application.isPlaying ? "  |  runtime: " + Loc.CurrentLanguage : "";
        string dirty = _dirtySheets.Count > 0 || _pendingSourceKey != null ? "  |  unsaved changes" : "";
        var r = LocUI.Header("Localization", $"{_table.entries.Count:N0} strings  |  {sheets} sheets  |  {_table.languages.Count(l => !PseudoLocalizer.IsPseudo(l)) + 1} languages{runtime}{dirty}");

        float x = r.xMax - 8;
        if (LocUI.HeaderButton(ref x, r, "Build runtime table", true, 140))
        {
            CommitPendingSource(); SaveDirty();
            LocalizationSheetIO.BuildRuntime(_table);
            _status = "Wrote " + LocalizationPaths.RuntimeStrings + " (" + _table.entries.Count + " entries).";
        }
        if (LocUI.HeaderButton(ref x, r, "Save", false, 56)) { CommitPendingSource(); SaveDirty(); _status = "Saved sheets."; }
        if (LocUI.HeaderButton(ref x, r, "Reload", false, 62)) { SaveDirty(); Reload(); }
        if (LocUI.HeaderButton(ref x, r, "? Guide", false, 66)) LocalizationGuideWindow.Open();
    }

    /// <summary>Commits source edits to the origin asset, and sheet edits to disk, when the focused control changes.</summary>
    private void HandleFocusChange()
    {
        if (Event.current.type != EventType.Repaint) return;
        string focus = GUI.GetNameOfFocusedControl();
        if (focus == _lastFocus) return;
        _lastFocus = focus;
        if (_pendingSourceKey != null && focus != "src:" + _pendingSourceKey) CommitPendingSource();
        if (_dirtySheets.Count > 0)
            foreach (var s in _dirtySheets) LocalizationSheetIO.SaveSheet(_table, s);
    }

    private void CommitPendingSource()
    {
        if (_pendingSourceKey == null || _table == null) return;
        string key = _pendingSourceKey;
        _pendingSourceKey = null;
        if (_table.TryGet(key, out var e))
        {
            int n = LocalizationApply.WriteSource(key, e.source);
            _status = n > 0 ? "Source of '" + key + "' written to " + n + " origin(s)." : "'" + key + "' has no origin recorded; CSV updated only.";
            MarkDirty(e);
        }
    }

    // ================================================================ Scan tab

    private bool _scanScenes = true, _scanPrefabs = true, _scanSOs = true, _scanSuspectObjects = true, _showLocalized;
    private string _scanGroup = LocGroups.FilterAll;
    private List<ScanItem> _scan;
    private Vector2 _scanScroll;
    private string _scanFilter = "";

    private static int GroupIndex(string filter)
    {
        return Mathf.Max(0, System.Array.IndexOf(LocGroups.FilterOptions, filter));
    }



    // ================================================================ Strings tab

    private const string AllSheets = "(all)";
    private string _sheetFilter = AllSheets;
    private string _sourceGroup = LocGroups.FilterAll;
    private string _search = "";
    private bool _fUntranslated, _fChanged, _fFrozen, _fOverflow, _showPseudoColumns;
    private string _fLang = "";
    private int _page;
    private const int PageSize = 40;
    private bool _filterDirty = true;
    private List<LocEntry> _filtered = new List<LocEntry>();
    private Vector2 _stringsScroll;
    private string _selectedKey;
    private readonly HashSet<string> _overflowKeys = new HashSet<string>();

    private void RebuildFilter()
    {
        _filterDirty = false;
        IEnumerable<LocEntry> q = _table.entries;
        if (_sheetFilter != AllSheets) q = q.Where(e => e.sheet == _sheetFilter);
        if (_sourceGroup != LocGroups.FilterAll) q = q.Where(e => LocGroups.Matches(_sourceGroup, LocBindings.GroupOf(e.key)));
        if (!string.IsNullOrEmpty(_search))
            q = q.Where(e => Contains(e.key, _search) || Contains(e.source, _search) || Contains(e.notes, _search)
                             || e.values.Values.Any(v => Contains(v, _search)));
        if (_fUntranslated)
        {
            var langs = string.IsNullOrEmpty(_fLang) ? _table.languages.Where(l => !PseudoLocalizer.IsPseudo(l)).ToList() : new List<string> { _fLang };
            q = q.Where(e => langs.Any(l => string.IsNullOrEmpty(e.GetValue(l))));
        }
        if (_fChanged) q = q.Where(e => LocalizationSheetIO.Status(e) == "changed" || LocalizationSheetIO.Status(e) == "new");
        if (_fFrozen) q = q.Where(e => LocalizationSheetIO.Status(e) == "frozen");
        if (_fOverflow) q = q.Where(e => _overflowKeys.Contains(e.key));
        _filtered = q.ToList();
        _page = Mathf.Clamp(_page, 0, Mathf.Max(0, (_filtered.Count - 1) / PageSize));
    }

    private static bool Contains(string s, string needle)
    {
        return !string.IsNullOrEmpty(s) && s.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }


    private string _newLangCode = "";

    private void AddLanguagePrompt()
    {
        string code = (_newLangCode ?? "").Trim();
        if (string.IsNullOrEmpty(code) || PseudoLocalizer.IsPseudo(code))
        {
            _status = "Type a language code (for example fr) in the box next to the button.";
            return;
        }
        _table.AddLanguage(code);
        foreach (var s in _table.SheetNames()) _dirtySheets.Add(s);
        SaveDirty();
        _status = "Added language column '" + code + "'.";
        _newLangCode = "";
        _filterDirty = true;
    }
}
