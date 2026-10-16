using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Scan and Strings tabs.
public partial class LocalizationWindow
{
    private static Color StatusColor(string status)
    {
        switch (status)
        {
            case "frozen": return LocUI.Neutral;
            case "changed": return LocUI.Warn;
            default: return LocUI.Accent;
        }
    }

    private static Color GroupColor(string group)
    {
        switch (group)
        {
            case LocGroups.SuspectObjects: return LocUI.Violet;
            case LocGroups.SuspectData: return new Color(0.25f, 0.65f, 0.70f);
            case LocGroups.ConversationData: return new Color(0.85f, 0.45f, 0.60f);
            default: return LocUI.Neutral;
        }
    }

    // ================================================================ Scan tab

    private void DrawScanTab()
    {
        using (LocUI.Card())
        {
            LocUI.Muted_("Finds TMP labels, dialogue in data assets and dialogue coded on suspect objects. Nothing is merged automatically.");
            using (new EditorGUILayout.HorizontalScope())
            {
                _scanScenes = LocUI.Chip(_scanScenes, "Loaded scenes");
                _scanPrefabs = LocUI.Chip(_scanPrefabs, "Prefabs");
                _scanSOs = LocUI.Chip(_scanSOs, "Dialogue data assets");
                _scanSuspectObjects = LocUI.Chip(_scanSuspectObjects, "Suspect objects", LocUI.Violet);
                GUILayout.FlexibleSpace();
                if (LocUI.Button("Scan project", true, GUILayout.Width(110)))
                {
                    _scan = LocalizationScanner.Scan(_settings, _table, _scanScenes, _scanPrefabs, _scanSOs, _scanSuspectObjects);
                    LocBindings.ClearGroupCache();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var types = _settings != null && _settings.dialogueAssetTypes != null ? string.Join(", ", _settings.dialogueAssetTypes) : "";
                LocUI.Muted_("Data assets scanned: " + types + " (SuspectData: entry reasons, dialogues, answers, barks only). Edit in Fonts > All localization settings.");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Remove excluded data strings...", "Deletes strings already in the sheets whose only source is a ScriptableObject type not in the list, or a field outside that type's allowlist (e.g. SuspectData descriptions)."), GUILayout.Width(220)))
                {
                    var keys = LocalizationScanner.NonDialogueDataKeys(_table, _settings);
                    if (keys.Count == 0) _status = "No strings from excluded data assets or fields in the sheets.";
                    else EditorApplication.delayCall += () => { ConfirmDelete(keys); Repaint(); };
                }
            }
        }

        if (_scan == null) { LocUI.Hint("Press <b>Scan project</b> to build the review list."); return; }

        int unloc = _scan.Count(i => i.state == ScanState.Unlocalized);
        int tmpUnloc = _scan.Count(i => i.state == ScanState.Unlocalized && i.binding.kind == "TMP");
        using (new EditorGUILayout.HorizontalScope())
        {
            LocUI.StatTile("candidates", _scan.Count.ToString("N0"), LocUI.Accent, 130);
            LocUI.StatTile("not localized", unloc.ToString("N0"), LocUI.Warn, 130);
            LocUI.StatTile("hardcoded TMP labels", tmpUnloc.ToString("N0"), LocUI.Bad, 160);
            LocUI.StatTile("selected", _scan.Count(i => i.selected).ToString("N0"), LocUI.Ok, 130);
            GUILayout.FlexibleSpace();
        }

        using (LocUI.Card())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _scanFilter = EditorGUILayout.TextField(_scanFilter, EditorStyles.toolbarSearchField);
                _scanGroup = LocGroups.FilterOptions[EditorGUILayout.Popup(GroupIndex(_scanGroup), LocGroups.FilterOptions, GUILayout.Width(170))];
                _showLocalized = LocUI.Chip(_showLocalized, "Show localized", LocUI.Ok);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all new (visible)", GUILayout.Width(160))) foreach (var i in VisibleScan()) i.selected = i.state != ScanState.Localized;
                if (GUILayout.Button("Select none", GUILayout.Width(100))) foreach (var i in _scan) i.selected = false;
                GUILayout.FlexibleSpace();
                if (LocUI.Button("Apply selected", true, GUILayout.Width(130)))
                {
                    int n = LocalizationScanner.Apply(_scan, _table);
                    _status = "Applied " + n + " item(s). Sheets saved, LocalizedText attached.";
                    _scan = null;
                    _filterDirty = true;
                    return;
                }
            }
        }

