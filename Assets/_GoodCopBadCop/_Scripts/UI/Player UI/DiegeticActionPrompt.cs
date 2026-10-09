using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right "[LMB / RT] {text}" prompt for cursor-driven diegetic views (e.g. "[LMB] to drag" in the
/// lever view). Diegetic views hide the Player HUD, so <see cref="HeldItemUsePrompt"/> isn't visible there;
/// this prompt lives outside the HUD and is driven explicitly by the view via <see cref="Show"/> / <see cref="Hide"/>.
/// The icon swaps between the mouse and the rebindable gamepad Use Item sprite via <see cref="ActiveInputDeviceTracker"/>.
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

    /// <summary>Shows the prompt with <paramref name="text"/> next to the LMB / RT icon (e.g. "to drag").</summary>
    public static void Show(string text) => _requestedText = string.IsNullOrEmpty(text) ? null : text;

    /// <summary>Hides the prompt.</summary>
    public static void Hide() => _requestedText = null;

    // Domain reload is disabled for Play Mode, so statics survive between sessions — reset them.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _requestedText = null;
    }

    private void Awake()
    {
        _instance = this;
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
        bool visible = !paused && !string.IsNullOrEmpty(_requestedText);

        if (visible && _label != null && _label.text != _requestedText)
            _label.text = _requestedText;
        SetVisible(visible);
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
        if (ActiveInputDeviceTracker.IsGamepad)
        {
            Sprite bound = _iconDatabase != null
                ? _iconDatabase.GetGamepadControlSprite(RebindableInput.GetGamepadBinding(GameAction.UseItem))
                : null;
            icon = bound != null ? bound : _gamepadSprite;
        }
        if (icon != null) _keyImage.sprite = icon;
    }
}
