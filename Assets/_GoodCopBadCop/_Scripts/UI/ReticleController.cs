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
    [Tooltip("The TMP label shown next to the key icon: an object's interactText (ShowInteractHint objects) or the \"Hold E to …\" prompt (child 'Do Text').")]
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

    [Tooltip("Label used for the Interact button in \"Hold … to …\" prompts while a controller is active (Xbox X / PlayStation Square).")]
    [SerializeField] private string _gamepadInteractLabel = "X";

    [Header("Use Held Item Icon")]
    [Tooltip("Shown instead of the Interact icon when the held item can be used on the target (LMB). E.g. a trash bag over junk.")]
    [SerializeField] private Sprite _useMouseSprite;

    [Tooltip("Controller equivalent of the use icon (right trigger).")]
    [SerializeField] private Sprite _useGamepadSprite;

    /// <summary>True while the key icon shows the use-held-item (LMB / RT) sprite instead of Interact.</summary>
    private bool _showingUseIcon;

    [Header("Hold Ring")]
    [Tooltip("Radial-filled Image around the reticle that fills while the Interact key is held on an object with a hold action. Forced to Filled / Radial 360 from the top at runtime.")]
    [SerializeField] private Image _holdRing;

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
        if (action == GameAction.Interact) RefreshHintIcon();
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
    /// <param name="holdVerb">When set, replaces the prompt with "Hold E to {holdVerb}".</param>
    /// <param name="useItemIcon">Swap the key icon for the use-held-item icon (LMB / RT): the held item can be used on this target.</param>
    public void SetInteractState(bool state, string text = "", bool showKeyIcon = false, bool showButtonTooltip = true, bool showHint = false, string holdVerb = null, bool useItemIcon = false)
    {
        canInteract = state;
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

        if (!string.IsNullOrEmpty(holdVerb))
        {
            // The key name is spelled out in the label, so the separate icon is hidden.
            SetPrompt(false, $"Hold {GetInteractKeyLabel()} to {holdVerb}");
            return;
        }

        if (useItemIcon)
        {
            SetUseIcon(true);
            SetPrompt(true, null);
            return;
        }

        SetUseIcon(false);
        bool keyVisible = showKeyIcon && showButtonTooltip;
        SetPrompt(keyVisible, keyVisible && showHint && !string.IsNullOrEmpty(text) ? text : null);
    }

    private void SetUseIcon(bool useIcon)
    {
        if (_showingUseIcon == useIcon) return;
        _showingUseIcon = useIcon;
        RefreshHintIcon();
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
            _holdRing.fillAmount = Mathf.Clamp01(progress);
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

    /// <summary>Shows/hides the key icon and the label (null/empty label hides it).</summary>
    private void SetPrompt(bool keyIconVisible, string label)
    {
        bool labelVisible = !string.IsNullOrEmpty(label);

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
    }

    private string GetInteractKeyLabel()
    {
        return ActiveInputDeviceTracker.IsGamepad
            ? _gamepadInteractLabel
            : RebindableInput.GetDisplayName(GameAction.Interact);
    }

    private void RefreshHintIcon()
    {
        if (_hintKeyImage == null) return;

        Sprite icon = null;

        if (_showingUseIcon)
        {
            icon = ActiveInputDeviceTracker.IsGamepad ? _useGamepadSprite : _useMouseSprite;
        }
        else if (_iconDatabase != null)
        {
            icon = ActiveInputDeviceTracker.IsGamepad
                ? _iconDatabase.GetGamepadSprite(GameAction.Interact)
                : _iconDatabase.GetKeyboardMouseSprite(GameAction.Interact);
        }

        if (icon == null && !_showingUseIcon)
            icon = ActiveInputDeviceTracker.IsGamepad ? _gamepadHintSprite : _keyboardHintSprite;

        if (icon != null)
            _hintKeyImage.sprite = icon;
    }
}
