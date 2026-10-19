using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F11 Trailer Console overlay. Renders <see cref="TrailerConsoleActions"/> as toggle/stepper rows.
/// Built programmatically like <see cref="CheatConsoleUI"/>. Gated by
/// <see cref="GameSettings.TrailerConsoleEnabled"/> (independent of the debug console flag).
/// Unlike the cheat console, rows keep the menu open, and closing restores the cursor and the
/// player's look state so a take can start immediately. Time scale is never touched on open.
/// </summary>
[RequireComponent(typeof(TrailerConsoleActions))]
public class TrailerConsoleUI : MonoBehaviour
{
    // ── Layout constants ──────────────────────────────────────────────────────
    private const float PanelWidth     = 520f;
    private const float PanelHeight    = 860f;
    private const float PanelMargin    = 40f;
    private const float PanelPadding   = 20f;
    private const float TitleHeight    = 40f;
    private const float SubtitleHeight = 20f;
    private const float RowHeight      = 38f;
    private const float RowSpacing     = 6f;
    private const float SectionHeight  = 26f;
    private const float SmallButtonWidth = 64f;

    private const int TitleFontSize    = 22;
    private const int SubtitleFontSize = 13;
    private const int RowFontSize      = 15;
    private const int SectionFontSize  = 12;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color PanelColor        = new Color(0.08f, 0.08f, 0.08f, 0.94f);
    private static readonly Color TitleColor        = new Color(1f,    0.3f,  0.85f, 1f);   // magenta
    private static readonly Color SubtitleColor     = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color SectionBgColor    = new Color(0.35f, 0.08f, 0.3f,  0.6f);
    private static readonly Color SectionColor      = new Color(1f,    0.55f, 0.9f,  0.9f);
    private static readonly Color ButtonNormalColor = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ButtonHoverColor  = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color ButtonPressColor  = new Color(0.10f, 0.10f, 0.10f, 1f);
    private static readonly Color StatusColor       = new Color(0.4f,  0.9f,  0.5f,  1f);

    private static readonly float[] DollyDurations = { 2f, 3f, 4f, 6f, 8f, 10f, 15f, 20f, 30f };
    private const float FovStep = 5f;

    /// <summary>True while the Trailer Console menu is open (free cam ignores input meanwhile).</summary>
    public static bool IsOpen { get; private set; }

    private static CursorLockMode s_restoreLock = CursorLockMode.Locked;
    private static bool s_restoreVisible;

