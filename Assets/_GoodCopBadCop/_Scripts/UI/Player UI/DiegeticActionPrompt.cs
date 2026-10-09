using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right "[LMB / RT] {text}" prompt for cursor-driven diegetic views (e.g. "[LMB] to drag" in the
/// lever view). Diegetic views hide the Player HUD, so <see cref="HeldItemUsePrompt"/> isn't visible there;
/// this prompt lives outside the HUD and is driven explicitly by the view via <see cref="Show"/> / <see cref="Hide"/>.
/// The icon swaps between the mouse and the rebindable gamepad Use Item sprite via <see cref="ActiveInputDeviceTracker"/>.
/// An optional prefix is shown before the icon ("Hold [LMB] to rotate"); its label is cloned from
/// <see cref="_label"/> at runtime, so restyling the Label restyles both.
///
/// Attach to an always-active object outside the Player HUD (not the prompt row itself, which is toggled).
/// </summary>
public class DiegeticActionPrompt : MonoBehaviour
{
    [Tooltip("Prompt row toggled while a view has requested the prompt.")]
    [SerializeField] private GameObject _root;

    [Tooltip("Image that displays the use button icon.")]
    [SerializeField] private Image _keyImage;

    [Tooltip("Label next to the icon.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Icon for left mouse button.")]
    [SerializeField] private Sprite _mouseSprite;

    [Tooltip("Icon for the gamepad use button (right trigger). Fallback when no icon database is assigned.")]
    [SerializeField] private Sprite _gamepadSprite;

    [Tooltip("When assigned, the gamepad icon follows the rebindable Use Item binding.")]
    [SerializeField] private InputIconDatabase _iconDatabase;

    private static DiegeticActionPrompt _instance;
    private static string _requestedText;
    private static string _requestedGamepadText;
    private static Sprite _requestedGamepadSprite;
    private static string _requestedPrefix;
    private static string _requestedGamepadPrefix;

    /// <summary>Runtime clone of <see cref="_label"/> placed before the icon; shown only with a prefix.</summary>
    private TMP_Text _prefixLabel;

    /// <summary>
    /// Shows the prompt with <paramref name="text"/> next to the LMB / RT icon (e.g. "to drag").
    /// While a gamepad is active, <paramref name="gamepadText"/> and <paramref name="gamepadSprite"/>
    /// replace the label and the Use Item icon when given (e.g. "[Left Stick] to pull").
    /// <paramref name="prefix"/> / <paramref name="gamepadPrefix"/> are shown before the icon (e.g. "Hold").
    /// </summary>
    public static void Show(string text, string gamepadText = null, Sprite gamepadSprite = null,
                            string prefix = null, string gamepadPrefix = null)
    {
        _requestedText = string.IsNullOrEmpty(text) ? null : text;
        _requestedGamepadText = string.IsNullOrEmpty(gamepadText) ? null : gamepadText;
        _requestedGamepadSprite = gamepadSprite;
        _requestedPrefix = string.IsNullOrEmpty(prefix) ? null : prefix;
        _requestedGamepadPrefix = string.IsNullOrEmpty(gamepadPrefix) ? null : gamepadPrefix;
        if (_instance != null) _instance.RefreshIcon();
    }

    /// <summary>Hides the prompt.</summary>
    public static void Hide()
    {
        _requestedText = null;
        _requestedGamepadText = null;
        _requestedGamepadSprite = null;
        _requestedPrefix = null;
        _requestedGamepadPrefix = null;
    }

    private static string CurrentText =>
        ActiveInputDeviceTracker.IsGamepad && _requestedGamepadText != null ? _requestedGamepadText : _requestedText;

    private static string CurrentPrefix =>
        ActiveInputDeviceTracker.IsGamepad && _requestedGamepadPrefix != null ? _requestedGamepadPrefix : _requestedPrefix;

    // Domain reload is disabled for Play Mode, so statics survive between sessions — reset them.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _requestedText = null;
        _requestedGamepadText = null;
        _requestedGamepadSprite = null;
        _requestedPrefix = null;
        _requestedGamepadPrefix = null;
    }

    private void Awake()
    {
        _instance = this;
        BuildPrefixLabel();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
        RebindableInput.BindingChanged += OnBindingChanged;
        RefreshIcon();
        SetVisible(false);
    }

    private void OnDisable()
    {
        ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        RebindableInput.BindingChanged -= OnBindingChanged;
        SetVisible(false);
    }

    private void OnDeviceChanged(bool isGamepad) => RefreshIcon();

    private void OnBindingChanged(GameAction action)
    {
        if (action == GameAction.UseItem) RefreshIcon();
    }

    private void Update()
    {
        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        string text = CurrentText;
        bool visible = !paused && !string.IsNullOrEmpty(text);

        if (visible && _label != null && _label.text != text)
            _label.text = text;

        if (_prefixLabel != null)
        {
            string prefix = visible ? CurrentPrefix : null;
            bool showPrefix = !string.IsNullOrEmpty(prefix);
            if (showPrefix && _prefixLabel.text != prefix)
                _prefixLabel.text = prefix;
            if (_prefixLabel.gameObject.activeSelf != showPrefix)
                _prefixLabel.gameObject.SetActive(showPrefix);
        }

        SetVisible(visible);
    }

    /// <summary>Clones the label into a hidden prefix label that sits before the key icon.</summary>
    private void BuildPrefixLabel()
    {
        if (_label == null || _keyImage == null || _label.transform.parent != _keyImage.transform.parent) return;

        _prefixLabel = Instantiate(_label, _label.transform.parent);
        _prefixLabel.name = "Prefix Label";
        _prefixLabel.text = string.Empty;
        _prefixLabel.transform.SetSiblingIndex(_keyImage.transform.GetSiblingIndex());
        _prefixLabel.gameObject.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    private void RefreshIcon()
    {
        if (_keyImage == null) return;

        Sprite icon = _mouseSprite;
        if (ActiveInputDeviceTracker.IsGamepad && _requestedGamepadSprite != null)
        {
            icon = _requestedGamepadSprite;
        }
        else if (ActiveInputDeviceTracker.IsGamepad)
        {
            Sprite bound = _iconDatabase != null
                ? _iconDatabase.GetGamepadControlSprite(RebindableInput.GetGamepadBinding(GameAction.UseItem))
                : null;
            icon = bound != null ? bound : _gamepadSprite;
        }
        if (icon != null) _keyImage.sprite = icon;
    }
}
