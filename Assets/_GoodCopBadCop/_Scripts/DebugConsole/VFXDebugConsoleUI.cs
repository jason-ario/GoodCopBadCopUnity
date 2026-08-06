using System;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay VFX test console toggled by F11.
/// Provides one-click buttons to preview the hit, radiation, and drunk vignette effects
/// without having to trigger them through normal gameplay.
/// Built programmatically — no prefab or scene wiring required.
/// </summary>
public class VFXDebugConsoleUI : MonoBehaviour
{
    // ── Layout constants (match CheatConsoleUI) ──────────────────────────────
    private const float PanelWidth     = 440f;
    private const float PanelHeight    = 600f;
    private const float PanelPadding   = 24f;
    private const float TitleHeight    = 48f;
    private const float SubtitleHeight = 22f;
    private const float SpacerHeight   = 12f;
    private const float ButtonHeight   = 50f;
    private const float ButtonSpacing  = 8f;
    private const float SectionHeight  = 28f;

    private const int TitleFontSize    = 22;
    private const int SubtitleFontSize = 13;
    private const int ButtonFontSize   = 15;
    private const int SectionFontSize  = 11;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color OverlayColor      = new Color(0f,    0f,    0f,    0.6f);
    private static readonly Color PanelColor        = new Color(0.08f, 0.08f, 0.08f, 0.96f);
    private static readonly Color TitleColor        = new Color(0.2f,  0.85f, 0.9f,  1f);   // teal
    private static readonly Color SubtitleColor     = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color SectionColor      = new Color(0.2f,  0.7f,  0.75f, 0.85f);
    private static readonly Color ButtonNormalColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ButtonHoverColor  = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color ButtonPressColor  = new Color(0.10f, 0.10f, 0.10f, 1f);
    private static readonly Color StatusColor       = new Color(0.4f,  0.9f,  0.5f,  1f);   // green

    private readonly List<(string Label, Action Callback)> _entries = new();
    private GameObject    _canvasRoot;
    private RectTransform _outerRT;
    private RectTransform _scrollContent;
    private ScrollRect    _scrollRect;
    private TextMeshProUGUI _statusLabel;
    private bool          _isVisible;


