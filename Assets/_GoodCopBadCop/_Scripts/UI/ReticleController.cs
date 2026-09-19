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

    public float normalScale = 1f;
    public float interactScale = 1.3f;
    public float lerpSpeed = 10f;

    [Header("Extract Hint")]
    [Tooltip("The TMP label that shows the extraction action text (child 'Do Text').")]
    [SerializeField] private TextMeshProUGUI _hintLabel;

    [Tooltip("The GameObject wrapping the key icon (child 'Button Tooltip').")]
    [SerializeField] private GameObject _hintKeyIcon;

    [Tooltip("Image that displays the keyboard or controller hint icon.")]
    [SerializeField] private Image _hintKeyImage;

    [Tooltip("Hint icon used while keyboard or mouse is the active input device.")]
    [SerializeField] private Sprite _keyboardHintSprite;

    [Tooltip("Hint icon used while a controller is the active input device.")]
    [SerializeField] private Sprite _gamepadHintSprite;

    private bool canInteract = false;
    private bool isTooFar = false;
    private bool isEnemyTarget = false;
    private bool isEnemyInRange = false;

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnInputDeviceChanged;
        RefreshHintIcon();
    }

    private void OnDisable()
    {
        ActiveInputDeviceTracker.DeviceChanged -= OnInputDeviceChanged;
    }

    private void OnInputDeviceChanged(bool isGamepad) => RefreshHintIcon();

    void Update()
    {
        Color targetColor = normalColor;
        float targetScale = normalScale;

        if (isEnemyTarget)
        {
            targetColor = isEnemyInRange ? enemyColor : enemyOutOfRangeColor;
            targetScale = interactScale;
        }
        else if (canInteract)
        {
            targetColor = interactColor;
            targetScale = interactScale;
        }
        else if (isTooFar)
        {
            targetColor = interactOutOfRangeColor;
            targetScale = interactScale;
        }

        reticle.color = Color.Lerp(reticle.color, targetColor, Time.deltaTime * lerpSpeed);

        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * targetScale,
            Time.deltaTime * lerpSpeed
        );
    }

    /// <summary>
    /// Updates the interact reticle state. When <paramref name="showHint"/> is true,
    /// the extract hint (key icon + action text) is shown using <paramref name="text"/>.
    /// All other text/tooltip parameters are kept for call-site compatibility but are not rendered.
    /// </summary>
    /// <param name="state">Whether the reticle is in interact mode.</param>
    /// <param name="text">Action label shown in the extract hint when <paramref name="showHint"/> is true.</param>
    /// <param name="useKeyPrompt">Unused.</param>
    /// <param name="showButtonTooltip">Unused.</param>
    /// <param name="showHint">When true, shows the extract hint elements next to the reticle.</param>
    public void SetInteractState(bool state, string text = "", bool useKeyPrompt = false, bool showButtonTooltip = true, bool showHint = false)
    {
        canInteract = state;
        if (state)
        {
            isTooFar = false;
            isEnemyTarget = false;
        }
        SetHintVisible(showHint, text);
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

    private void SetHintVisible(bool visible, string text = "")
    {
        if (_hintLabel != null)
        {
            _hintLabel.gameObject.SetActive(visible);
            if (visible)
                _hintLabel.text = text;
        }

        if (_hintKeyIcon != null)
            _hintKeyIcon.SetActive(visible);

        if (visible)
            RefreshHintIcon();
    }

    private void RefreshHintIcon()
    {
        if (_hintKeyImage == null) return;

        Sprite icon = ActiveInputDeviceTracker.IsGamepad ? _gamepadHintSprite : _keyboardHintSprite;
        if (icon != null)
            _hintKeyImage.sprite = icon;
    }
}
