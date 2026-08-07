using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Selects the first interactable button in <see cref="_buttons"/> only once the player
/// actually starts navigating with a gamepad/keyboard (D-pad, left stick, or arrow keys).
/// Until then no button is highlighted, so the menu doesn't look "pre-hovered" when opened
/// with a mouse. From the moment navigation input is detected, the InputSystemUIInputModule
/// drives D-pad / left-stick navigation, Submit (A), and Cancel (B) automatically through
/// Unity's EventSystem. Moving the mouse clears the selection so hover styling takes over again.
///
/// Attach to any menu root that contains a list of buttons to navigate via controller.
/// Assign the ordered list of Buttons in the Inspector to define navigation order.
/// </summary>
public class GamepadMenuNavigator : MonoBehaviour
{
    [Tooltip("Ordered list of buttons to navigate. Only active, interactable entries are selectable.")]
    [SerializeField] private List<Button> _buttons;

    [Tooltip("Minimum stick displacement (0-1) required to count as an intentional navigation move.")]
    [SerializeField] private float _stickDeadzone = 0.5f;

    private Vector2 _lastMousePosition;
    private bool _hasMouseBaseline;

    private void OnEnable()
    {
        _hasMouseBaseline = false;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        // If the mouse moves, defer to hover-based highlighting instead of a persistent selection.
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        if (!_hasMouseBaseline)
        {
            _lastMousePosition = mousePos;
            _hasMouseBaseline = true;
        }
        else if (mousePos != _lastMousePosition)
        {
            _lastMousePosition = mousePos;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                !IsEditingInputField(EventSystem.current.currentSelectedGameObject))
                EventSystem.current.SetSelectedGameObject(null);
            return;
        }

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            return;

        if (NavigationInputDetected())
            SelectFirst();
    }

    /// <summary>True if the given selection is an input field actively being edited (typing),
    /// which should keep focus even while the mouse moves elsewhere.</summary>
    private static bool IsEditingInputField(GameObject selected)
    {
        if (selected == null) return false;

        TMP_InputField tmpField = selected.GetComponent<TMP_InputField>();
        if (tmpField != null && tmpField.isFocused) return true;

        InputField uiField = selected.GetComponent<InputField>();
        if (uiField != null && uiField.isFocused) return true;

        return false;
    }

    private bool NavigationInputDetected()
    {
        Gamepad gp = Gamepad.current;
        if (gp != null)
        {
            Vector2 stick = gp.leftStick.ReadValue();
            if (stick.sqrMagnitude >= _stickDeadzone * _stickDeadzone) return true;
            if (gp.dpad.up.wasPressedThisFrame || gp.dpad.down.wasPressedThisFrame ||
                gp.dpad.left.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame)
                return true;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame ||
                kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    /// <summary>Sets the EventSystem selection to the first active, interactable button.</summary>
    public void SelectFirst()
    {
        if (_buttons == null || EventSystem.current == null) return;

        foreach (Button btn in _buttons)
        {
            if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
            {
                EventSystem.current.SetSelectedGameObject(btn.gameObject);
                return;
            }
        }
    }

    /// <summary>
    /// Refreshes the selection after the button list changes at runtime
    /// (e.g. the Continue button becomes visible after a save file is found), but only
    /// if a selection is already active — it won't force a highlight while the mouse is in control.
    /// </summary>
    public void RefreshSelection()
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            SelectFirst();
    }
}
