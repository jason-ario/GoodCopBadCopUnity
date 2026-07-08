using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay cheat console toggled by F12. Provides one-click buttons for common debug skip points.
/// Built programmatically — no prefab or scene wiring required.
/// Add new entries inside <see cref="RegisterCheats"/> to extend the menu.
/// </summary>
[RequireComponent(typeof(DebugConsole))]
public class CheatConsoleUI : MonoBehaviour
{
    private const float PanelWidth       = 440f;
    private const float PanelHeight      = 600f;
    private const float PanelPadding     = 24f;
    private const float TitleHeight      = 48f;
    private const float SubtitleHeight   = 22f;
    private const float SpacerHeight     = 12f;
    private const float ButtonHeight     = 50f;
    private const float ButtonSpacing    = 8f;

    private const int TitleFontSize    = 22;
    private const int SubtitleFontSize = 13;
    private const int ButtonFontSize   = 15;

    private static readonly Color OverlayColor     = new Color(0f,    0f,    0f,    0.6f);
    private static readonly Color PanelColor       = new Color(0.08f, 0.08f, 0.08f, 0.96f);
    private static readonly Color TitleColor       = new Color(1f,    0.8f,  0.2f,  1f);
    private static readonly Color SubtitleColor    = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color ButtonNormalColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ButtonHoverColor  = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color ButtonPressColor  = new Color(0.10f, 0.10f, 0.10f, 1f);

    private readonly List<(string Label, Action Callback)> _cheats = new();
    private GameObject      _canvasRoot;
    private RectTransform   _outerRT;
    private RectTransform   _scrollContent;
    private ScrollRect      _scrollRect;
    private bool            _isVisible;

    private void Awake()
    {
        RegisterCheats();
        BuildUI();
        SetVisible(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
            SetVisible(!_isVisible);
    }

    /// <summary>
    /// Registers all cheat entries shown in the overlay menu.
    /// Add new <c>_cheats.Add(...)</c> calls here to extend the console.
    /// </summary>
    private void RegisterCheats()
    {
        _cheats.Add(("Skip to Day 1 — Booth Start", () =>
        {
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToDay(1));
            SetVisible(false);
        }));

        _cheats.Add(("Skip to Day 1 — Soldier / Alexei Cutscene", () =>
        {
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToSoldierSlot());
            SetVisible(false);
        }));