    // ─────────────────────────────────────────────────────────────────────────
    // Unity messages
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        RegisterEntries();
        BuildUI();
        SetVisible(false);
    }

    private void Update()
    {
        if (!GameSettings.Instance.DebugConsoleEnabled)
        {
            if (_isVisible) SetVisible(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.F11))
            SetVisible(!_isVisible);
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Entry registration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Add new VFX test actions here. Sections are added via <see cref="AddSection"/>.
    /// </summary>
    private void RegisterEntries()
    {
        // ── Hit / Damage ──────────────────────────────────────────────────────
        AddSection("HIT / DAMAGE");

        AddEntry("Take Small Hit  (5 dmg — brief bloody flash)", () =>
        {
            var health = GetLocalPlayerHealth();
            if (health == null) { ShowStatus("No local player found."); return; }
            health.TakeDamage(5f, EffectKeys.DefaultPlayerDamage);
            ShowStatus("Hit: 5 dmg applied.");
        });

        AddEntry("Take Medium Hit  (20 dmg)", () =>
        {
            var health = GetLocalPlayerHealth();
            if (health == null) { ShowStatus("No local player found."); return; }
            health.TakeDamage(20f, EffectKeys.DefaultPlayerDamage);
            ShowStatus("Hit: 20 dmg applied.");
        });

        AddEntry("Take Large Hit  (50 dmg)", () =>
        {
            var health = GetLocalPlayerHealth();
            if (health == null) { ShowStatus("No local player found."); return; }
            health.TakeDamage(50f, EffectKeys.DefaultPlayerDamage);
            ShowStatus("Hit: 50 dmg applied.");
        });

        AddEntry("Restore Full Health", () =>
        {
            var health = GetLocalPlayerHealth();
            if (health == null) { ShowStatus("No local player found."); return; }
            health.ResetHealth();
            ShowStatus("Health restored to full.");
        });

        // ── Radiation ─────────────────────────────────────────────────────────
        AddSection("RADIATION  (VFX active above 75%)");

        AddEntry("Set Radiation  0%  (clear — VFX off)", () =>
        {
            var rad = GetLocalPlayerRadiation();
            if (rad == null) { ShowStatus("No local player found."); return; }
            rad.RemoveRadiation(rad.MaxRadiation);
            ShowStatus($"Radiation set to 0%.");
        });

        AddEntry("Set Radiation  50%  (building up)", () =>
        {
            if (!SetRadiation(0.5f, out string msg)) { ShowStatus(msg); return; }
            ShowStatus("Radiation set to 50%.");
        });

        AddEntry("Set Radiation  80%  (critical — VFX ON)", () =>
        {
            if (!SetRadiation(0.8f, out string msg)) { ShowStatus(msg); return; }
            ShowStatus("Radiation set to 80%  — glitch VFX should appear.");
        });

        AddEntry("Set Radiation  100%  (max)", () =>
        {
            if (!SetRadiation(1.0f, out string msg)) { ShowStatus(msg); return; }
            ShowStatus("Radiation set to 100%.");
        });

        // ── Drunk ─────────────────────────────────────────────────────────────
        AddSection("DRUNK");

        AddEntry("Set Drunk  ON", () =>
        {
            var drunk = PlayerInstance.Instance?.PlayerDrunkState;
            if (drunk == null) { ShowStatus("No local player / PlayerDrunkState found."); return; }
            drunk.SetDrunk(true);
            ShowStatus("Drunk state: ON  — effect pulses every 2.5 s.");
        });

        AddEntry("Set Drunk  OFF", () =>
        {
            var drunk = PlayerInstance.Instance?.PlayerDrunkState;
            if (drunk == null) { ShowStatus("No local player / PlayerDrunkState found."); return; }
            drunk.SetDrunk(false);
            ShowStatus("Drunk state: OFF.");
        });
    }

    // ── Helpers for registration ──────────────────────────────────────────────

    private void AddSection(string label)
    {
        // Sentinel: sections are identified by a null callback.
        _entries.Add((label, null));
    }

    private void AddEntry(string label, Action callback)
    {
        _entries.Add((label, callback));
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Game-state helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static PlayerHealth GetLocalPlayerHealth()
    {
        return PlayerInstance.Instance != null ? PlayerInstance.Instance.PlayerHealth : null;
    }

    private static PlayerRadiation GetLocalPlayerRadiation()
    {
        return PlayerInstance.Instance != null ? PlayerInstance.Instance.PlayerRadiation : null;
    }

    /// <summary>Sets <see cref="PlayerRadiation"/> to a specific normalized value (0–1).</summary>
    private static bool SetRadiation(float normalized01, out string errorMessage)
    {
        var rad = GetLocalPlayerRadiation();
        if (rad == null) { errorMessage = "No local player found."; return false; }

        // Clear current radiation then add the desired amount.
        rad.RemoveRadiation(rad.MaxRadiation);
        rad.AddRadiation(rad.MaxRadiation * Mathf.Clamp01(normalized01));

        errorMessage = null;
        return true;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Visibility
    // ─────────────────────────────────────────────────────────────────────────

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _canvasRoot.SetActive(visible);

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            RebuildLayout();
            ShowStatus(string.Empty);
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

    private void ShowStatus(string message)
    {
        if (_statusLabel != null)
            _statusLabel.text = message;
    }


    // ─────────────────────────────────────────────────────────────────────────
    // UI construction  (matches CheatConsoleUI layout)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        _canvasRoot = new GameObject("[VFXDebugConsole] Canvas");
        _canvasRoot.transform.SetParent(transform);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101; // above CheatConsole

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── Full-screen dim ───────────────────────────────────────────────────
        var overlay = new GameObject("Overlay");
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

        // ── Outer VLG (header + status + scroll view) ─────────────────────────
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
        AddLabel(outer.transform, "VFX DEBUG CONSOLE", TitleHeight, TitleFontSize,
            TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── Subtitle ──────────────────────────────────────────────────────────
        AddLabel(outer.transform, "Press F11 to close", SubtitleHeight, SubtitleFontSize,
            SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Status line ───────────────────────────────────────────────────────
        var statusGO = new GameObject("Status");
        statusGO.transform.SetParent(outer.transform, false);
        var statusLE = statusGO.AddComponent<LayoutElement>();
        statusLE.minHeight = statusLE.preferredHeight = SubtitleHeight;
        _statusLabel = statusGO.AddComponent<TextMeshProUGUI>();
        _statusLabel.fontSize  = SubtitleFontSize;
        _statusLabel.color     = StatusColor;
        _statusLabel.alignment = TextAlignmentOptions.Center;

        // ── Spacer ────────────────────────────────────────────────────────────
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(outer.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.minHeight = spacerLE.preferredHeight = SpacerHeight;

        // ── Scroll view ───────────────────────────────────────────────────────
        float scrollHeight = PanelHeight - 2f * PanelPadding
                             - TitleHeight - SubtitleHeight * 2f - SpacerHeight
                             - 3f * ButtonSpacing;

        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(outer.transform, false);
        var svLE = svGO.AddComponent<LayoutElement>();
        svLE.minHeight       = scrollHeight;
        svLE.preferredHeight = scrollHeight;
        svLE.flexibleHeight  = 1f;

        var sv = svGO.AddComponent<ScrollRect>();
        sv.horizontal = false;
        sv.vertical   = true;
        _scrollRect   = sv;

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

        // ── Buttons & section headers ─────────────────────────────────────────
        foreach (var (label, callback) in _entries)
        {
            if (callback == null)
                AddSectionHeader(_scrollContent, label);
            else
                AddButton(_scrollContent, label, callback);
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────────────────────────

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

    private static void AddSectionHeader(Transform parent, string label)
    {
        var go = new GameObject($"Section_{label}");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SectionHeight;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.35f, 0.38f, 0.6f);

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 0f);
        textRT.offsetMax = new Vector2(-10f, 0f);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = $"── {label} ──";
        tmp.fontSize  = SectionFontSize;
        tmp.color     = SectionColor;
        tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
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