    private TrailerConsoleActions _actions;
    private GameObject      _canvasRoot;
    private RectTransform   _outerRT;
    private RectTransform   _scrollContent;
    private ScrollRect      _scrollRect;
    private TextMeshProUGUI _statusLabel;
    private bool            _isVisible;
    private bool            _savedCanLook;
    private PlayerMovementController _lookOwner;
    private readonly List<Action> _refreshers = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        s_restoreLock = CursorLockMode.Locked;
        s_restoreVisible = false;
    }

    /// <summary>
    /// Updates the cursor state restored when the menu closes. Used by systems (e.g. the free cam)
    /// that change the cursor while the menu owns it.
    /// </summary>
    public static void SetCursorRestoreState(CursorLockMode lockMode, bool visible)
    {
        s_restoreLock = lockMode;
        s_restoreVisible = visible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Unity messages
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _actions = GetComponent<TrailerConsoleActions>();
        // UI is built lazily on first show (see CheatConsoleUI for the TMP/RectMask2D reason).
    }

    private void OnEnable()  => _actions.StatusChanged += OnStatusChanged;

    private void OnDisable()
    {
        _actions.StatusChanged -= OnStatusChanged;
        if (_isVisible) SetVisible(false);
    }

    private void Update()
    {
        if (GameSettings.Instance == null || !GameSettings.Instance.TrailerConsoleEnabled)
        {
            if (_isVisible) SetVisible(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.F11))
            SetVisible(!_isVisible);

        // Other systems may re-lock the cursor while we're open; keep it usable.
        if (_isVisible && Cursor.lockState != CursorLockMode.None)
        {
            SetCursorRestoreState(Cursor.lockState, Cursor.visible);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Visibility
    // ─────────────────────────────────────────────────────────────────────────

    private void SetVisible(bool visible)
    {
        if (visible == _isVisible) return;
        _isVisible = visible;
        IsOpen = visible;

        if (visible)
        {
            if (_canvasRoot == null)
                BuildUI();

            SetCursorRestoreState(Cursor.lockState, Cursor.visible);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            var player = PlayerInstance.Instance;
            _lookOwner = player != null ? player.GetComponent<PlayerMovementController>() : null;
            if (_lookOwner != null)
            {
                _savedCanLook = _lookOwner.CanLook;
                _lookOwner.SetCanLook(false);
            }

            _canvasRoot.SetActive(true);
            ShowMainPage();
            OnStatusChanged(_actions.LastStatus);
        }
        else
        {
            if (_canvasRoot != null)
                _canvasRoot.SetActive(false);

            if (_lookOwner != null)
            {
                _lookOwner.SetCanLook(_savedCanLook);
                _lookOwner = null;
            }

            Cursor.lockState = s_restoreLock;
            Cursor.visible   = s_restoreVisible;
        }
    }

    private void OnStatusChanged(string message)
    {
        if (_statusLabel != null)
            _statusLabel.text = message;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pages
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowMainPage()
    {
        ClearScrollContent();
        var a = _actions;
        var cam = a.FreeCam;

        // ── Top ───────────────────────────────────────────────────────────────
        AddRow(() => "Macros", ("Trailer Mode", a.TrailerMode), ("Reset All", a.ResetAll));

        // ── Clean frame ───────────────────────────────────────────────────────
        AddSection("CLEAN FRAME");
        AddToggle("Hide HUD  (Num0)",          () => a.HudHidden,          a.SetHudHidden);
        AddToggle("Hide subtitles",            () => a.SubtitlesHidden,    a.SetSubtitlesHidden);
        AddToggle("Hide highlights",           () => a.HighlightsHidden,   a.SetHighlightsHidden);
        AddToggle("Hide arms / held item",     () => a.ArmsHidden,         a.SetArmsHidden);
        AddToggle("Suppress screen FX",        () => a.ScreenFxSuppressed, a.SetScreenFxSuppressed);
        AddToggle("Disable comfort FX",        () => a.ComfortFxDisabled,  a.SetComfortFxDisabled);
        AddToggle("God mode",                  () => a.GodMode,            a.SetGodMode);

        // ── Camera ────────────────────────────────────────────────────────────
        AddSection("CAMERA");
        AddToggle("Free cam  (Num.)", () => a.IsFreeCamActive, a.SetFreeCamActive);
        if (cam != null)
        {
            AddToggle("Show player body in free cam", () => cam.ShowPlayerBody, v => cam.ShowPlayerBody = v);
            AddStepper("Speed", () => $"{cam.MoveSpeed:0.##} m/s",
                () => cam.MoveSpeed /= 1.5f, () => cam.MoveSpeed *= 1.5f);
            AddStepper("Smoothing", () => $"{cam.Smoothing:0.00}",
                () => cam.Smoothing -= 0.05f, () => cam.Smoothing += 0.05f);
            AddStepper("FOV", () => $"{cam.FieldOfView:0}°",
                () => cam.FieldOfView -= FovStep, () => cam.FieldOfView += FovStep);

            for (int slot = 1; slot <= 4; slot++)
            {
                int s = slot;
                AddRow(() => $"Bookmark {s}  {(cam.HasBookmark(s) ? "[saved]" : "[empty]")}",
                    ("Save", () => cam.SaveBookmark(s)),
                    ("Go",   () => cam.RecallBookmark(s)));
            }

            AddStepper("Dolly 1->2", () => $"{cam.DollyDuration:0.#} s",
                () => cam.DollyDuration = StepArray(DollyDurations, cam.DollyDuration, -1),
                () => cam.DollyDuration = StepArray(DollyDurations, cam.DollyDuration, +1));
            AddRow(() => cam.IsDollyPlaying ? "Dolly playing…" : "Dolly  (Num5)", ("Play", () => cam.PlayDolly()));
        }
        else
        {
            AddInfo("TrailerFreeCam not wired on this object.");
        }

        AddToggle("Depth of field", () => a.DofEnabled, a.SetDofEnabled);
        AddStepper("Focus", () => $"{a.FocusDistance:0.0#} m",
            () => a.FocusDistance = StepFocus(a.FocusDistance, -1), () => a.FocusDistance = StepFocus(a.FocusDistance, +1));
        AddStepper("Aperture", () => $"f/{a.Aperture:0.0}",
            () => a.Aperture = StepAperture(a.Aperture, -1), () => a.Aperture = StepAperture(a.Aperture, +1));
        AddToggle("Vignette / grain off", () => a.VignetteGrainOff, a.SetVignetteGrainOff);

        // ── Time ──────────────────────────────────────────────────────────────
        AddSection("TIME");
        var presets = TrailerConsoleActions.TimeScalePresets;
        var scaleButtons = new (string, Action)[presets.Length];
        for (int i = 0; i < presets.Length; i++)
        {
            float v = presets[i];
            scaleButtons[i] = ($"x{v:0.##}", () => a.SetTimeScale(v));
        }
        AddRow(() => $"Scale  x{a.TimeScale:0.##}", scaleButtons);
        AddToggle("Freeze  (NumEnter)", () => a.Frozen, a.SetFrozen);
        AddRow(() => "Frame step  (Num+)", ("Step", a.FrameStep));
        AddToggle("Freeze shift clock (host)", () => a.ClockFrozen, a.SetClockFrozen);

        // ── Environment ───────────────────────────────────────────────────────
        AddSection("ENVIRONMENT");
        AddToggle("Lock preset", () => a.PresetLocked, a.SetPresetLocked);
        AddToggle("Rain", () => a.RainEnabled, a.SetRain);
        AddRow(() => "Lightning", ("Strike", a.StrikeLightning));
        AddButton(_scrollContent, "Environment Presets…", ShowPresetsPage, null);

        RefreshPage();
    }

    private void ShowPresetsPage()
    {
        ClearScrollContent();
        var a = _actions;

        AddButton(_scrollContent, "<  Back", ShowMainPage, null);
        AddToggle("Lock preset", () => a.PresetLocked, a.SetPresetLocked);

        var list = a.Presets;
        if (list == null || list.Count == 0)
            AddInfo("No presets assigned on TrailerConsoleActions.");

        for (int i = 0; i < (list?.Count ?? 0); i++)
        {
            int index = i;
            var preset = list[i];
            if (preset == null) continue;
            AddButton(_scrollContent, null, () => Invoke(() => a.ApplyPreset(index)),
                () => (a.CurrentPreset == preset ? "> " : "   ") + preset.name);
        }

        RefreshPage();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static float StepArray(float[] values, float current, int dir)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++)
            if (Mathf.Abs(values[i] - current) < Mathf.Abs(values[best] - current)) best = i;
        return values[Mathf.Clamp(best + dir, 0, values.Length - 1)];
    }

    private static float StepFocus(float current, int dir)
    {
        // Finer steps up close where focus matters most. Decrements use the band just below.
        float probe = dir < 0 ? current - 0.001f : current;
        float step = probe < 2f ? 0.1f : probe < 5f ? 0.25f : probe < 15f ? 1f : 5f;
        return Mathf.Round((current + step * dir) * 100f) / 100f;
    }

    private static readonly float[] ApertureStops = { 1f, 1.4f, 2f, 2.8f, 4f, 5.6f, 8f, 11f, 16f, 22f };

    private static float StepAperture(float current, int dir) => StepArray(ApertureStops, current, dir);

    // ─────────────────────────────────────────────────────────────────────────
    // Row builders
    // ─────────────────────────────────────────────────────────────────────────

    private void Invoke(Action action)
    {
        action?.Invoke();
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        foreach (var r in _refreshers) r();
    }

    private void AddSection(string label)
    {
        var go = new GameObject($"Section_{label}");
        go.transform.SetParent(_scrollContent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SectionHeight;
        go.AddComponent<Image>().color = SectionBgColor;

        var tmp = AddFillText(go.transform, $"── {label} ──", SectionFontSize, SectionColor,
            TextAlignmentOptions.MidlineLeft, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
    }

    private void AddInfo(string text)
    {
        AddLabel(_scrollContent, text, SubtitleHeight, SubtitleFontSize, SubtitleColor,
            FontStyles.Normal, TextAlignmentOptions.Center);
    }

    /// <summary>Full-width button showing <c>label [ON]/[OFF]</c>.</summary>
    private void AddToggle(string label, Func<bool> get, Action<bool> set)
    {
        AddButton(_scrollContent, null, () => Invoke(() => set(!get())),
            () => $"{label}   {(get() ? "<color=#5f5>[ON]</color>" : "<color=#888>[OFF]</color>")}");
    }

    /// <summary>Label + value with −/+ buttons.</summary>
    private void AddStepper(string label, Func<string> value, Action dec, Action inc)
    {
        AddRow(() => $"{label}:  {value()}", ("-", dec), ("+", inc));
    }

    /// <summary>A label (refreshed after every action) followed by small buttons.</summary>
    private void AddRow(Func<string> label, params (string Text, Action OnClick)[] buttons)
    {
        var row = new GameObject("Row");
        row.transform.SetParent(_scrollContent, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = RowHeight;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = RowSpacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Label cell
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(row.transform, false);
        var labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1f;
        labelLE.minWidth = 100f;
        var labelTmp = AddFillText(labelGO.transform, label(), RowFontSize, Color.white,
            TextAlignmentOptions.MidlineLeft, new Vector2(8f, 0f), Vector2.zero);
        _refreshers.Add(() => { if (labelTmp != null) labelTmp.text = label(); });

        foreach (var (text, onClick) in buttons)
        {
            var btn = AddButton(row.transform, text, () => Invoke(onClick), null);
            var ble = btn.GetComponent<LayoutElement>();
            ble.minWidth = ble.preferredWidth = SmallButtonWidth;
            btn.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        }
    }

    private void ClearScrollContent()
    {
        _refreshers.Clear();
        for (int i = _scrollContent.childCount - 1; i >= 0; i--)
        {
            var child = _scrollContent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    private void RefreshPage()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_outerRT);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        Canvas.ForceUpdateCanvases();
        if (_scrollRect != null)
            _scrollRect.normalizedPosition = new Vector2(0f, 1f);
        foreach (var tmp in _canvasRoot.GetComponentsInChildren<TextMeshProUGUI>())
            tmp.ForceMeshUpdate();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI construction (mirrors CheatConsoleUI)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        _canvasRoot = new GameObject("[TrailerConsole] Canvas");
        _canvasRoot.transform.SetParent(transform);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 102; // above CheatConsole (100)

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // No full-screen dim: the shot must stay visible while tuning DoF/time/presets.
        // ── Panel (left side) ─────────────────────────────────────────────────
        var panel   = new GameObject("Panel");
        panel.transform.SetParent(_canvasRoot.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0f, 0.5f);
        panelRT.anchorMax = new Vector2(0f, 0.5f);
        panelRT.pivot     = new Vector2(0f, 0.5f);
        panelRT.anchoredPosition = new Vector2(PanelMargin, 0f);
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
        outerVLG.spacing                = RowSpacing;
        outerVLG.childAlignment         = TextAnchor.UpperCenter;
        outerVLG.childControlWidth      = true;
        outerVLG.childControlHeight     = true;
        outerVLG.childForceExpandWidth  = true;
        outerVLG.childForceExpandHeight = false;

        AddLabel(outer.transform, "TRAILER CONSOLE", TitleHeight, TitleFontSize,
            TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);
        AddLabel(outer.transform, "F11 to close · Numpad hotkeys", SubtitleHeight, SubtitleFontSize,
            SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);
        _statusLabel = AddLabel(outer.transform, string.Empty, SubtitleHeight, SubtitleFontSize,
            StatusColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Scroll view ───────────────────────────────────────────────────────
        float scrollHeight = PanelHeight - 2f * PanelPadding
                             - TitleHeight - SubtitleHeight * 2f - 3f * RowSpacing;

        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(outer.transform, false);
        var svLE = svGO.AddComponent<LayoutElement>();
        svLE.minHeight       = scrollHeight;
        svLE.preferredHeight = scrollHeight;
        svLE.flexibleHeight  = 1f;

        var sv = svGO.AddComponent<ScrollRect>();
        sv.horizontal        = false;
        sv.vertical          = true;
        sv.scrollSensitivity = 30f;
        _scrollRect          = sv;

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

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(vpGO.transform, false);
        _scrollContent = contentGO.AddComponent<RectTransform>();
        _scrollContent.anchorMin = new Vector2(0f, 1f);
        _scrollContent.anchorMax = new Vector2(1f, 1f);
        _scrollContent.pivot     = new Vector2(0.5f, 1f);
        _scrollContent.offsetMin = Vector2.zero;
        _scrollContent.offsetMax = Vector2.zero;

        var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVLG.spacing                = RowSpacing;
        contentVLG.padding                = new RectOffset(0, 0, 4, 4);
        contentVLG.childAlignment         = TextAnchor.UpperCenter;
        contentVLG.childControlWidth      = true;
        contentVLG.childControlHeight     = true;
        contentVLG.childForceExpandWidth  = true;
        contentVLG.childForceExpandHeight = false;

        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        sv.content = _scrollContent;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static TextMeshProUGUI AddFillText(Transform parent, string text, int fontSize, Color color,
        TextAlignmentOptions alignment, Vector2 offsetMin, Vector2 offsetMax)
    {
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(parent, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = offsetMin;
        textRT.offsetMax = offsetMax;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.font      = TMP_Settings.defaultFontAsset;
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    private static TextMeshProUGUI AddLabel(Transform parent, string text, float height, int fontSize,
        Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;

        // Child text with fill anchors so TMP gets a properly sized rect (CheatConsoleUI fix).
        var tmp = AddFillText(go.transform, text, fontSize, color, alignment, Vector2.zero, Vector2.zero);
        tmp.fontStyle = style;
        return tmp;
    }

    /// <summary>
    /// Adds a button. When <paramref name="dynamicLabel"/> is set, the text is refreshed after every
    /// action; otherwise <paramref name="label"/> is static.
    /// </summary>
    private GameObject AddButton(Transform parent, string label, Action onClick, Func<string> dynamicLabel)
    {
        var btnGO = new GameObject("Btn");
        btnGO.transform.SetParent(parent, false);
        var le = btnGO.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = RowHeight;

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

        var tmp = AddFillText(btnGO.transform, dynamicLabel != null ? dynamicLabel() : label, RowFontSize,
            Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(12f, 0f), new Vector2(-12f, 0f));

        if (dynamicLabel != null)
            _refreshers.Add(() => { if (tmp != null) tmp.text = dynamicLabel(); });

        return btnGO;
    }
}
