using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class LocalizationWindow
{
    private List<FitRow> _fit;
    private List<FitRow> _fitFiltered = new List<FitRow>();
    private bool _fitDirty = true;
    private bool _fitPrefabs = true, _fitScenes = true, _fitHideMatching;
    private string _fitFolder = TextFit.DefaultFolder;
    private string _fitSearch = "";
    private int _fitRiskFilter = 1;     // 0 all, 1 at risk, 2+ specific risk
    private int _fitCatFilter;          // 0 all, 1+ category
    private int _fitPage;
    private FitCategory _fitBulkCategory = FitCategory.Label;
    private Vector2 _fitScroll;
    private readonly HashSet<FitRow> _fitSelected = new HashSet<FitRow>();
    private const int FitPageSize = 50;

    private static readonly string[] FitRiskOptions =
        new[] { "All risks", "At risk (high, medium, auto no clip)" }.Concat(TextFit.RiskLabels.Select(r => "Only " + r.ToLowerInvariant())).ToArray();
    private static readonly string[] FitCatOptions = { "All categories", "Label", "Header", "Body", "Grow", "Skip" };
    private static readonly string[] FitCatNames = System.Enum.GetNames(typeof(FitCategory));

    private static Color RiskColor(FitRisk r)
    {
        switch (r)
        {
            case FitRisk.High: return LocUI.Bad;
            case FitRisk.Medium: return LocUI.Warn;
            case FitRisk.AutoNoClip: return new Color(0.85f, 0.55f, 0.25f);
            case FitRisk.ClippedNoShrink: return LocUI.Violet;
            case FitRisk.Ok: return LocUI.Ok;
            case FitRisk.Grouped: return LocUI.Accent;
            default: return LocUI.Neutral;
        }
    }

    private void RebuildFitFilter()
    {
        _fitDirty = false;
        IEnumerable<FitRow> q = _fit ?? Enumerable.Empty<FitRow>();
        q = q.Where(r => r.tmp != null);
        if (_fitRiskFilter == 1) q = q.Where(r => TextFit.IsAtRisk(r.risk));
        else if (_fitRiskFilter >= 2) q = q.Where(r => (int)r.risk == _fitRiskFilter - 2);
        if (_fitCatFilter > 0) q = q.Where(r => (int)r.chosen == _fitCatFilter - 1);
        if (_fitHideMatching) q = q.Where(r => !r.matchesPreset);
        if (!string.IsNullOrEmpty(_fitSearch))
            q = q.Where(r => Contains(r.assetPath, _fitSearch) || Contains(r.path, _fitSearch) || Contains(r.text, _fitSearch));
        _fitFiltered = q.OrderBy(r => r.risk).ThenBy(r => r.assetPath).ThenBy(r => r.path).ToList();
        _fitPage = Mathf.Clamp(_fitPage, 0, Mathf.Max(0, (_fitFiltered.Count - 1) / FitPageSize));
    }

    private void DrawTextFitTab()
    {
        DrawFitIntro();
        DrawFitScanCard();
        if (_fit == null) return;

        if (_fitDirty) RebuildFitFilter();
        DrawFitSummary();
        DrawFitFilters();
        DrawFitToolbar();

        _fitScroll = EditorGUILayout.BeginScrollView(_fitScroll);
        foreach (var row in _fitFiltered.Skip(_fitPage * FitPageSize).Take(FitPageSize).ToList())
            DrawFitRow(row);
        EditorGUILayout.EndScrollView();
    }

    private void DrawFitIntro()
    {
        LocUI.Hint("<b>Text Fit</b> keeps translated text inside its box. Each TMP text gets a category: "
            + "<b>Label</b> (one line, shrinks to 70%, then ...), <b>Header</b> (wraps, shrinks to 60%, then ...), "
            + "<b>Body</b> (wraps, shrinks to 75%, then cuts off), <b>Grow</b> (layout-driven: keeps its sizing, only wraps) or <b>Skip</b> (numbers, inputs, animated text). "
            + "The current font size becomes the maximum, so English barely changes. Check the result with a pseudolocale in the Preview tab.\n"
            + "Not covered here: sizes set by scripts (SettingsMenuView, TerminalListElement, Guidebook views) and font-size overrides on prefab instances inside scenes.");
    }

    private void DrawFitScanCard()
    {
        using (LocUI.Card())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _fitPrefabs = LocUI.Chip(_fitPrefabs, "Prefabs");
                _fitScenes = LocUI.Chip(_fitScenes, "Loaded scenes");
                GUILayout.Label("Folder", LocUI.MutedStyle, GUILayout.Width(40));
                _fitFolder = EditorGUILayout.TextField(_fitFolder, GUILayout.MinWidth(160));
                if (LocUI.Button(_fit == null ? "Scan texts" : "Rescan", true))
                {
                    _fit = TextFit.Scan(_fitPrefabs, _fitScenes, _fitFolder);
                    _fitSelected.Clear();
                    _fitDirty = true;
                    _status = "Text Fit: found " + _fit.Count + " TMP texts.";
                    GUIUtility.ExitGUI();
                }
            }

            if (_settings != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    GUILayout.Label("Min size", LocUI.MutedStyle, GUILayout.Width(52));
                    float l = FitSlider("Label", _settings.fitLabelMin);
                    float h = FitSlider("Header", _settings.fitHeaderMin);
                    float b = FitSlider("Body", _settings.fitBodyMin);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, "Text Fit ratios");
                        _settings.fitLabelMin = l; _settings.fitHeaderMin = h; _settings.fitBodyMin = b;
                        EditorUtility.SetDirty(_settings);
                    }
                }
            }
        }
    }

    private static float FitSlider(string label, float value)
    {
        GUILayout.Label(label, GUILayout.Width(46));
        float v = GUILayout.HorizontalSlider(value, 0.3f, 1f, GUILayout.Width(80));
        GUILayout.Label(Mathf.RoundToInt(v * 100) + "%", GUILayout.Width(36));
        return Mathf.Round(v * 100f) / 100f;
    }

    private void DrawFitSummary()
    {
        var live = _fit.Where(r => r.tmp != null).ToList();
        using (new EditorGUILayout.HorizontalScope())
        {
            LocUI.StatTile("texts", live.Count.ToString("N0"), LocUI.Accent, 110);
            LocUI.StatTile("high", live.Count(r => r.risk == FitRisk.High).ToString("N0"), LocUI.Bad, 100);
            LocUI.StatTile("medium", live.Count(r => r.risk == FitRisk.Medium).ToString("N0"), LocUI.Warn, 100);
            LocUI.StatTile("auto, no clip", live.Count(r => r.risk == FitRisk.AutoNoClip).ToString("N0"), RiskColor(FitRisk.AutoNoClip), 110);
            LocUI.StatTile("ok / grouped", live.Count(r => r.risk == FitRisk.Ok || r.risk == FitRisk.Grouped).ToString("N0"), LocUI.Ok, 110);
            LocUI.StatTile("grow / skip", live.Count(r => r.risk == FitRisk.Grows || r.risk == FitRisk.Skipped).ToString("N0"), LocUI.Neutral, 110);
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawFitFilters()
    {
        using (LocUI.Card())
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _fitRiskFilter = EditorGUILayout.Popup(_fitRiskFilter, FitRiskOptions, GUILayout.Width(230));
                _fitCatFilter = EditorGUILayout.Popup(_fitCatFilter, FitCatOptions, GUILayout.Width(120));
                _fitHideMatching = LocUI.Chip(_fitHideMatching, "Hide already fitted", LocUI.Ok);
                _fitSearch = EditorGUILayout.TextField(_fitSearch, EditorStyles.toolbarSearchField);
            }
            if (EditorGUI.EndChangeCheck()) { _fitPage = 0; _fitDirty = true; }
        }
    }

    private void DrawFitToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(_fitFiltered.Count.ToString("N0") + " shown", EditorStyles.boldLabel, GUILayout.Width(80));
            GUI.enabled = _fitPage > 0;
            if (GUILayout.Button("\u25C0", GUILayout.Width(26))) _fitPage--;
            GUI.enabled = (_fitPage + 1) * FitPageSize < _fitFiltered.Count;
            if (GUILayout.Button("\u25B6", GUILayout.Width(26))) _fitPage++;
            GUI.enabled = true;
            GUILayout.Label("Page " + (_fitPage + 1) + " / " + Mathf.Max(1, Mathf.CeilToInt(_fitFiltered.Count / (float)FitPageSize)), GUILayout.Width(80));
            GUILayout.FlexibleSpace();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(_fitSelected.Count + " selected", EditorStyles.boldLabel, GUILayout.Width(80));
            if (GUILayout.Button("Select page", EditorStyles.miniButtonLeft, GUILayout.Width(80)))
                foreach (var r in _fitFiltered.Skip(_fitPage * FitPageSize).Take(FitPageSize)) _fitSelected.Add(r);
            if (GUILayout.Button("Select all shown (" + _fitFiltered.Count + ")", EditorStyles.miniButtonMid, GUILayout.Width(150)))
                foreach (var r in _fitFiltered) _fitSelected.Add(r);
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50))) _fitSelected.Clear();

            GUILayout.FlexibleSpace();
            GUI.enabled = _fitSelected.Count > 0;

            GUILayout.Label("Set to", LocUI.MutedStyle, GUILayout.Width(36));
            _fitBulkCategory = (FitCategory)EditorGUILayout.Popup((int)_fitBulkCategory, FitCatNames, GUILayout.Width(80));
            if (GUILayout.Button("Set", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                foreach (var r in _fitSelected) { r.chosen = _fitBulkCategory; TextFit.Refresh(r); }
                _fitDirty = true;
            }

            if (GUILayout.Button(new GUIContent("Share one size", "Adds a TextSizeGroup on the closest common parent so the selected texts always use the same size."), GUILayout.Width(110)))
            {
                _status = TextFit.AddSizeGroup(_fitSelected.ToList(), _settings);
                _fitDirty = true;
                GUIUtility.ExitGUI();
            }

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = LocUI.Accent;
            if (GUILayout.Button("Apply to selected (" + _fitSelected.Count + ")", GUILayout.Width(160), GUILayout.Height(20)))
                ApplyFit(_fitSelected.ToList());
            GUI.backgroundColor = prev;
            GUI.enabled = true;
        }
    }

    private void ApplyFit(List<FitRow> rows)
    {
        int files = rows.Select(r => r.assetPath).Distinct().Count();
        if (!EditorUtility.DisplayDialog("Apply Text Fit?",
                $"Apply the chosen category to {rows.Count} text(s) in {files} prefab/scene file(s)?\n\n"
                + "The current font size becomes the auto-size maximum. Undo (Ctrl+Z) reverts the change.", "Apply", "Cancel")) return;
        int n = TextFit.Apply(rows, _settings);
        _fitDirty = true;
        _status = "Text Fit: updated " + n + " text(s) in " + files + " file(s).";
        GUIUtility.ExitGUI();
    }

    private void DrawFitRow(FitRow row)
    {
        var t = row.tmp;
        if (t == null) return;
        using (LocUI.Card(RiskColor(row.risk)))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool sel = _fitSelected.Contains(row);
                bool nsel = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
                if (nsel != sel) { if (nsel) _fitSelected.Add(row); else _fitSelected.Remove(row); }

                LocUI.Pill(TextFit.RiskLabels[(int)row.risk], RiskColor(row.risk), 70);
                EditorGUILayout.SelectableLabel(row.path, EditorStyles.boldLabel, GUILayout.Height(18), GUILayout.ExpandWidth(true));
                LocUI.Pill(Path.GetFileNameWithoutExtension(row.assetPath) + (row.isScene ? " (scene)" : ""), LocUI.Neutral);

                EditorGUI.BeginChangeCheck();
                var cat = (FitCategory)EditorGUILayout.Popup((int)row.chosen, FitCatNames, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { row.chosen = cat; TextFit.Refresh(row); }

                if (GUILayout.Button("Ping", EditorStyles.miniButtonLeft, GUILayout.Width(40))) EditorGUIUtility.PingObject(t.gameObject);
                if (GUILayout.Button("Open", EditorStyles.miniButtonMid, GUILayout.Width(42))) TextFit.Open(row);
                GUI.enabled = !row.inGroup && row.chosen != FitCategory.Skip;
                if (GUILayout.Button("Apply", EditorStyles.miniButtonRight, GUILayout.Width(46)))
                {
                    TextFit.Apply(new List<FitRow> { row }, _settings);
                    _status = "Text Fit: updated " + row.path;
                    _fitDirty = true;
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22);
                string snippet = row.text.Length > 70 ? row.text.Substring(0, 70) + "..." : row.text;
                string sizing = t.enableAutoSizing ? $"auto {t.fontSizeMin:0.#}-{t.fontSizeMax:0.#}" : $"{t.fontSize:0.#} pt";
                string wrap = t.textWrappingMode == TMPro.TextWrappingModes.NoWrap ? "no wrap" : "wraps";
                LocUI.Muted_($"\"{snippet}\"   |   {sizing}   |   {wrap}   |   {t.overflowMode}{(row.inLayout ? "   |   layout-driven" : "")}"
                    + (row.chosen != row.suggested ? $"   |   suggested: {row.suggested}" : ""));
                GUILayout.FlexibleSpace();
                if (row.matchesPreset && row.chosen != FitCategory.Skip) LocUI.Pill("FITTED", LocUI.Ok);
            }
        }
    }
}
