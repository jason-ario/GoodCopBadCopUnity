using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay anomaly debug panel toggled by the BackQuote (`) key.
/// Shows all anomalies on the current suspect at the window, grouped by category,
/// with per-anomaly toggle buttons and Activate All / Clear All header actions.
/// Built entirely at runtime — no prefab or scene wiring required.
/// Add this component to the same GameObject as <see cref="DebugConsole"/>.
/// </summary>
[RequireComponent(typeof(DebugConsole))]
public class AnomalyDebugUI : MonoBehaviour
{
    // ── Layout ────────────────────────────────────────────────────────────────

    private const float PanelWidth       = 480f;
    private const float PanelHeight      = 680f;
    private const float PanelPadding     = 20f;
    private const float TitleHeight      = 44f;
    private const float SubtitleHeight   = 18f;
    private const float SpacerHeight     = 10f;
    private const float HeaderRowHeight  = 40f;
    private const float CategoryHeight   = 26f;
    private const float RowHeight        = 42f;
    private const float RowSpacing       = 5f;
    private const float ScrollViewHeight = 480f;

    private const int TitleFontSize    = 20;
    private const int SubtitleFontSize = 12;
    private const int CategoryFontSize = 12;
    private const int RowFontSize      = 13;
    private const int BtnFontSize      = 13;

    // ── Colors ────────────────────────────────────────────────────────────────

    private static readonly Color OverlayColor     = new Color(0f,    0f,    0f,    0.55f);
    private static readonly Color PanelColor       = new Color(0.08f, 0.08f, 0.08f, 0.97f);
    private static readonly Color TitleColor       = new Color(0.4f,  0.85f, 1f,    1f);
    private static readonly Color SubtitleColor    = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color CategoryColor    = new Color(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color CategoryBgColor  = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color RowBgColor       = new Color(0.13f, 0.13f, 0.13f, 1f);
    private static readonly Color ActiveBtnColor   = new Color(0.12f, 0.60f, 0.22f, 1f);
    private static readonly Color InactiveBtnColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    private static readonly Color HdrBtnColor      = new Color(0.20f, 0.20f, 0.20f, 1f);
    private static readonly Color HdrBtnHoverColor = new Color(0.32f, 0.32f, 0.32f, 1f);
    private static readonly Color HdrBtnPressColor = new Color(0.10f, 0.10f, 0.10f, 1f);
    private static readonly Color BtnPressColor    = new Color(0.08f, 0.08f, 0.08f, 1f);

    // ── State ─────────────────────────────────────────────────────────────────

    private GameObject      _canvasRoot;
    private TextMeshProUGUI _suspectLabel;
    private RectTransform   _outerRT;
    private RectTransform   _scrollContent;
    private ScrollRect      _scrollRect;
    private bool            _isVisible;

    // ── Category descriptors ──────────────────────────────────────────────────

    private static readonly (string Label, Type CategoryType)[] Categories =
    {
        ("Documentation", typeof(DocumentationAnomaly)),
        ("Vitals",        typeof(VitalsAnomaly)),
        ("Behavior",      typeof(BehaviorAnomaly)),
        ("Physical",      typeof(PhysicalAnomaly)),
        ("Supernatural",  typeof(SupernaturalAnomaly)),
    };

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildStaticUI();
        SetVisible(false);
    }

    private void Update()
    {
        if (!GameSettings.Instance.DebugConsoleEnabled)
        {
            if (_isVisible) SetVisible(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.BackQuote))
            SetVisible(!_isVisible);
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _canvasRoot.SetActive(visible);

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            RefreshContent();
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshContent()
    {
        SuspectCharacter suspect = SuspectController.Instance?.CurrentSuspect;
        AnomalyController ac     = suspect?.AnomalyController;

        _suspectLabel.text = suspect != null
            ? $"Suspect: <b>{suspect.name}</b>"
            : "Suspect: <i>none at window</i>";

        // DestroyImmediate so old rows are gone before new ones are added —
        // Destroy() is deferred and causes ghost children on repeated opens.
        for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            DestroyImmediate(_scrollContent.GetChild(i).gameObject);

        if (ac == null)
        {
            AddScrollLabel("No active suspect — open a shift first.");
        }
        else
        {
            Anomaly[] all = ac.GetAllAnomaliesDebug();

            foreach (var (label, categoryType) in Categories)
            {
                Anomaly[] inCategory = all.Where(a => a != null && categoryType.IsInstanceOfType(a)).ToArray();
                if (inCategory.Length == 0) continue;

                AddCategoryHeader(label);
                foreach (Anomaly anomaly in inCategory)
                    AddAnomalyRow(ac, anomaly);
            }
        }

        // Rebuild outer first so svGO gets its real height (480 px) and the
        // viewport Mask has a non-zero rect before we ask the content to size itself.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_outerRT);
        // Second pass: content size-fitter recalculates with the now-correct widths.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        Canvas.ForceUpdateCanvases();

        if (_scrollRect != null)
            _scrollRect.normalizedPosition = new Vector2(0f, 1f);
    }

    private void ForceScrollContentRebuild()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_outerRT);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        Canvas.ForceUpdateCanvases();
        if (_scrollRect != null)
            _scrollRect.normalizedPosition = new Vector2(0f, 1f);
    }

    // ── Static UI construction ────────────────────────────────────────────────

    private void BuildStaticUI()
    {
        // ── Canvas ────────────────────────────────────────────────────────────
        _canvasRoot = new GameObject("[AnomalyDebug] Canvas");
        _canvasRoot.transform.SetParent(transform);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;

        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── Full-screen dim (click-outside-to-close) ──────────────────────────
        var overlay   = new GameObject("Overlay");
        overlay.transform.SetParent(_canvasRoot.transform, false);
        var overlayRT = overlay.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        var overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = OverlayColor;
        var overlayBtn = overlay.AddComponent<Button>();
        overlayBtn.targetGraphic = overlayImg;
        overlayBtn.onClick.AddListener(() => SetVisible(false));

        // ── Panel ─────────────────────────────────────────────────────────────
        var panel   = new GameObject("Panel");
        panel.transform.SetParent(_canvasRoot.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.AddComponent<Image>().color = PanelColor;

        // ── Outer VLG (non-scrolling header + scroll view) ────────────────────
        var outer   = new GameObject("Outer");
        outer.transform.SetParent(panel.transform, false);
        var outerRT = outer.AddComponent<RectTransform>();
        outerRT.anchorMin = Vector2.zero;
        outerRT.anchorMax = Vector2.one;
        outerRT.offsetMin = new Vector2(PanelPadding, PanelPadding);
        outerRT.offsetMax = new Vector2(-PanelPadding, -PanelPadding);
        _outerRT = outerRT;

        var outerVLG = outer.AddComponent<VerticalLayoutGroup>();
        outerVLG.spacing              = 6f;
        outerVLG.childAlignment       = TextAnchor.UpperCenter;
        outerVLG.childControlWidth    = true;
        outerVLG.childControlHeight   = true;
        outerVLG.childForceExpandWidth  = true;
        outerVLG.childForceExpandHeight = false;

        // Title
        AddStaticLabel(outer.transform, "ANOMALY DEBUG", TitleHeight, TitleFontSize,
            TitleColor, FontStyles.Bold, TextAlignmentOptions.Center);

        // Dynamic suspect name label
        _suspectLabel = AddStaticLabel(outer.transform, "Suspect: —", SubtitleHeight,
            SubtitleFontSize, SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // Hotkey hint
        AddStaticLabel(outer.transform, "Press ` to close  ·  Click outside to dismiss",
            SubtitleHeight, SubtitleFontSize - 1, SubtitleColor, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Header action row ─────────────────────────────────────────────────
        var hdrRow = new GameObject("HeaderRow");
        hdrRow.transform.SetParent(outer.transform, false);
        var hdrRowLE = hdrRow.AddComponent<LayoutElement>();
        hdrRowLE.minHeight = hdrRowLE.preferredHeight = HeaderRowHeight;
        var hdrRowHLG = hdrRow.AddComponent<HorizontalLayoutGroup>();
        hdrRowHLG.spacing              = 8f;
        hdrRowHLG.childControlWidth    = true;
        hdrRowHLG.childControlHeight   = true;
        hdrRowHLG.childForceExpandWidth  = true;
        hdrRowHLG.childForceExpandHeight = true;

        AddSmallButton(hdrRow.transform, "Activate All", () =>
        {
            AnomalyController ac = SuspectController.Instance?.CurrentSuspect?.AnomalyController;
            if (ac == null) return;
            foreach (Anomaly a in ac.GetAllAnomaliesDebug())
                if (a != null && !ac.activeAnomalies.Contains(a))
                    ac.DebugToggleAnomaly(a);
            RefreshContent();
        });

        AddSmallButton(hdrRow.transform, "Clear All", () =>
        {
            AnomalyController ac = SuspectController.Instance?.CurrentSuspect?.AnomalyController;
            if (ac == null) return;
            ac.InitializeClean();
            RefreshContent();
        });

        AddSmallButton(hdrRow.transform, "Refresh", () => RefreshContent());

        // Spacer
        var spacer   = new GameObject("Spacer");
        spacer.transform.SetParent(outer.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.minHeight = spacerLE.preferredHeight = SpacerHeight;

        // ── Scroll view ───────────────────────────────────────────────────────
        var svGO = new GameObject("ScrollView");
        svGO.transform.SetParent(outer.transform, false);
        var svLE = svGO.AddComponent<LayoutElement>();
        svLE.minHeight       = ScrollViewHeight;
        svLE.preferredHeight = ScrollViewHeight;
        svLE.flexibleHeight  = 1f;

        var svImg = svGO.AddComponent<Image>();
        svImg.color = new Color(0f, 0f, 0f, 0f);

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
        var vpImg = vpGO.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0f);
        vpGO.AddComponent<RectMask2D>();
        sv.viewport = vpRT;

        // Content
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(vpGO.transform, false);
        _scrollContent = contentGO.AddComponent<RectTransform>();
        _scrollContent.anchorMin = new Vector2(0f, 1f);
        _scrollContent.anchorMax = new Vector2(1f, 1f);
        _scrollContent.pivot     = new Vector2(0.5f, 1f);
        _scrollContent.offsetMin = Vector2.zero;
        _scrollContent.offsetMax = Vector2.zero;

        var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVLG.spacing              = RowSpacing;
        contentVLG.padding              = new RectOffset(0, 0, 4, 4);
        contentVLG.childAlignment       = TextAnchor.UpperLeft;
        contentVLG.childControlWidth    = true;
        contentVLG.childControlHeight   = true;
        contentVLG.childForceExpandWidth  = true;
        contentVLG.childForceExpandHeight = false;

        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        sv.content = _scrollContent;
    }

    // ── Dynamic row builders ──────────────────────────────────────────────────

    private void AddCategoryHeader(string text)
    {
        var go = new GameObject($"Cat_{text}");
        go.transform.SetParent(_scrollContent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = CategoryHeight;
        go.AddComponent<Image>().color = CategoryBgColor;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(8f, 0f);
        labelRT.offsetMax = new Vector2(-8f, 0f);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = $"── {text.ToUpper()} ──";
        tmp.fontSize  = CategoryFontSize;
        tmp.color     = CategoryColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void AddAnomalyRow(AnomalyController ac, Anomaly anomaly)
    {
        bool isActive = ac.activeAnomalies.Contains(anomaly);

        var row = new GameObject($"Row_{anomaly.GetType().Name}");
        row.transform.SetParent(_scrollContent, false);
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.minHeight = rowLE.preferredHeight = RowHeight;
        row.AddComponent<Image>().color = RowBgColor;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding              = new RectOffset(10, 8, 0, 0);
        hlg.spacing              = 8f;
        hlg.childAlignment       = TextAnchor.MiddleLeft;
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        // Anomaly name label (flexible width)
        var nameLabelGO = new GameObject("Name");
        nameLabelGO.transform.SetParent(row.transform, false);
        var nameLE = nameLabelGO.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;
        var nameTmp = nameLabelGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text      = anomaly.GetType().Name.Replace("Anomaly", "");
        nameTmp.fontSize  = RowFontSize;
        nameTmp.color     = Color.white;
        nameTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Toggle button (fixed width)
        var btnGO = new GameObject("ToggleBtn");
        btnGO.transform.SetParent(row.transform, false);
        var btnLE = btnGO.AddComponent<LayoutElement>();
        btnLE.minWidth       = 72f;
        btnLE.preferredWidth = 72f;
        btnLE.flexibleWidth  = 0f;

        Color normalColor      = isActive ? ActiveBtnColor   : InactiveBtnColor;
        Color highlightedColor = isActive
            ? new Color(ActiveBtnColor.r + 0.1f, ActiveBtnColor.g + 0.1f, ActiveBtnColor.b + 0.1f, 1f)
            : new Color(0.35f, 0.35f, 0.35f, 1f);

        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = normalColor;

        var btn    = btnGO.AddComponent<Button>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor      = normalColor;
        colors.highlightedColor = highlightedColor;
        colors.pressedColor     = BtnPressColor;
        colors.selectedColor    = normalColor;
        colors.colorMultiplier  = 1f;
        btn.colors        = colors;
        btn.targetGraphic = btnImg;

        var btnLabelGO = new GameObject("Label");
        btnLabelGO.transform.SetParent(btnGO.transform, false);
        var btnLabelRT = btnLabelGO.AddComponent<RectTransform>();
        btnLabelRT.anchorMin = Vector2.zero;
        btnLabelRT.anchorMax = Vector2.one;
        btnLabelRT.offsetMin = Vector2.zero;
        btnLabelRT.offsetMax = Vector2.zero;
        var btnTmp = btnLabelGO.AddComponent<TextMeshProUGUI>();
        btnTmp.text      = isActive ? "ON" : "OFF";
        btnTmp.fontSize  = BtnFontSize;
        btnTmp.color     = Color.white;
        btnTmp.fontStyle = FontStyles.Bold;
        btnTmp.alignment = TextAlignmentOptions.Center;

        btn.onClick.AddListener(() =>
        {
            ac.DebugToggleAnomaly(anomaly);
            RefreshContent();
        });
    }

    private void AddScrollLabel(string text)
    {
        var go = new GameObject("MsgLabel");
        go.transform.SetParent(_scrollContent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 40f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = SubtitleFontSize;
        tmp.color     = SubtitleColor;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    // ── Static label factory ──────────────────────────────────────────────────

    private static TextMeshProUGUI AddStaticLabel(Transform parent, string text, float height,
        int fontSize, Color color, FontStyles style, TextAlignmentOptions alignment)
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
        return tmp;
    }

    private static void AddSmallButton(Transform parent, string label, Action onClick)
    {
        var btnGO = new GameObject($"HdrBtn_{label}");
        btnGO.transform.SetParent(parent, false);

        var img  = btnGO.AddComponent<Image>();
        img.color = HdrBtnColor;

        var btn    = btnGO.AddComponent<Button>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor      = HdrBtnColor;
        colors.highlightedColor = HdrBtnHoverColor;
        colors.pressedColor     = HdrBtnPressColor;
        colors.selectedColor    = HdrBtnColor;
        colors.colorMultiplier  = 1f;
        btn.colors        = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = BtnFontSize;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
    }
}
