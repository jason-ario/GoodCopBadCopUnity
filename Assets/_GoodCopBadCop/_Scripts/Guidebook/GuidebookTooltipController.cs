using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Page-turn prompts shown while the guidebook is open: a left chevron on the left edge of the
/// screen and a right chevron on the right edge, each with the input that turns that way
/// (A / D on keyboard, left stick left / right on gamepad). The key icons swap automatically
/// with <see cref="ActiveInputDeviceTracker"/>. A side hides when there's no page in that direction.
///
/// The controller itself stays always active so its static-event subscriptions
/// are never dropped. Only the <see cref="_tooltipContainer"/> child is toggled.
/// </summary>
public class GuidebookTooltipController : MonoBehaviour
{
    [Tooltip("Root container that holds both tooltip panels — toggled on guidebook open/close.")]
    [SerializeField] private GameObject _tooltipContainer;

    [Header("Next page (right side)")]
    [Tooltip("Right-edge prompt, shown when a next page exists.")]
    [SerializeField] private GameObject _nextPageTooltip;
    [SerializeField] private Image  _nextKeyIcon;
    [SerializeField] private Sprite _nextKeyboardSprite;
    [SerializeField] private Sprite _nextGamepadSprite;

    [Header("Previous page (left side)")]
    [Tooltip("Left-edge prompt, shown when a previous page exists.")]
    [SerializeField] private GameObject _prevPageTooltip;
    [SerializeField] private Image  _prevKeyIcon;
    [SerializeField] private Sprite _prevKeyboardSprite;
    [SerializeField] private Sprite _prevGamepadSprite;

    private GuidebookPageController _pageController;

    private void Awake()
    {
        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(false);
    }

    private void OnEnable()
    {
        GuidebookController.OnGuidebookOpened += HandleGuidebookOpened;
        GuidebookController.OnGuidebookClosed += HandleGuidebookClosed;

        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
        RefreshIcons();
    }

    private void OnDisable()
    {
        GuidebookController.OnGuidebookOpened -= HandleGuidebookOpened;
        GuidebookController.OnGuidebookClosed -= HandleGuidebookClosed;
        ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
    }

    private void Update()
    {
        if (_tooltipContainer == null || !_tooltipContainer.activeSelf || _pageController == null) return;

        SetActive(_nextPageTooltip, _pageController.HasNextPage);
        SetActive(_prevPageTooltip, _pageController.HasPreviousPage);
    }

    private void OnDeviceChanged(bool isGamepad) => RefreshIcons();

    private void RefreshIcons()
    {
        bool gamepad = ActiveInputDeviceTracker.IsGamepad;
        SetIcon(_nextKeyIcon, gamepad ? _nextGamepadSprite : _nextKeyboardSprite);
        SetIcon(_prevKeyIcon, gamepad ? _prevGamepadSprite : _prevKeyboardSprite);
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image != null && sprite != null) image.sprite = sprite;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private void HandleGuidebookOpened()
    {
        // Bind to the local player's first-person guidebook — the scene also holds body-rig
        // and cutscene copies, so a global Find could pick the wrong one.
        _pageController = GuidebookController.Local != null ? GuidebookController.Local.PageController : null;

        RefreshIcons();
        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(true);
    }

    private void HandleGuidebookClosed()
    {
        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(false);
    }
}
