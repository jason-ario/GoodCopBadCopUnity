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
                if (_settings != null)
                {
                    using (new EditorGUI.DisabledScope(!_scanPrefabs))
                    {
                        bool onlyUsedPrefabs = LocUI.Chip(_settings.onlyUsedPrefabs, "Used prefabs only");
                        if (onlyUsedPrefabs != _settings.onlyUsedPrefabs)
                        {
                            _settings.onlyUsedPrefabs = onlyUsedPrefabs;
                            EditorUtility.SetDirty(_settings);
                        }
                    }
                }
                _scanSOs = LocUI.Chip(_scanSOs, "Dialogue data assets");
                if (_settings != null)
                {
                    bool onlyUsed = LocUI.Chip(_settings.onlyUsedDialogueAssets, "Used dialogue only");
                    if (onlyUsed != _settings.onlyUsedDialogueAssets)
                    {
                        _settings.onlyUsedDialogueAssets = onlyUsed;
                        EditorUtility.SetDirty(_settings);
                    }
                }
                _scanSuspectObjects = LocUI.Chip(_scanSuspectObjects, "Suspect objects", LocUI.Violet);
                _scanCode = LocUI.Chip(_scanCode, "Code strings");
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_scan == null))
                {
                    if (GUILayout.Button(new GUIContent("Clear results", "Discards the current scan review list (selections and edited keys). Sheets and assets are not touched."), GUILayout.Width(100)))
                        ClearScanResults();
                }
                if (LocUI.Button("Scan project", true, GUILayout.Width(110)))
                {
                    _scan = LocalizationScanner.Scan(_settings, _table, _scanScenes, _scanPrefabs, _scanSOs, _scanSuspectObjects, _scanCode);
                    LocBindings.ClearGroupCache();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var types = _settings != null && _settings.dialogueAssetTypes != null ? string.Join(", ", _settings.dialogueAssetTypes) : "";
                LocUI.Muted_("Data assets scanned: " + types + " (SuspectData: entry reasons, dialogues, answers, barks only). Edit in Fonts > All localization settings.");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("List unused dialogue", "Logs dialogue assets that no enabled build scene or Resources asset references. These are skipped while 'Used dialogue only' is on."), GUILayout.Width(150)))
                {
                    var unused = LocalizationScanner.UnusedDialogueAssets(_settings);
                    Debug.Log("[Localization] Unused dialogue assets (" + unused.Count + "):\n" + string.Join("\n", unused));
                    _status = unused.Count + " unused dialogue asset(s), listed in the Console.";
                }
                if (GUILayout.Button(new GUIContent("Remove excluded data strings...", "Deletes strings already in the sheets whose only source is a ScriptableObject type not in the list, a field outside that type's allowlist (e.g. SuspectData descriptions), or a dialogue asset the game never loads."), GUILayout.Width(220)))
                {
                    var keys = LocalizationScanner.NonDialogueDataKeys(_table, _settings);
                    if (keys.Count == 0) _status = "No strings from excluded data assets or fields in the sheets.";
                    else EditorApplication.delayCall += () => { ConfirmDelete(keys); Repaint(); };
                }
                if (GUILayout.Button(new GUIContent("Remove stale code strings...", "Deletes code.* strings whose English no longer appears in any Loc.T(\"...\") call (the literal was edited or removed)."), GUILayout.Width(200)))
                {
                    var keys = LocalizationScanner.StaleCodeKeys(_table);
                    if (keys.Count == 0) _status = "No stale code strings in the sheets.";
                    else EditorApplication.delayCall += () => { ConfirmDelete(keys); Repaint(); };
                }
            }
            LocUI.Muted_("Code strings: wrap player-facing literals in scripts as Loc.T(\"English\") or Loc.T(\"Day {0}\", day) so the scan finds them.");
        }

        if (_scan == null) { LocUI.Hint("Press <b>Scan project</b> to build the review list."); return; }

        int unloc = _scan.Count(i => i.state == ScanState.Unlocalized && !i.runtime);
        int tmpUnloc = _scan.Count(i => i.state == ScanState.Unlocalized && !i.runtime && i.binding.kind == "TMP");
        int runtimeCount = _scan.Count(i => i.runtime);
        int deletedCount = _scan.Count(i => i.state == ScanState.Deleted);
        int deletedSelected = _scan.Count(i => i.state == ScanState.Deleted && i.selected);
        using (new EditorGUILayout.HorizontalScope())
        {
            LocUI.StatTile("candidates", _scan.Count.ToString("N0"), LocUI.Accent, 130);
            LocUI.StatTile("not localized", unloc.ToString("N0"), LocUI.Warn, 130);
            LocUI.StatTile("hardcoded TMP labels", tmpUnloc.ToString("N0"), LocUI.Bad, 160);
            LocUI.StatTile("runtime-filled", runtimeCount.ToString("N0"), LocUI.Violet, 130);
            LocUI.StatTile("deleted", deletedCount.ToString("N0"), LocUI.Bad, 130);
            LocUI.StatTile("selected", _scan.Count(i => i.selected && i.state != ScanState.Deleted).ToString("N0"), LocUI.Ok, 130);
            GUILayout.FlexibleSpace();
        }

        using (LocUI.Card())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _scanFilter = EditorGUILayout.TextField(_scanFilter, EditorStyles.toolbarSearchField);
                _scanGroup = LocGroups.FilterOptions[EditorGUILayout.Popup(GroupIndex(_scanGroup), LocGroups.FilterOptions, GUILayout.Width(170))];
                _showLocalized = LocUI.Chip(_showLocalized, "Show localized", LocUI.Ok);
                _showRuntime = LocUI.Chip(_showRuntime, "Show runtime-filled", LocUI.Violet);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all new (visible)", GUILayout.Width(160))) foreach (var i in VisibleScan()) if (i.state != ScanState.Deleted) i.selected = i.state != ScanState.Localized && !i.runtime;
                if (GUILayout.Button("Select none", GUILayout.Width(100))) foreach (var i in _scan) i.selected = false;
                using (new EditorGUI.DisabledScope(runtimeCount == 0))
                {
                    if (GUILayout.Button(new GUIContent("Remove runtime placeholders...", "Deletes sheet rows that only runtime-filled labels use and clears the LocalizedText key on those labels (the component stays for language fonts)."), GUILayout.Width(200)))
                        EditorApplication.delayCall += () => { ConfirmRemoveRuntimePlaceholders(); Repaint(); };
                }
                using (new EditorGUI.DisabledScope(deletedSelected == 0))
                {
                    if (GUILayout.Button(new GUIContent("Remove selected deleted (" + deletedSelected + ")...", "Deletes the ticked DELETED rows from the sheets, with their translations and bindings."), GUILayout.Width(210)))
                    {
                        var gone = _scan.Where(i => i.state == ScanState.Deleted && i.selected).ToList();
                        EditorApplication.delayCall += () => { ConfirmRemoveDeleted(gone); Repaint(); };
                    }
                }
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
            bool deleted = item.state == ScanState.Deleted;
            Color accent = deleted ? LocUI.Bad : item.runtime ? LocUI.Violet : item.state == ScanState.Unlocalized ? LocUI.Accent : item.state == ScanState.SourceChanged ? LocUI.Warn : LocUI.Ok;
            using (LocUI.Card(accent))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(item.runtime))
                        item.selected = EditorGUILayout.Toggle(item.selected && !item.runtime, GUILayout.Width(18));
                    if (deleted) LocUI.Pill("DELETED", LocUI.Bad);
                    else if (item.runtime) LocUI.Pill("RUNTIME", LocUI.Violet);
                    else LocUI.Pill(item.state == ScanState.Unlocalized ? "NEW" : item.state == ScanState.SourceChanged ? "CHANGED" : "OK", accent);
                    LocUI.Pill(item.group, GroupColor(item.group));
                    LocUI.Pill(item.sheet, LocUI.Neutral);
                    GUILayout.FlexibleSpace();
                    if (deleted)
                    {
                        if (GUILayout.Button(new GUIContent("Remove", "Delete this row from its sheet, with its translations and binding."), EditorStyles.miniButton, GUILayout.Width(65)))
                        {
                            var one = new List<ScanItem> { item };
                            EditorApplication.delayCall += () => { ConfirmRemoveDeleted(one); Repaint(); };
                        }
                    }
                    else if (item.binding.kind == "TMP")
                    {
                        if (item.runtime)
                        {
                            if (GUILayout.Button(new GUIContent("Not runtime", "The authored text is real copy: scan it as normal and remember this label."), EditorStyles.miniButton, GUILayout.Width(80)))
                                SetRuntimeOverride(item, false);
                        }
                        else if (GUILayout.Button(new GUIContent("Runtime", "Code fills this label at runtime: treat the authored text as a placeholder and remember this label."), EditorStyles.miniButton, GUILayout.Width(65)))
                            SetRuntimeOverride(item, true);
                    }
                }
                if (deleted) LocUI.Muted_(item.deletedReason);
                else if (item.runtime) LocUI.Muted_("Filled at runtime by " + item.runtimeReason + ". The placeholder text is not localized.");
                else if (item.runtimeOverridden) LocUI.Muted_("Detected as runtime-filled, kept as copy (Not runtime).");
                if (item.state == ScanState.Unlocalized && !item.runtime) item.key = EditorGUILayout.TextField(item.key);
                else EditorGUILayout.LabelField(item.key, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(item.text.Replace("\n", "  \u21B5  "), EditorStyles.wordWrappedLabel);
                LocUI.Muted_(item.notes);
                if (item.reuseKey != null && !item.runtime && !deleted)
                    item.reuse = EditorGUILayout.ToggleLeft("Same text and context as '" + item.reuseKey + "': reuse that key (true duplicate)", item.reuse);
            }
        }
        EditorGUILayout.EndScrollView();
        if (_scan.Count > shown.Count && shown.Count >= 300) LocUI.Muted_("Showing the first 300. Use the filters to narrow.");
    }

    private void ClearScanResults()
    {
        if (_scan == null) return;
        int selected = _scan.Count(i => i.selected && i.state != ScanState.Deleted);
        if (selected > 0 && !EditorUtility.DisplayDialog("Clear scan results",
                "Discard the scan list? " + selected + " selected item(s) have not been applied.\n\nSheets and assets are not changed.",
                "Clear", "Cancel"))
            return;
        _scan = null;
        _scanScroll = Vector2.zero;
        _status = "Scan results cleared.";
        GUIUtility.ExitGUI();
    }

    /// <summary>Remembers the label as runtime-filled (or as real copy) in LocalizationSettings and updates the item in place.</summary>
    private void SetRuntimeOverride(ScanItem item, bool runtime)
    {
        if (_settings == null) return;
        string id = LocalizationScanner.RuntimeTextId(item.binding);
        Undo.RecordObject(_settings, runtime ? "Mark label runtime" : "Mark label as copy");
        _settings.runtimeTextLabels.Remove(id);
        _settings.staticTextLabels.Remove(id);
        if (runtime) _settings.runtimeTextLabels.Add(id);
        else _settings.staticTextLabels.Add(id);
        EditorUtility.SetDirty(_settings);
        AssetDatabase.SaveAssetIfDirty(_settings);

        item.runtimeOverridden = !runtime && item.runtime && item.runtimeReason != "marked runtime";
        item.runtime = runtime;
        item.runtimeReason = runtime ? "marked runtime" : null;
        item.selected = !runtime && item.state != ScanState.Localized;
        _stats = null;
    }

    private void ConfirmRemoveRuntimePlaceholders()
    {
        if (_scan == null) return;
        var keys = LocalizationScanner.RuntimePlaceholderKeys(_scan, _table);
        var labels = LocalizationScanner.RuntimeBoundLabels(_scan);
        if (keys.Count == 0 && labels.Count == 0) { _status = "No runtime placeholder text in the sheets or on LocalizedText keys."; return; }
        if (!EditorUtility.DisplayDialog("Remove runtime placeholders?",
                "Deletes " + keys.Count + " sheet row(s) used only by runtime-filled labels (with their translations and bindings) and clears the LocalizedText key on "
                + labels.Count + " label(s).\n\nLocalizedText stays on those labels so they keep their language font. The placeholder text in scenes and prefabs is not changed. "
                + "Save open scenes afterwards, then run Build runtime table.\n\nThis cannot be undone from the editor (use version control).",
                "Remove", "Cancel")) return;

        if (keys.Count > 0) DeleteKeys(keys);
        int cleared = LocalizationApply.ClearLocalizedTextKeys(labels);
        AssetDatabase.SaveAssets();
        _scan = null;
        _stats = null;
        _status = "Removed " + keys.Count + " runtime placeholder row(s), cleared " + cleared + " LocalizedText key(s). Save scenes, rescan, then Build runtime table.";
    }

    /// <summary>Deletes DELETED scan rows from the sheets and drops them from the review list (the rest of the scan stays).</summary>
    private void ConfirmRemoveDeleted(List<ScanItem> gone)
    {
        if (_scan == null || gone.Count == 0) return;
        var keys = gone.Select(i => i.key).Distinct().ToList();
        int translated = keys.Count(k => _table.TryGet(k, out var e) && e.values.Any(v => !string.IsNullOrEmpty(v.Value)));
        string what = keys.Count == 1 ? "'" + keys[0] + "'" : keys.Count + " deleted strings";
        if (!EditorUtility.DisplayDialog("Remove " + what + "?",
                "Removes " + what + " from the localization sheets, with their bindings"
                + (translated > 0 ? " and the translations of " + translated + " row(s)" : "") + ".\n\n"
                + "Run Build runtime table afterwards. This cannot be undone from the editor (use version control).",
                "Remove", "Cancel")) return;

        DeleteKeys(keys);
        var set = new HashSet<string>(keys);
        _scan.RemoveAll(i => i.state == ScanState.Deleted && set.Contains(i.key));
        _stats = null;
        _status = "Removed " + keys.Count + " deleted string(s) from the sheets. Run Build runtime table to update the game.";
    }

    private IEnumerable<ScanItem> VisibleScan()
    {
        return _scan.Where(i => (_showLocalized || i.state != ScanState.Localized)
                                && (_showRuntime || !i.runtime)
                                && LocGroups.Matches(_scanGroup, i.group)
                                && (string.IsNullOrEmpty(_scanFilter)
                                    || i.key.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.text.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                                    || i.sheet.IndexOf(_scanFilter, System.StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(i => i.state == ScanState.Deleted ? 0 : 1);
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
                        EditorGUI.BeginChangeCheck();
                        int maxChars = EditorGUILayout.IntField(new GUIContent("Max chars", "Sheet column max_chars. 0 = no limit. Translations over it fail validation."), e.maxChars);
                        if (EditorGUI.EndChangeCheck()) { e.maxChars = Mathf.Max(0, maxChars); MarkDirty(e); }
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
