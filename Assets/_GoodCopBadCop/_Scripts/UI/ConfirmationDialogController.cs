using System;
using System.Collections.Generic;
using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Yes/No confirmation dialog. Gamepad support:
/// <list type="bullet">
///   <item>While a gamepad is the active device, the Cancel button is focused when the dialog opens
///   (the safe default) and focus is restored if it leaves the dialog. D-pad / stick moves between
///   the buttons and A submits through the EventSystem.</item>
///   <item>Gamepad B (East) cancels. Other B handlers (<see cref="GamepadBackButtonActivator"/>,
///   <see cref="MainMenuController"/>, <see cref="PauseMenuController"/>) check
///   <see cref="IsAnyOpenOutside"/> / <see cref="CancelHandledThisFrame"/> so one press only
///   closes the dialog.</item>
/// </list>
/// </summary>
public class ConfirmationDialogController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private TextMeshProUGUI cancelButtonText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private Action onConfirm;
    private Action onCancel;
    private int _shownFrame = -1;

    private static readonly HashSet<ConfirmationDialogController> OpenDialogs = new HashSet<ConfirmationDialogController>();
    private static int _cancelHandledFrame = -1;

    /// <summary>True if any confirmation dialog is currently open.</summary>
    public static bool IsAnyOpen => OpenDialogs.Count > 0;

    /// <summary>True for the frame in which a dialog consumed gamepad B to cancel.</summary>
    public static bool CancelHandledThisFrame => _cancelHandledFrame == Time.frameCount;

    /// <summary>
    /// True if a dialog is open that is not part of <paramref name="root"/>'s hierarchy. Menus use this
    /// to leave focus and Back input to the dialog.
    /// </summary>
    public static bool IsAnyOpenOutside(Transform root)
    {
        foreach (ConfirmationDialogController dialog in OpenDialogs)
        {
            if (dialog == null) continue;
            if (root == null || !dialog.transform.IsChildOf(root)) return true;
        }
        return false;
    }

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(Confirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(Cancel);
        }
    }

    private void OnEnable()
    {
        OpenDialogs.Add(this);
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
        _shownFrame = Time.frameCount;
        if (ActiveInputDeviceTracker.IsGamepad) FocusDefault();
    }

    private void OnDisable()
    {
        OpenDialogs.Remove(this);
        ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject != null
            && eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
            eventSystem.SetSelectedGameObject(null);
    }

    private void OnDestroy()
    {
        OpenDialogs.Remove(this);

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(Confirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(Cancel);
        }
    }

    private void Update()
    {
        // The press that opened the dialog must not also cancel it.
        if (Time.frameCount == _shownFrame) return;

        if (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)
        {
            _cancelHandledFrame = Time.frameCount;
            Cancel();
            return;
        }

        if (!ActiveInputDeviceTracker.IsGamepad) return;

        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(transform))
            FocusDefault();
    }

    private void OnDeviceChanged(bool isGamepad)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        if (isGamepad)
        {
            FocusDefault();
        }
        else if (eventSystem.currentSelectedGameObject != null
                 && eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
        {
            // Mouse/keyboard took over: drop the persistent highlight so hover styling applies.
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private void FocusDefault()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        Button target = IsUsable(cancelButton) ? cancelButton : IsUsable(confirmButton) ? confirmButton : null;
        if (target != null) eventSystem.SetSelectedGameObject(target.gameObject);
    }

    private static bool IsUsable(Button button) =>
        button != null && button.gameObject.activeInHierarchy && button.IsInteractable();

    public void Show(
        string title,
        string body,
        string confirmText,
        string cancelText,
        Action confirmCallback,
        Action cancelCallback = null)
    {
        onConfirm = confirmCallback;
        onCancel = cancelCallback;

        SetText(titleText, title);
        SetText(bodyText, body);
        SetText(confirmButtonText, confirmText);
        SetText(cancelButtonText, cancelText);

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        onConfirm = null;
        onCancel = null;
        gameObject.SetActive(false);
    }

    private void Confirm()
    {
        Action callback = onConfirm;
        Hide();
        callback?.Invoke();
    }

    public void Cancel()
    {
        Action callback = onCancel;
        Hide();
        callback?.Invoke();
    }

    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }
}