        var shown = VisibleScan().Take(300).ToList();
        _scanScroll = EditorGUILayout.BeginScrollView(_scanScroll);
        foreach (var item in shown)
        {
            Color accent = item.state == ScanState.Unlocalized ? LocUI.Accent : item.state == ScanState.SourceChanged ? LocUI.Warn : LocUI.Ok;
            using (LocUI.Card(accent))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(18));
                    LocUI.Pill(item.state == ScanState.Unlocalized ? "NEW" : item.state == ScanState.SourceChanged ? "CHANGED" : "OK", accent);
                    LocUI.Pill(item.group, GroupColor(item.group));
                    LocUI.Pill(item.sheet, LocUI.Neutral);
                    GUILayout.FlexibleSpace();
                }
                if (item.state == ScanState.Unlocalized) item.key = EditorGUILayout.TextField(item.key);
                else EditorGUILayout.LabelField(item.key, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(item.text.Replace("\n", "  \u21B5  "), EditorStyles.wordWrappedLabel);
                LocUI.Muted_(item.notes);
                if (item.reuseKey != null)
                    item.reuse = EditorGUILayout.ToggleLeft("Same text and context as '" + item.reuseKey + "': reuse that key (true duplicate)", item.reuse);
            }
        }
        EditorGUILayout.EndScrollView();
        if (_scan.Count > shown.Count && shown.Count >= 300) LocUI.Muted_("Showing the first 300. Use the filters to narrow.");
    }

    private IEnumerable<ScanItem> VisibleScan()
    {
        return _scan.Where(i => (_showLocalized || i.state != ScanState.Localized)
                                && LocGroups.Matches(_scanGroup, i.group)
                                && (string.IsNullOrEmpty(_scanFilter)
                                    || i.key.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.text.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.sheet.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0));
    }

    // ================================================================ Strings tab

    private void DrawStringsTab()
    {
        var sheets = new List<string> { AllSheets };
        sheets.AddRange(_table.SheetNames());

        using (LocUI.Card())
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                int idx = Mathf.Max(0, sheets.IndexOf(_sheetFilter));
                idx = EditorGUILayout.Popup(idx, sheets.Select(s => s == AllSheets ? "All sheets" : s).ToArray(), GUILayout.Width(190));
                _sheetFilter = sheets[idx];
                _sourceGroup = LocGroups.FilterOptions[EditorGUILayout.Popup(GroupIndex(_sourceGroup), LocGroups.FilterOptions, GUILayout.Width(170))];
                var langs = new List<string> { "" };
                langs.AddRange(_table.languages);
                int li = Mathf.Max(0, langs.IndexOf(_fLang));
                li = EditorGUILayout.Popup(li, langs.Select(l => string.IsNullOrEmpty(l) ? "Any language" : l).ToArray(), GUILayout.Width(120));
                _fLang = langs[li];
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _fUntranslated = LocUI.Chip(_fUntranslated, "Untranslated", LocUI.Warn);
                _fChanged = LocUI.Chip(_fChanged, "New / changed");
                _fFrozen = LocUI.Chip(_fFrozen, "Frozen", LocUI.Neutral);
                _fOverflow = LocUI.Chip(_fOverflow, "Pseudo overflow", LocUI.Bad);
                _showPseudoColumns = LocUI.Chip(_showPseudoColumns, "Pseudo columns", LocUI.Violet);
                GUILayout.FlexibleSpace();
            }
            if (EditorGUI.EndChangeCheck()) { _page = 0; _filterDirty = true; }
        }

        if (_filterDirty) RebuildFilter();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(_filtered.Count.ToString("N0") + " strings", EditorStyles.boldLabel, GUILayout.Width(110));
            GUI.enabled = _page > 0;
            if (GUILayout.Button("\u25C0", GUILayout.Width(30))) _page--;
            GUI.enabled = (_page + 1) * PageSize < _filtered.Count;
            if (GUILayout.Button("\u25B6", GUILayout.Width(30))) _page++;
            GUI.enabled = true;
            GUILayout.Label("Page " + (_page + 1) + " / " + Mathf.Max(1, Mathf.CeilToInt(_filtered.Count / (float)PageSize)), GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.Label("New language", LocUI.MutedStyle, GUILayout.Width(80));
            _newLangCode = EditorGUILayout.TextField(_newLangCode, GUILayout.Width(70));
            if (GUILayout.Button("Add", GUILayout.Width(44))) AddLanguagePrompt();
        }

        DrawDeleteToolbar();

        var cols = new List<string>(_table.languages.Where(l => _showPseudoColumns || !PseudoLocalizer.IsPseudo(l)));
        var pseudoCols = _showPseudoColumns ? PseudoLocalizer.Codes.ToList() : new List<string>();

        _stringsScroll = EditorGUILayout.BeginScrollView(_stringsScroll);
        foreach (var e in _filtered.Skip(_page * PageSize).Take(PageSize).ToList())
        {
            string status = LocalizationSheetIO.Status(e);
            string group = LocBindings.GroupOf(e.key);
            using (LocUI.Card(StatusColor(status)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool sel = _selectedForDelete.Contains(e.key);
                    bool nsel = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
                    if (nsel != sel) { if (nsel) _selectedForDelete.Add(e.key); else _selectedForDelete.Remove(e.key); }
                    EditorGUILayout.SelectableLabel(e.key, EditorStyles.boldLabel, GUILayout.Height(18), GUILayout.ExpandWidth(true));
                    LocUI.Pill(status.ToUpperInvariant(), StatusColor(status));
                    LocUI.Pill(group, GroupColor(group));
                    LocUI.Pill(e.sheet, LocUI.Neutral);
                    if (GUILayout.Button("Ping", EditorStyles.miniButtonLeft, GUILayout.Width(44))) LocalizationApply.Ping(e.key);
                    if (GUILayout.Button("Preview", EditorStyles.miniButtonMid, GUILayout.Width(60))) { _selectedKey = e.key; _tab = 2; }
                    if (GUILayout.Button("Delete", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                    {
                        string k = e.key;
                        EditorApplication.delayCall += () => { ConfirmDelete(new List<string> { k }); Repaint(); };
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(150)))
                    {
                        GUILayout.Label("Notes", LocUI.MutedStyle);
                        EditorGUI.BeginChangeCheck();
                        string notes = EditorGUILayout.TextArea(e.notes ?? "", GUILayout.MinHeight(38));
                        if (EditorGUI.EndChangeCheck()) { e.notes = notes; MarkDirty(e); }
                    }

                    using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(170), GUILayout.ExpandWidth(true)))
                    {
                        GUILayout.Label("English source" + (status == "changed" ? "  (edited after freeze: goes into the next delta)" : ""), LocUI.MutedStyle);
                        GUI.SetNextControlName("src:" + e.key);
                        EditorGUI.BeginChangeCheck();
                        string src = EditorGUILayout.TextArea(e.source ?? "", GUILayout.MinHeight(38));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (_pendingSourceKey != null && _pendingSourceKey != e.key) CommitPendingSource();
                            e.source = src;
                            _pendingSourceKey = e.key;
                            MarkDirty(e);
                        }
                    }

                    foreach (var lang in cols)
                    {
                        using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(170), GUILayout.ExpandWidth(true)))
                        {
                            string val = e.GetValue(lang);
                            bool missing = string.IsNullOrEmpty(val);
                            GUILayout.Label(lang + (missing ? "  (missing)" : ""), LocUI.MutedStyle);
                            var prev = GUI.backgroundColor;
                            if (missing) GUI.backgroundColor = new Color(1f, 0.82f, 0.45f);
                            EditorGUI.BeginChangeCheck();
                            string nv = EditorGUILayout.TextArea(val, GUILayout.MinHeight(38));
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (string.IsNullOrEmpty(nv)) e.values.Remove(lang); else e.values[lang] = nv;
                                MarkDirty(e);
                            }
                            GUI.backgroundColor = prev;
                        }
                    }

                    foreach (var code in pseudoCols)
                    {
                        using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(170), GUILayout.ExpandWidth(true)))
                        {
                            GUILayout.Label(code + "  (generated)", LocUI.MutedStyle);
                            EditorGUILayout.SelectableLabel(PseudoLocalizer.Generate(code, e.source, e.key, _settings.pseudoExpansion), EditorStyles.textArea, GUILayout.MinHeight(38));
                        }
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
}
