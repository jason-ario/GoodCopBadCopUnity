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
    private const float PanelWidth = 440f;
    private const float PanelPadding = 24f;
    private const float TitleHeight = 48f;
    private const float SubtitleHeight = 22f;
    private const float SpacerHeight = 12f;
    private const float ButtonHeight = 50f;
    private const float ButtonSpacing = 8f;

    private const int TitleFontSize = 22;
    private const int SubtitleFontSize = 13;
    private const int ButtonFontSize = 15;

    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.08f, 0.96f);
    private static readonly Color TitleColor = new Color(1f, 0.8f, 0.2f, 1f);
    private static readonly Color SubtitleColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color ButtonNormalColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ButtonHoverColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color ButtonPressColor = new Color(0.10f, 0.10f, 0.10f, 1f);

    private readonly List<(string Label, Action Callback)> _cheats = new();
    private GameObject _canvasRoot;
    private bool _isVisible;

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
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _canvasRoot.SetActive(visible);

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void BuildUI()
    {
        float totalButtonsHeight = _cheats.Count * ButtonHeight + (_cheats.Count - 1) * ButtonSpacing;
        float panelHeight = PanelPadding + TitleHeight + SubtitleHeight + SpacerHeight
                            + totalButtonsHeight + PanelPadding;

        // ── Canvas ──────────────────────────────────────────────────────────
        _canvasRoot = new GameObject("[CheatConsole] Canvas");
        _canvasRoot.transform.SetParent(transform);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── Full-screen dim ──────────────────────────────────────────────────
        var overlay = new GameObject("Overlay");
        overlay.transform.SetParent(_canvasRoot.transform, false);
        var overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        overlay.AddComponent<Image>().color = OverlayColor;

        // ── Panel ────────────────────────────────────────────────────────────
        var panel = new GameObject("Panel");
        panel.transform.SetParent(_canvasRoot.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, panelHeight);
        panel.AddComponent<Image>().color = PanelColor;

        // ── Content container with VerticalLayoutGroup ───────────────────────
        var content = new GameObject("Content");
        content.transform.SetParent(panel.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(PanelPadding, PanelPadding);
        contentRT.offsetMax = new Vector2(-PanelPadding, -PanelPadding);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = ButtonSpacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // ── Title ────────────────────────────────────────────────────────────
        AddLabel(content.transform, "CHEAT CONSOLE", TitleHeight, TitleFontSize,
            TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── Subtitle ─────────────────────────────────────────────────────────
        AddLabel(content.transform, "Press F12 to close", SubtitleHeight, SubtitleFontSize,
            SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Spacer ───────────────────────────────────────────────────────────
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(content.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.minHeight = SpacerHeight;
        spacerLE.preferredHeight = SpacerHeight;

        // ── Cheat buttons ─────────────────────────────────────────────────────
        foreach (var (label, callback) in _cheats)
            AddButton(content.transform, label, callback);
    }

    private static void AddLabel(Transform parent, string text, float height, int fontSize,
        Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject($"Label_{text}");
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
    }

    private static void AddButton(Transform parent, string label, Action onClick)
    {
        var btnGO = new GameObject($"Btn_{label}");
        btnGO.transform.SetParent(parent, false);

        var le = btnGO.AddComponent<LayoutElement>();
        le.minHeight = ButtonHeight;
        le.preferredHeight = ButtonHeight;

        var img = btnGO.AddComponent<Image>();
        img.color = ButtonNormalColor;

        var btn = btnGO.AddComponent<Button>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = ButtonNormalColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonPressColor;
        colors.selectedColor = ButtonNormalColor;
        colors.colorMultiplier = 1f;
        btn.colors = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        // Button label text
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(16f, 0f);
        labelRT.offsetMax = new Vector2(-16f, 0f);

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = ButtonFontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
    }
}
