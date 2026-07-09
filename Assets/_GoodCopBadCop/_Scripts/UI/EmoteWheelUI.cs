using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space emote wheel overlay. Generates a ring of circular buttons at runtime,
/// tracks the mouse direction from screen centre to highlight the nearest slot, and fires
/// <see cref="OnEmoteSelected"/> when the player clicks a slot.
///
/// Lives on a hidden Canvas/Panel in the scene UI hierarchy (e.g. inside Player UI).
/// Discovered at runtime by <see cref="PlayerEmoteController"/> via the static Instance.
/// </summary>
public class EmoteWheelUI : MonoBehaviour
{
    public static EmoteWheelUI Instance { get; private set; }

    [Header("Layout")]
    [Tooltip("Radius in pixels from wheel centre to button centre.")]
    [SerializeField] private float _buttonRadius = 160f;

    [Tooltip("Diameter of each circular emote button in pixels.")]
    [SerializeField] private float _buttonSize = 90f;

    [Tooltip("Minimum mouse distance from screen centre (pixels) before a slot is highlighted.")]
    [SerializeField] private float _deadZoneRadius = 35f;

    [Header("Colours")]
    [SerializeField] private Color _normalButtonColor  = new Color(0.10f, 0.10f, 0.10f, 0.88f);
    [SerializeField] private Color _hoverButtonColor   = new Color(0.32f, 0.32f, 0.32f, 1.00f);
    [SerializeField] private Color _labelNormalColor   = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField] private Color _labelHighlightColor = Color.white;
    [SerializeField] private Color _overlayColor       = new Color(0f, 0f, 0f, 0.45f);

    [Header("Emotes")]
    [SerializeField] private EmoteDefinition[] _emotes = new EmoteDefinition[]
    {
        new EmoteDefinition { Name = "Wave",          AnimBoolName = "Waving",       Duration = 2.5f },
        new EmoteDefinition { Name = "Shrug",         AnimBoolName = "Shrug",        Duration = 2.0f },
        new EmoteDefinition { Name = "Dance",         AnimBoolName = "Dance",        Duration = 4.0f },
        new EmoteDefinition { Name = "Thumbs Up",     AnimBoolName = "ThumbsUp",     Duration = 2.0f },
        new EmoteDefinition { Name = "Puke",          AnimBoolName = "Puke",         Duration = 3.0f },
        new EmoteDefinition { Name = "Cough",         AnimBoolName = "Cough",        Duration = 2.5f },
        new EmoteDefinition { Name = "Point",         AnimBoolName = "Point",        Duration = 2.0f },
        new EmoteDefinition { Name = "Middle Finger", AnimBoolName = "MiddleFinger", Duration = 1.5f },
    };

    // Per-button runtime state
    private struct SlotUI
    {
        public RectTransform Root;
        public Image          Background;
        public Image          IconImage;
        public TextMeshProUGUI Label;
    }

    private SlotUI[] _slots;
    private int      _hoveredIndex = -1;
    private Sprite   _circleSprite;

    /// <summary>Fired with the selected emote index when the player clicks a highlighted slot.</summary>
    public event Action<int> OnEmoteSelected;

    /// <summary>Read-only access to the emote definitions so <see cref="PlayerEmoteController"/> can look up data.</summary>
    public EmoteDefinition[] Emotes => _emotes;

    // ─── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _circleSprite = CreateCircleSprite(64);
        BuildWheel();
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!gameObject.activeSelf) return;

        // Derive the hovered slot from the mouse direction relative to screen centre.
        Vector2 mousePos    = Input.mousePosition;
        Vector2 screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 dir         = mousePos - screenCentre;

        int newHover = -1;

        if (dir.magnitude >= _deadZoneRadius && _slots.Length > 0)
        {
            // Buttons start at 12 o'clock (90°) and advance clockwise.
            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float normalised = ((90f - angleDeg) % 360f + 360f) % 360f;
            float step = 360f / _slots.Length;
            newHover = Mathf.RoundToInt(normalised / step) % _slots.Length;
        }

        if (newHover != _hoveredIndex)
        {
            SetSlotHighlight(_hoveredIndex, false);
            _hoveredIndex = newHover;
            SetSlotHighlight(_hoveredIndex, true);
        }

        if (Input.GetMouseButtonDown(0) && _hoveredIndex >= 0)
        {
            OnEmoteSelected?.Invoke(_hoveredIndex);
        }
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    public void Show()
    {
        gameObject.SetActive(true);
        _hoveredIndex = -1;
    }

    public void Hide()
    {
        SetSlotHighlight(_hoveredIndex, false);
        _hoveredIndex = -1;
        gameObject.SetActive(false);
    }

    // ─── Internal ───────────────────────────────────────────────────────────

    private void BuildWheel()
    {
        // Dark overlay covering the whole panel (this RectTransform should already be
        // stretched full-screen by the prefab/scene setup).
        Image overlay = GetComponent<Image>();
        if (overlay == null)
            overlay = gameObject.AddComponent<Image>();
        overlay.color = _overlayColor;
        overlay.raycastTarget = true;

        // Wheel root anchored to centre of panel.
        GameObject wheelRoot = new GameObject("WheelRoot");
        wheelRoot.transform.SetParent(transform, false);
        RectTransform wheelRt = wheelRoot.AddComponent<RectTransform>();
        wheelRt.anchorMin = wheelRt.anchorMax = new Vector2(0.5f, 0.5f);
        wheelRt.sizeDelta = Vector2.zero;
        wheelRt.anchoredPosition = Vector2.zero;

        int count = _emotes.Length;
        _slots = new SlotUI[count];
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float angle = (90f - i * angleStep) * Mathf.Deg2Rad;
            Vector2 buttonPos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _buttonRadius;

            // ── Button root ──
            GameObject slotGO = new GameObject($"Slot_{_emotes[i].Name}");
            slotGO.transform.SetParent(wheelRoot.transform, false);
            RectTransform slotRt = slotGO.AddComponent<RectTransform>();
            slotRt.anchorMin = slotRt.anchorMax = new Vector2(0.5f, 0.5f);
            slotRt.sizeDelta = new Vector2(_buttonSize, _buttonSize);
            slotRt.anchoredPosition = buttonPos;

            // ── Circular background ──
            Image bg = slotGO.AddComponent<Image>();
            bg.sprite        = _circleSprite;
            bg.color         = _normalButtonColor;
            bg.raycastTarget = false;

            // ── Icon (centred inside the circle) ──
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            RectTransform iconRt = iconGO.AddComponent<RectTransform>();
            iconRt.anchorMin      = new Vector2(0.1f, 0.1f);
            iconRt.anchorMax      = new Vector2(0.9f, 0.9f);
            iconRt.offsetMin      = Vector2.zero;
            iconRt.offsetMax      = Vector2.zero;
            Image iconImg         = iconGO.AddComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            if (_emotes[i].Icon != null)
                iconImg.sprite = _emotes[i].Icon;
            else
                iconImg.color = new Color(1f, 1f, 1f, 0f); // invisible placeholder

            // ── Label (outward from centre, above the button) ──
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(slotGO.transform, false);
            RectTransform labelRt = labelGO.AddComponent<RectTransform>();

            // Offset the label outward from the wheel centre so it sits just outside
            // the button circle. Normalise the button position to get the outward direction.
            Vector2 outward = buttonPos.normalized;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.sizeDelta = new Vector2(110f, 22f);
            // Position relative to button: outward * half-button-size + a small gap.
            labelRt.anchoredPosition = outward * (_buttonSize * 0.55f + 8f);

            TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text      = _emotes[i].Name;
            label.fontSize  = 11f;
            label.alignment = TextAlignmentOptions.Center;
            label.color     = _labelNormalColor;
            label.raycastTarget = false;
            label.fontStyle = FontStyles.Bold;

            _slots[i] = new SlotUI
            {
                Root       = slotRt,
                Background = bg,
                IconImage  = iconImg,
                Label      = label,
            };
        }

        // ── Centre indicator dot ──
        GameObject centre = new GameObject("Centre");
        centre.transform.SetParent(wheelRoot.transform, false);
        RectTransform centreRt = centre.AddComponent<RectTransform>();
        centreRt.anchorMin = centreRt.anchorMax = new Vector2(0.5f, 0.5f);
        centreRt.sizeDelta = new Vector2(14f, 14f);
        centreRt.anchoredPosition = Vector2.zero;
        Image centreImg = centre.AddComponent<Image>();
        centreImg.sprite = _circleSprite;
        centreImg.color  = new Color(1f, 1f, 1f, 0.5f);
        centreImg.raycastTarget = false;
    }

    private void SetSlotHighlight(int index, bool highlighted)
    {
        if (index < 0 || index >= _slots.Length) return;
        _slots[index].Background.color = highlighted ? _hoverButtonColor : _normalButtonColor;
        _slots[index].Label.color      = highlighted ? _labelHighlightColor : _labelNormalColor;
    }

    /// <summary>
    /// Creates a white circle sprite of diameter <paramref name="diameter"/> pixels at runtime.
    /// Used as the button background so no external asset is required.
    /// </summary>
    private static Sprite CreateCircleSprite(int diameter)
    {
        Texture2D tex    = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
        Color[]   pixels = new Color[diameter * diameter];
        float     radius = diameter * 0.5f;
        Vector2   centre = new Vector2(radius, radius);

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                // Soft anti-aliased edge within one pixel.
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * diameter + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), diameter);
    }
}
