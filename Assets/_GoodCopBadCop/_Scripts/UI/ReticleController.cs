using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReticleController : MonoBehaviour
{
    public Image reticle;

    [Header("Colors")]
    [Tooltip("Nothing interactable and no weapon target under the reticle.")]
    public Color normalColor = new Color(0.6f, 0.6f, 0.6f, 0.4f);

    [Tooltip("Aiming at an interactable that is within interaction range.")]
    public Color interactColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("Aiming at an interactable that is out of interaction range.")]
    public Color interactOutOfRangeColor = new Color(1f, 1f, 1f, 0.4f);

    [Tooltip("Holding a weapon and aiming at a living enemy that is within the weapon's attack range.")]
    public Color enemyColor = new Color(1f, 0f, 0f, 1f);

    [Tooltip("Holding a weapon and aiming at a living enemy that is out of the weapon's attack range.")]
    public Color enemyOutOfRangeColor = new Color(1f, 0f, 0f, 0.4f);

    [Header("Scale")]
    [Tooltip("Reticle scale in every state except ready-to-interact.")]
    public float normalScale = 1f;

    [Tooltip("Reticle scale while aiming at an interactable within range (ready to interact).")]
    public float interactScale = 1.3f;

    [Tooltip("How fast color and scale blend toward their target values.")]
    public float lerpSpeed = 10f;

    [Header("Interact Prompt")]
    [Tooltip("The TMP label shown next to the key icon in the tap row: the action verb, or an object's interactText (ShowInteractHint objects) (child 'Do Text').")]
    [SerializeField] private TextMeshProUGUI _hintLabel;

    [Tooltip("The GameObject wrapping the Interact key icon (child 'Button Tooltip').")]
    [SerializeField] private GameObject _hintKeyIcon;

    [Tooltip("Image that displays the Interact key / controller button icon.")]
    [SerializeField] private Image _hintKeyImage;

    [Tooltip("When assigned, the key icon follows the player's current Interact binding (and the gamepad icon set). Falls back to the fixed sprites below when unassigned.")]
    [SerializeField] private InputIconDatabase _iconDatabase;

    [Tooltip("Fallback hint icon used while keyboard or mouse is the active input device.")]
    [SerializeField] private Sprite _keyboardHintSprite;

    [Tooltip("Fallback hint icon used while a controller is the active input device.")]
    [SerializeField] private Sprite _gamepadHintSprite;

    [Header("Prompt Rows")]
    [Tooltip("Row holding the Interact key icon and label (the tap action). Cloned at runtime into a \"[LMB] {verb}\" use row and a text-only \"Hold to …\" row, stacked beneath it by the parent's VerticalLayoutGroup.")]
    [SerializeField] private GameObject _tapRow;

    // Runtime-built "[LMB / RT] {verb}" row (clone of _tapRow): the held item's tool action on the target.
    private GameObject _useRow;
    private TextMeshProUGUI _useLabel;
    private Image _useKeyImage;

    // Runtime-built text-only "Hold to {verb}" row (clone of _tapRow without the icon).
    private GameObject _holdRow;
    private TextMeshProUGUI _holdLabel;

    [Header("Use Held Item Icon")]
    [Tooltip("Icon on the use row while keyboard/mouse is active (LMB): the held item's tool action on the target, e.g. a stamp over a folder.")]
    [SerializeField] private Sprite _useMouseSprite;

    [Tooltip("Fallback controller icon on the use row (right trigger) when the icon database has no sprite for the current Use Item binding.")]
    [SerializeField] private Sprite _useGamepadSprite;

    [Header("Hold Ring")]
    [Tooltip("Radial-filled Image around the reticle that fills while the Interact key is held on an object with a hold action. Forced to Filled / Radial 360 from the top at runtime.")]
    [SerializeField] private Image _holdRing;

    [Tooltip("Gap (in the reticle's local UI units) between the hold ring and the left edge of the \"Hold to …\" row it sits beside.")]
    [SerializeField] private float _holdRingTextGap = 10f;

    // True when the hold prompt lives on the tap row ("[E] Hold to {verb}", hold is the only action);
    // false when it is the separate "Hold to {verb}" row beneath the tap row.
    private bool _holdOnTapRow;
    private readonly Vector3[] _cornerBuffer = new Vector3[4];

    private bool canInteract = false;
    private bool isTooFar = false;
    private bool isEnemyTarget = false;
    private bool isEnemyInRange = false;

    private void Awake()
    {
        if (_holdRing != null)
        {
            _holdRing.type = Image.Type.Filled;
            _holdRing.fillMethod = Image.FillMethod.Radial360;
            _holdRing.fillOrigin = (int)Image.Origin360.Top;
            _holdRing.fillClockwise = true;
            _holdRing.fillAmount = 0f;
            _holdRing.raycastTarget = false;
            _holdRing.gameObject.SetActive(false);
        }

        BuildUseRow();
        BuildHoldRow();
        SetPrompt(false, null);
    }

    /// <summary>
    /// Clones <see cref="_tapRow"/> (icon kept) into a sibling "[LMB / RT] {verb}" row directly beneath
    /// it, shown when the held item has a tool action on the target. Built before the hold row so the
    /// stack order is: tap row, use row, hold row.
    /// </summary>
    private void BuildUseRow()
    {
        if (_tapRow == null || _hintLabel == null || _hintKeyImage == null) return;
        if (_hintLabel.transform.parent != _tapRow.transform || !_hintKeyImage.transform.IsChildOf(_tapRow.transform))
        {
            Debug.LogWarning("[ReticleController] Hint label and key image must be inside the tap row.", this);
            return;
        }

        string labelPath = GetRelativePath(_tapRow.transform, _hintLabel.transform);
        string imagePath = GetRelativePath(_tapRow.transform, _hintKeyImage.transform);

        _useRow = Instantiate(_tapRow, _tapRow.transform.parent);
        _useRow.name = "Use Row";
        _useRow.transform.SetSiblingIndex(_tapRow.transform.GetSiblingIndex() + 1);

        Transform label = _useRow.transform.Find(labelPath);
        Transform image = _useRow.transform.Find(imagePath);
        _useLabel = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
        _useKeyImage = image != null ? image.GetComponent<Image>() : null;

        if (label != null) label.gameObject.SetActive(true);
        if (_hintKeyIcon != null)
        {
            Transform icon = _useRow.transform.Find(GetRelativePath(_tapRow.transform, _hintKeyIcon.transform));
            if (icon != null) icon.gameObject.SetActive(true);
        }

        _useRow.SetActive(false);
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        string path = target.name;
        for (Transform t = target.parent; t != null && t != root; t = t.parent)
            path = t.name + "/" + path;
        return path;
    }

    /// <summary>
    /// Clones <see cref="_tapRow"/> into a sibling text-only "Hold to {verb}" row (the icon clone is
    /// removed — the tap row above already shows the key). The row's left padding is pulled in by the
    /// label's <see cref="TMPWidthFitter"/> padding so the text starts at the same x as the tap row's icon.
    /// </summary>
    private void BuildHoldRow()
    {
        if (_tapRow == null || _hintLabel == null || _hintKeyIcon == null) return;
        if (_hintLabel.transform.parent != _tapRow.transform || _hintKeyIcon.transform.parent != _tapRow.transform)
        {
            Debug.LogWarning("[ReticleController] Hint label and key icon must be direct children of the tap row.", this);
            return;
        }

        int labelIndex = _hintLabel.transform.GetSiblingIndex();
        int iconIndex = _hintKeyIcon.transform.GetSiblingIndex();

        _holdRow = Instantiate(_tapRow, _tapRow.transform.parent);
        _holdRow.name = "Hold Row";
        _holdRow.transform.SetSiblingIndex(_tapRow.transform.GetSiblingIndex() + (_useRow != null ? 2 : 1));

        Transform row = _holdRow.transform;
        Transform label = row.GetChild(labelIndex);
        GameObject iconClone = row.GetChild(iconIndex).gameObject;
        iconClone.transform.SetParent(null, false);
        Destroy(iconClone);

        _holdLabel = label.GetComponent<TextMeshProUGUI>();
        label.gameObject.SetActive(true);

        var layout = row.GetComponent<HorizontalOrVerticalLayoutGroup>();
        var fitter = label.GetComponent<TMPWidthFitter>();
        if (layout != null && fitter != null)
            layout.padding.left -= Mathf.RoundToInt(fitter.HorizontalPadding);

        _holdRow.SetActive(false);
    }

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnInputDeviceChanged;
        RebindableInput.BindingChanged += OnBindingChanged;
        RefreshHintIcon();
    }

    private void OnDisable()
    {
        ActiveInputDeviceTracker.DeviceChanged -= OnInputDeviceChanged;
        RebindableInput.BindingChanged -= OnBindingChanged;
    }

    private void OnInputDeviceChanged(bool isGamepad) => RefreshHintIcon();

    private void OnBindingChanged(GameAction action)
    {
        if (action == GameAction.Interact || action == GameAction.UseItem) RefreshHintIcon();
    }

    void Update()
    {
        Color targetColor = normalColor;
        float targetScale = normalScale;

        if (isEnemyTarget)
        {
            targetColor = isEnemyInRange ? enemyColor : enemyOutOfRangeColor;
        }
        else if (canInteract)
        {
            // Only the "ready to interact" state (in range) scales the reticle up.
            targetColor = interactColor;
            targetScale = interactScale;
        }
        else if (isTooFar)
        {
            targetColor = interactOutOfRangeColor;
        }

        reticle.color = Color.Lerp(reticle.color, targetColor, Time.deltaTime * lerpSpeed);

        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * targetScale,
            Time.deltaTime * lerpSpeed
        );
    }

    /// <summary>
    /// Updates the interact reticle state and the Interact key prompt next to it.
    /// </summary>
    /// <param name="state">Whether the reticle is in interact mode.</param>
    /// <param name="text">The target's interactText, shown next to the key icon when <paramref name="showHint"/> is true.</param>
    /// <param name="showKeyIcon">Show the Interact key icon (pressing Interact does something here).</param>
    /// <param name="showButtonTooltip">Extra gate for the key icon; false hides it regardless of <paramref name="showKeyIcon"/>.</param>
    /// <param name="showHint">Also show <paramref name="text"/> next to the key icon.</param>
    /// <param name="holdVerb">Secondary hold action: a text-only "Hold to {holdVerb}" row beneath the tap row, or the tap row's label ("[E] Hold to {holdVerb}") when tapping does nothing.</param>
    /// <param name="useItemVerb">The held item's tool action on this target (LMB / RT), shown as its own "[LMB] {verb}" row beneath the tap row. Null hides the row.</param>
    /// <param name="actionVerb">Action name shown next to the Interact key ("Pick up", "Open", "Insert fuse"…).</param>
    public void SetInteractState(bool state, string text = "", bool showKeyIcon = false, bool showButtonTooltip = true, bool showHint = false, string holdVerb = null, string useItemVerb = null, string actionVerb = null)
    {
        canInteract = state;
        _holdOnTapRow = false;
        if (state)
        {
            isTooFar = false;
            isEnemyTarget = false;
        }

        if (!state)
        {
            SetPrompt(false, null);
            return;
        }

        bool keyVisible = showKeyIcon && showButtonTooltip;
        bool hasHold = !string.IsNullOrEmpty(holdVerb);

        if (!keyVisible)
        {
            // Hold is the only Interact action: one "[E] Hold to {verb}" row, so the key icon is still shown.
            _holdOnTapRow = hasHold;
            SetPrompt(hasHold, hasHold ? $"Hold to {holdVerb}" : null, useVerb: useItemVerb);
            return;
        }

        string tapLabel = showHint && !string.IsNullOrEmpty(text) ? text : actionVerb;

        // "[E] {verb}" above "[LMB] {useItemVerb}" above a text-only "Hold to {holdVerb}" row.
        SetPrompt(true, tapLabel, holdVerb, useItemVerb);
    }

    /// <summary>
    /// Fills the hold ring around the reticle. 0 (or less) hides it; 1 is a full ring.
    /// Driven every frame by <see cref="PlayerInteractionController"/> while a hold-interact charges.
    /// </summary>
    public void SetHoldProgress(float progress)
    {
        if (_holdRing == null) return;

        bool visible = progress > 0f;
        if (_holdRing.gameObject.activeSelf != visible)
            _holdRing.gameObject.SetActive(visible);
        if (visible)
        {
            _holdRing.fillAmount = Mathf.Clamp01(progress);
            AlignHoldRingToPrompt();
        }
    }

    /// <summary>
    /// Places the hold ring like an icon beside the active "Hold to …" line: vertically centred on
    /// that row and <see cref="_holdRingTextGap"/> to the left of its leftmost visible element (the
    /// key icon for "[E] Hold to …", the label for the separate hold row). Recomputed every frame while
    /// filling so it tracks the reticle's interact scale lerp and layout changes.
    /// </summary>
    private void AlignHoldRingToPrompt()
    {
        GameObject rowObject = _holdOnTapRow ? _tapRow : _holdRow;
        if (rowObject == null || !rowObject.activeInHierarchy) return;

        RectTransform ringRect = _holdRing.rectTransform;
        Transform space = ringRect.parent;
        if (space == null) return;

        float left = float.MaxValue;
        foreach (Transform child in rowObject.transform)
        {
            if (!child.gameObject.activeSelf || child is not RectTransform childRect) continue;
            childRect.GetWorldCorners(_cornerBuffer);
            for (int i = 0; i < 4; i++)
                left = Mathf.Min(left, space.InverseTransformPoint(_cornerBuffer[i]).x);
        }
        if (left == float.MaxValue) return;

        ((RectTransform)rowObject.transform).GetWorldCorners(_cornerBuffer);
        float centreY = (space.InverseTransformPoint(_cornerBuffer[0]).y + space.InverseTransformPoint(_cornerBuffer[1]).y) * 0.5f;

        float halfWidth = ringRect.rect.width * ringRect.localScale.x * 0.5f;
        ringRect.localPosition = new Vector3(left - _holdRingTextGap - halfWidth, centreY, 0f);
    }

    /// <summary>
    /// Marks the reticle as aiming at a living enemy while the player is holding a weapon
    /// capable of hurting it. Takes priority over the interact/too-far states while active.
    /// </summary>
    /// <param name="state">Whether a valid weapon target is under the reticle.</param>
    /// <param name="inRange">Whether the target is within the held weapon's attack range.</param>
    public void SetEnemyState(bool state, bool inRange = false)
    {
        isEnemyTarget = state;
        isEnemyInRange = inRange;
        if (state)
        {
            canInteract = false;
            isTooFar = false;
            SetHintVisible(false);
        }
    }

    /// <summary>Hides the reticle entirely.</summary>
    public void DisableReticle()
    {
        reticle.enabled = false;
        SetHintVisible(false);
        SetHoldProgress(0f);
    }

    /// <summary>Shows the reticle.</summary>
    public void EnableReticle()
    {
        reticle.enabled = true;
    }

    /// <summary>
    /// Marks the reticle as "target too far away", switching it to the too-far color.
    /// </summary>
    public void SetTooFarState(bool state)
    {
        isTooFar = state;
        if (state)
        {
            canInteract = false;
            isEnemyTarget = false;
            SetHintVisible(false);
        }
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void SetHintVisible(bool visible, string text = "") => SetPrompt(visible, visible ? text : null);

    /// <summary>
    /// Shows/hides the tap row (key icon + label; null/empty label hides the label), the
    /// "[LMB] {useVerb}" row (null/empty useVerb hides it) and the "Hold to {holdVerb}" row
    /// (null/empty holdVerb hides it).
    /// </summary>
    private void SetPrompt(bool keyIconVisible, string label, string holdVerb = null, string useVerb = null)
    {
        bool labelVisible = !string.IsNullOrEmpty(label);

        if (_useRow != null)
        {
            bool useVisible = !string.IsNullOrEmpty(useVerb);
            if (useVisible && _useLabel != null && _useLabel.text != useVerb)
                _useLabel.text = useVerb;
            if (_useRow.activeSelf != useVisible)
            {
                _useRow.SetActive(useVisible);
                if (useVisible) RefreshHintIcon();
            }
        }

        if (_hintLabel != null)
        {
            if (_hintLabel.gameObject.activeSelf != labelVisible)
                _hintLabel.gameObject.SetActive(labelVisible);
            if (labelVisible && _hintLabel.text != label)
                _hintLabel.text = label;
        }

        if (_hintKeyIcon != null && _hintKeyIcon.activeSelf != keyIconVisible)
        {
            _hintKeyIcon.SetActive(keyIconVisible);
            if (keyIconVisible)
                RefreshHintIcon();
        }

        if (_tapRow != null)
        {
            bool tapRowVisible = keyIconVisible || labelVisible;
            if (_tapRow.activeSelf != tapRowVisible)
                _tapRow.SetActive(tapRowVisible);
        }

        if (_holdRow != null)
        {
            bool holdVisible = !string.IsNullOrEmpty(holdVerb);
            if (holdVisible)
            {
                string text = $"Hold to {holdVerb}";
                if (_holdLabel != null && _holdLabel.text != text)
                    _holdLabel.text = text;
            }
            if (_holdRow.activeSelf != holdVisible)
                _holdRow.SetActive(holdVisible);
        }
    }

    private void RefreshHintIcon()
    {
        Sprite interactIcon = null;
        if (_iconDatabase != null)
        {
            interactIcon = ActiveInputDeviceTracker.IsGamepad
                ? _iconDatabase.GetGamepadSprite(GameAction.Interact)
                : _iconDatabase.GetKeyboardMouseSprite(GameAction.Interact);
        }
        if (interactIcon == null)
            interactIcon = ActiveInputDeviceTracker.IsGamepad ? _gamepadHintSprite : _keyboardHintSprite;

        if (_hintKeyImage != null && interactIcon != null)
            _hintKeyImage.sprite = interactIcon;

        if (_useKeyImage != null)
        {
            Sprite useIcon = _useMouseSprite;
            if (ActiveInputDeviceTracker.IsGamepad)
            {
                Sprite bound = _iconDatabase != null
                    ? _iconDatabase.GetGamepadControlSprite(RebindableInput.GetGamepadBinding(GameAction.UseItem))
                    : null;
                useIcon = bound != null ? bound : _useGamepadSprite;
            }
            if (useIcon != null) _useKeyImage.sprite = useIcon;
        }
    }
}