        _cheats.Add(("Skip to End of Day 1", () =>
        {
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDay1());
            SetVisible(false);
        }));

        _cheats.Add(("Skip to Day 2 — Vlad Out Back Cutscene", () =>
        {
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToEndOfDay2());
            SetVisible(false);
        }));

        _cheats.Add(("Skip to Day 3 — Start (In Front of Bunker)", () =>
        {
            DebugConsole.Instance.EnsureGameStartedThen(() => DebugConsole.Instance.SkipToStartOfDay3());
            SetVisible(false);
        }));
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _canvasRoot.SetActive(visible);

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            RebuildLayout();
        }
    }

    private void RebuildLayout()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_outerRT);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        Canvas.ForceUpdateCanvases();
        if (_scrollRect != null)
            _scrollRect.normalizedPosition = new Vector2(0f, 1f);
    }

    private void BuildUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        _canvasRoot = new GameObject("[CheatConsole] Canvas");
        _canvasRoot.transform.SetParent(transform);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── Full-screen dim ───────────────────────────────────────────────────
        var overlay   = new GameObject("Overlay");
        overlay.transform.SetParent(_canvasRoot.transform, false);
        var overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        overlay.AddComponent<Image>().color = OverlayColor;

        // ── Panel ─────────────────────────────────────────────────────────────
        var panel   = new GameObject("Panel");
        panel.transform.SetParent(_canvasRoot.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.AddComponent<Image>().color = PanelColor;

        // ── Outer VLG (header + scroll view) ─────────────────────────────────
        var outer   = new GameObject("Outer");
        outer.transform.SetParent(panel.transform, false);
        var outerRT = outer.AddComponent<RectTransform>();
        outerRT.anchorMin = Vector2.zero;
        outerRT.anchorMax = Vector2.one;
        outerRT.offsetMin = new Vector2(PanelPadding, PanelPadding);
        outerRT.offsetMax = new Vector2(-PanelPadding, -PanelPadding);
        _outerRT = outerRT;

        var outerVLG = outer.AddComponent<VerticalLayoutGroup>();
        outerVLG.spacing              = ButtonSpacing;
        outerVLG.childAlignment       = TextAnchor.UpperCenter;
        outerVLG.childControlWidth    = true;
        outerVLG.childControlHeight   = true;
        outerVLG.childForceExpandWidth  = true;
        outerVLG.childForceExpandHeight = false;

        // ── Title ─────────────────────────────────────────────────────────────
        AddLabel(outer.transform, "CHEAT CONSOLE", TitleHeight, TitleFontSize,
            TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── Subtitle ──────────────────────────────────────────────────────────
        AddLabel(outer.transform, "Press F12 to close", SubtitleHeight, SubtitleFontSize,
            SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Spacer ────────────────────────────────────────────────────────────
        var spacer   = new GameObject("Spacer");
        spacer.transform.SetParent(outer.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.minHeight = spacerLE.preferredHeight = SpacerHeight;

        // ── Scroll view ───────────────────────────────────────────────────────
        float scrollHeight = PanelHeight - 2f * PanelPadding
                             - TitleHeight - SubtitleHeight - SpacerHeight
                             - 2f * ButtonSpacing;

        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(outer.transform, false);
        var svLE = svGO.AddComponent<LayoutElement>();
        svLE.minHeight       = scrollHeight;
        svLE.preferredHeight = scrollHeight;
        svLE.flexibleHeight  = 1f;

        var sv = svGO.AddComponent<ScrollRect>();
        sv.horizontal  = false;
        sv.vertical    = true;
        _scrollRect    = sv;

        // Viewport
        var vpGO = new GameObject("Viewport");
        vpGO.transform.SetParent(svGO.transform, false);
        var vpRT = vpGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;
        vpGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        vpGO.AddComponent<RectMask2D>();
        sv.viewport = vpRT;

        // Scroll content
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(vpGO.transform, false);
        _scrollContent = contentGO.AddComponent<RectTransform>();
        _scrollContent.anchorMin = new Vector2(0f, 1f);
        _scrollContent.anchorMax = new Vector2(1f, 1f);
        _scrollContent.pivot     = new Vector2(0.5f, 1f);
        _scrollContent.offsetMin = Vector2.zero;
        _scrollContent.offsetMax = Vector2.zero;

        var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVLG.spacing              = ButtonSpacing;
        contentVLG.padding              = new RectOffset(0, 0, 4, 4);
        contentVLG.childAlignment       = TextAnchor.UpperCenter;
        contentVLG.childControlWidth    = true;
        contentVLG.childControlHeight   = true;
        contentVLG.childForceExpandWidth  = true;
        contentVLG.childForceExpandHeight = false;

        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        sv.content = _scrollContent;

        // ── Cheat buttons ─────────────────────────────────────────────────────
        foreach (var (label, callback) in _cheats)
            AddButton(_scrollContent, label, callback);
    }

    private static void AddLabel(Transform parent, string text, float height, int fontSize,
        Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject($"Label_{text}");
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
    }

    private static void AddButton(Transform parent, string label, Action onClick)
    {
        var btnGO = new GameObject($"Btn_{label}");
        btnGO.transform.SetParent(parent, false);

        var le = btnGO.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = ButtonHeight;

        var img = btnGO.AddComponent<Image>();
        img.color = ButtonNormalColor;

        var btn    = btnGO.AddComponent<Button>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor      = ButtonNormalColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor     = ButtonPressColor;
        colors.selectedColor    = ButtonNormalColor;
        colors.colorMultiplier  = 1f;
        btn.colors        = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(16f, 0f);
        labelRT.offsetMax = new Vector2(-16f, 0f);

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = ButtonFontSize;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
    }
}
