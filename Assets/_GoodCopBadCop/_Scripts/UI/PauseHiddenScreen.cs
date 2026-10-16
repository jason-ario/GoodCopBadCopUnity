using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Hides a full-screen UI panel while the pause menu is open and shows it again on unpause.
///
/// Hiding goes through the root <see cref="CanvasGroup"/> (alpha 0, not interactable, no
/// raycasts) instead of <c>SetActive(false)</c>, so the screen keeps its <c>activeSelf</c> state
/// (which gameplay code reads as "screen is open") and never re-runs OnEnable/OnDisable side
/// effects (audio loops, subscriptions, animations). <see cref="GamepadMenuNavigator"/> ignores
/// faded controls, and the Back button activators use <c>Selectable.IsInteractable()</c>, so the
/// hidden screen takes no input while paused.
///
/// If the screen's own script writes the CanvasGroup while hidden (e.g. a fade coroutine), the
/// written values are captured as the values to restore and the group is re-hidden each frame.
/// A screen that opens while already paused starts hidden.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public class PauseHiddenScreen : MonoBehaviour
{
    private CanvasGroup _group;
    private bool _hidden;
    private float _savedAlpha = 1f;
    private bool _savedInteractable = true;
    private bool _savedBlocksRaycasts = true;

    /// <summary>Adds the component to <paramref name="screen"/> if it doesn't have one yet.</summary>
    public static PauseHiddenScreen EnsureOn(GameObject screen)
    {
        if (screen == null) return null;
        return screen.TryGetComponent(out PauseHiddenScreen existing)
            ? existing
            : screen.AddComponent<PauseHiddenScreen>();
    }

    private void Awake()
    {
        if (!TryGetComponent(out _group))
            _group = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        UIController.OnPauseMenuOpened += Hide;
        UIController.OnPauseMenuClosed += Show;

        if (UIController.Instance != null && UIController.Instance.IsPaused)
            Hide();
    }

    private void OnDisable()
    {
        UIController.OnPauseMenuOpened -= Hide;
        UIController.OnPauseMenuClosed -= Show;

        // Closed while paused: put the group back so the next open isn't stuck invisible.
        Show();
    }

    private void LateUpdate()
    {
        if (!_hidden || _group == null) return;

        // Anything the screen wrote this frame becomes the value to restore on unpause.
        if (_group.alpha > 0f) { _savedAlpha = _group.alpha; _group.alpha = 0f; }
        if (_group.interactable) { _savedInteractable = true; _group.interactable = false; }
        if (_group.blocksRaycasts) { _savedBlocksRaycasts = true; _group.blocksRaycasts = false; }
    }

    private void Hide()
    {
        if (_hidden || _group == null || !isActiveAndEnabled) return;

        _hidden = true;
        _savedAlpha = _group.alpha;
        _savedInteractable = _group.interactable;
        _savedBlocksRaycasts = _group.blocksRaycasts;

        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Drop gamepad focus held by this screen so the pause menu owns it.
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(transform))
            eventSystem.SetSelectedGameObject(null);
    }

    private void Show()
    {
        if (!_hidden || _group == null) return;

        _hidden = false;
        _group.alpha = _savedAlpha;
        _group.interactable = _savedInteractable;
        _group.blocksRaycasts = _savedBlocksRaycasts;
    }
}
