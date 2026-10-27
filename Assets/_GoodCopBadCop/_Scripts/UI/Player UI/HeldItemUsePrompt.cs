using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right HUD prompt "{0} to {verb}" ({0} = use button icon) naming what LMB / RT does with the held item right now —
/// LMB / RT is reserved for using the held item (see the input convention in IInteractable.cs).
/// The verb is the held item's tool action on the targeted object when there is one
/// (<see cref="PlayerInteractionController.TargetItemUseVerb"/>, e.g. "Stamp"), otherwise its own
/// in-place use (<see cref="PickableObject.GetHeldUseVerb"/>, e.g. "Take photo"). Hidden when the
/// item has no LMB action (e.g. a supply box).
/// The row lives in the "Held Item Controls" stack (<see cref="HeldItemControlsPrompt"/>) inside the
/// "Right HUD Stack", below "[F] to inspect" and above the reload / place / throw rows.
/// The icon swaps between the mouse and gamepad sprite via <see cref="ActiveInputDeviceTracker"/>.
///
/// Attach to the always-active "Player UI" root (not the prompt row itself, which is toggled).
/// </summary>
public class HeldItemUsePrompt : MonoBehaviour, IIconPromptDriver
{
    public System.Collections.Generic.IEnumerable<TMP_Text> DrivenLabels => new[] { _label };

    [Tooltip("Prompt row toggled while the held item has an LMB action (child of the Right HUD Stack).")]
    [SerializeField] private GameObject _root;

    [Tooltip("Image that displays the use button icon.")]
    [SerializeField] private Image _keyImage;

    [Tooltip("Label right of the icon. A label for text left of the icon is cloned from it when a translation needs one.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Icon for left mouse button.")]
    [SerializeField] private Sprite _mouseSprite;

    [Tooltip("Icon for the gamepad use button (right trigger). Fallback when no icon database is assigned.")]
    [SerializeField] private Sprite _gamepadSprite;

    [Tooltip("When assigned, the gamepad icon follows the rebindable Use Item binding.")]
    [SerializeField] private InputIconDatabase _iconDatabase;

    private IconPromptLabels _labels;
    private string _shownTemplate;

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
        string verb = paused ? null : ResolveVerb();
        bool visible = !string.IsNullOrEmpty(verb);

        if (visible && _label != null)
        {
            string template = FormatTemplate(verb);
            if (template != _shownTemplate)
            {
                _labels ??= new IconPromptLabels(_label, _keyImage);
                _labels.Apply(template);
                _shownTemplate = template;
            }
        }
        SetVisible(visible);
    }

    /// <summary>
    /// "Take photo" → "{0} to take photo" (French "Prendre une photo" → "{0} pour prendre une photo"), {0} = icon.
    /// The verb is translated before composing, since the render-time lookup can't match the combined string. Only
    /// the first letter is lowered so acronyms ("UV") survive.
    /// </summary>
    private static string FormatTemplate(string verb)
    {
        string translated = Loc.TranslateSource(verb);
        if (string.IsNullOrEmpty(translated)) return translated;
        translated = translated.Replace(Loc.Marker, string.Empty);
        string lowered = char.ToLowerInvariant(translated[0]) + translated.Substring(1);
        // {0} = use button icon, {1} = verb.
        return IconPrompt.Format(Loc.T("{0} to {1}"), lowered);
    }

    private static string ResolveVerb()
    {
        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return null;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        PickableObject held = pickup != null ? pickup.HeldObject : null;
        if (held == null) return null;

        PlayerInteractionController interaction = player.PlayerInteractionController;
        string targetVerb = interaction != null ? interaction.TargetItemUseVerb : null;
        return !string.IsNullOrEmpty(targetVerb) ? targetVerb : held.GetHeldUseVerb();
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
