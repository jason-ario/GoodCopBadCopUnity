using System.Collections.Generic;
using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Gamepad (and keyboard-arrow) navigation for the settings menu plus the gamepad rebind column.
    ///
    /// Focus model: a single linear list = visible rows of the active tab, followed by the footer
    /// buttons (Save, Restore Default, Back). Up/down walks the list (initial delay + repeat, unscaled
    /// time), left/right adjusts the focused row (dropdown cycles, slider steps) or moves between footer
    /// buttons. A = activate, B = close popup / close menu, Y = Restore Defaults, LB/RB = previous/next tab.
    /// Navigation is driven here instead of through EventSystem selection, so the menu's Buttons have
    /// navigation disabled and stale selections are released while the menu is open.
    /// </summary>
    public sealed partial class SettingsRedesignPreviewController
    {
        private const int TabCount = 4;
        private const float NavStickDeadzone = .5f;
        // Left/right on the stick changes values (e.g. flips "Invert Y"), so it needs a firmer, clearly
        // horizontal push than vertical scrolling does, and is locked after a vertical step until the
        // stick returns to neutral. Otherwise wobble while scrolling down silently toggled settings.
        private const float NavStickAdjustThreshold = .8f;
        private const float NavStickAdjustDominance = 2f;
        private const float NavStickNeutral = .25f;
        private const float NavInitialRepeatDelay = .35f;
        private const float NavRepeatInterval = .09f;
        private const float SliderStep = 5f;
        private const float MouseOverrideThresholdPixels = 4f;
        private const float GamepadCellX = 560f;
        private const float GamepadCellWidth = 190f;
        private const float GamepadDividerX = 535f;
        private const float ScrollPadding = 8f;
        private const string KeyRebindPrompt = "Press any key/button…";
        private const string GamepadRebindPrompt = "Press a button…";
        private const string GamepadCellPrefix = "<size=65%><color=#8A8F8A>PAD</color></size>  ";

        private static readonly Color LockedBindingTextColor = new Color(.42f, .44f, .42f, .55f);
        private static readonly Color DropdownOptionTextColor = new Color(.75f, .77f, .75f, 1f);

        // -1 = nothing focused (pointer mode). [0, RowCount) = rows, [RowCount, FocusableCount) = footer buttons.
        private int focusIndex = -1;
        private Button hoveredFooterButton;
        private readonly List<Button> footerButtonsBuffer = new List<Button>();

        private Vector2 mouseBaseline;
        private bool hasMouseBaseline;
        private int openedFrame = -1;
        private int lastDialogFrame = -10;
        private bool focusOnOpenPending;

        private int verticalHeldDirection;
        private float verticalNextRepeatTime;
        private int horizontalHeldDirection;
        private float horizontalNextRepeatTime;
        private bool stickHorizontalLocked;

        private int dropdownHighlightIndex = -1;
        private readonly List<Image> dropdownOptionImages = new List<Image>();
        private readonly List<TMP_Text> dropdownOptionLabels = new List<TMP_Text>();

        private int _awaitingGamepadRebindIndex = -1;
        private int gamepadRebindStartFrame;
        private bool gamepadRebindWaitForSubmitRelease;

        private int RowCount => activeSettings == null ? 0 : Mathf.Min(activeSettings.Length, rows.Count);
        private int FocusableCount => RowCount + GetFooterButtons().Count;
        private bool IsConfirmationDialogOpen => confirmationDialog != null && confirmationDialog.gameObject.activeInHierarchy;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void OnGamepadNavigationEnabled()
        {
            openedFrame = Time.frameCount;
            focusOnOpenPending = true;
            hasMouseBaseline = false;
            focusIndex = -1;
            ResetNavigationRepeat();
            ReleaseStaleEventSystemSelection();
        }

        private void OnGamepadNavigationDisabled()
        {
            focusOnOpenPending = false;
            ClearFocus();
        }

        private void OnBindingChanged(GameAction action)
        {
            // Gamepad swaps change two rows, so refresh every rebind row on the open tab.
            if (!isInitialized || activeSettings == null) return;
            for (int i = 0; i < RowCount; i++)
                if (activeSettings[i].Control == Control.Rebind)
                    BindRow(i);
        }

        // ── Per-frame navigation ───────────────────────────────────────────────────

        private void UpdateGamepadNavigation()
        {
            if (isCloseRequested || !isActiveAndEnabled || activeSettings == null) return;

            // The Restore Defaults dialog owns input while open, and for one frame after it closes so the
            // press that dismissed it doesn't also act on the menu.
            if (IsConfirmationDialogOpen)
            {
                lastDialogFrame = Time.frameCount;
                ResetNavigationRepeat();
                return;
            }

            ReleaseStaleEventSystemSelection();
            UpdateMouseOverride();

            // Ignore the press that opened the menu.
            if (Time.frameCount == openedFrame || Time.frameCount - lastDialogFrame <= 1) return;

            if (focusOnOpenPending)
            {
                focusOnOpenPending = false;
                if (ActiveInputDeviceTracker.IsGamepad && focusIndex < 0) SetFocus(0);
            }

            Gamepad gamepad = Gamepad.current;
            Keyboard keyboard = Keyboard.current;

            bool padSubmit = gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
            bool keySubmit = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool padCancel = gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
            bool padRestore = gamepad != null && gamepad.buttonNorth.wasPressedThisFrame;
            bool padPrevTab = gamepad != null && gamepad.leftShoulder.wasPressedThisFrame;
            bool padNextTab = gamepad != null && gamepad.rightShoulder.wasPressedThisFrame;

            ReadNavigation(gamepad, keyboard, out int vertical, out int horizontal);
            int verticalStep = ConsumeRepeat(vertical, ref verticalHeldDirection, ref verticalNextRepeatTime);
            int horizontalStep = ConsumeRepeat(horizontal, ref horizontalHeldDirection, ref horizontalNextRepeatTime);

            if (padCancel)
            {
                if (openDropdownIndex >= 0) CloseDropdown();
                else RequestClose();
                return;
            }

            if (openDropdownIndex >= 0)
            {
                HandleDropdownPopupInput(verticalStep, padSubmit || keySubmit);
                return;
            }

            if (padPrevTab || padNextTab)
            {
                int next = ((int)activeTab + (padNextTab ? 1 : -1) + TabCount) % TabCount;
                RequestTab((Tab)next);
                return;
            }

            if (padRestore)
            {
                RequestRestoreDefaults();
                return;
            }

            if (focusIndex < 0)
            {
                if (verticalStep != 0 || horizontalStep != 0 || padSubmit || keySubmit) SetFocus(0);
                return;
            }

            if (verticalStep != 0)
            {
                MoveFocus(-verticalStep);
                return;
            }

            if (horizontalStep != 0)
            {
                AdjustFocused(horizontalStep);
                return;
            }

            if (padSubmit) SubmitFocused(true);
            else if (keySubmit) SubmitFocused(false);
        }

        private void ReadNavigation(Gamepad gamepad, Keyboard keyboard, out int vertical, out int horizontal)
        {
            vertical = 0;
            horizontal = 0;

            if (gamepad != null)
            {
                if (gamepad.dpad.up.isPressed) vertical += 1;
                if (gamepad.dpad.down.isPressed) vertical -= 1;
                if (gamepad.dpad.right.isPressed) horizontal += 1;
                if (gamepad.dpad.left.isPressed) horizontal -= 1;

                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.magnitude < NavStickNeutral) stickHorizontalLocked = false;

                if (vertical == 0 && horizontal == 0)
                {
                    float absX = Mathf.Abs(stick.x);
                    float absY = Mathf.Abs(stick.y);
                    if (absY >= NavStickDeadzone && absY >= absX)
                    {
                        vertical = stick.y > 0f ? 1 : -1;
                        stickHorizontalLocked = true;
                    }
                    else if (!stickHorizontalLocked && absX >= NavStickAdjustThreshold && absX >= absY * NavStickAdjustDominance)
                    {
                        horizontal = stick.x > 0f ? 1 : -1;
                    }
                }
            }

            if (keyboard != null && vertical == 0 && horizontal == 0)
            {
                if (keyboard.upArrowKey.isPressed) vertical += 1;
                if (keyboard.downArrowKey.isPressed) vertical -= 1;
                if (keyboard.rightArrowKey.isPressed) horizontal += 1;
                if (keyboard.leftArrowKey.isPressed) horizontal -= 1;
            }

            // One axis at a time; vertical wins on diagonals.
            if (vertical != 0) horizontal = 0;
        }

        private static int ConsumeRepeat(int direction, ref int heldDirection, ref float nextRepeatTime)
        {
            float now = Time.unscaledTime;
            if (direction == 0)
            {
                heldDirection = 0;
                return 0;
            }

            if (direction != heldDirection)
            {
                heldDirection = direction;
                nextRepeatTime = now + NavInitialRepeatDelay;
                return direction;
            }

            if (now >= nextRepeatTime)
            {
                nextRepeatTime = now + NavRepeatInterval;
                return direction;
            }

            return 0;
        }

        private void ResetNavigationRepeat()
        {
            verticalHeldDirection = 0;
            horizontalHeldDirection = 0;
            // Treat a stick still held from before (opening the menu / closing a dialog) as non-neutral.
            stickHorizontalLocked = true;
        }

        /// <summary>Moving the mouse a few pixels hides gamepad focus so pointer interaction takes over.</summary>
        private void UpdateMouseOverride()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 position = mouse.position.ReadValue();
            if (!hasMouseBaseline || focusIndex < 0)
            {
                mouseBaseline = position;
                hasMouseBaseline = true;
                return;
            }

            if ((position - mouseBaseline).sqrMagnitude >= MouseOverrideThresholdPixels * MouseOverrideThresholdPixels)
            {
                mouseBaseline = position;
                ClearFocus();
            }
        }

        /// <summary>
        /// Releases EventSystem selections that would let the UI input module submit/navigate behind our
        /// back (a menu Button selected by a mouse click, or the hidden main/pause menu button that opened us).
        /// </summary>
        private void ReleaseStaleEventSystemSelection()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null) return;
            if (confirmationDialog != null && selected.transform.IsChildOf(confirmationDialog.transform)) return;

            if (!selected.activeInHierarchy || selected.transform.IsChildOf(transform))
                eventSystem.SetSelectedGameObject(null);
        }

        // ── Focus ──────────────────────────────────────────────────────────────────

        private List<Button> GetFooterButtons()
        {
            footerButtonsBuffer.Clear();
            AddFooterButton(saveButton);
            AddFooterButton(restoreDefaultButton);
            AddFooterButton(backButton);
            return footerButtonsBuffer;
        }

        private void AddFooterButton(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                footerButtonsBuffer.Add(button);
        }

        private void SetFocus(int index)
        {
            int count = FocusableCount;
            focusIndex = count == 0 ? -1 : Mathf.Clamp(index, 0, count - 1);
            hasMouseBaseline = false;
            RefreshFocusVisuals();
            if (focusIndex >= 0 && focusIndex < RowCount) ScrollRowIntoView(focusIndex);
        }

        private void ClearFocus()
        {
            if (focusIndex < 0 && hoveredFooterButton == null) return;
            focusIndex = -1;
            RefreshFocusVisuals();
        }

        private void MoveFocus(int delta)
        {
            int count = FocusableCount;
            if (count == 0) return;
            int target = Mathf.Clamp(focusIndex + delta, 0, count - 1);
            if (target != focusIndex) SetFocus(target);
        }

        private void RefocusAfterTabChange(bool tabChanged)
        {
            if (!gameObject.activeInHierarchy) return;

            if (tabChanged)
            {
                if (focusIndex >= 0 || ActiveInputDeviceTracker.IsGamepad) SetFocus(0);
            }
            else if (focusIndex >= 0)
            {
                SetFocus(Mathf.Min(focusIndex, FocusableCount - 1));
            }
        }

        private void RefreshFocusVisuals()
        {
            for (int i = 0; i < RowCount; i++)
                ApplyRowStyle(i);

            Button footerTarget = null;
            if (focusIndex >= RowCount)
            {
                List<Button> footer = GetFooterButtons();
                int footerIndex = focusIndex - RowCount;
                if (footerIndex < footer.Count) footerTarget = footer[footerIndex];
            }

            if (footerTarget == hoveredFooterButton) return;
            SendPointerHover(hoveredFooterButton, false);
            hoveredFooterButton = footerTarget;
            SendPointerHover(footerTarget, true);
        }

        /// <summary>Reuses each footer button's own hover styling (Selectable transition / TextButton animator).</summary>
        private static void SendPointerHover(Button button, bool entered)
        {
            if (button == null || EventSystem.current == null) return;
            PointerEventData data = new PointerEventData(EventSystem.current);
            if (entered) ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerEnterHandler);
            else ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerExitHandler);
        }

        /// <summary>Base row colors + locked keyboard binding dimming + gamepad focus highlight.</summary>
        private void ApplyRowStyle(int index)
        {
            if (!IsValidRowIndex(index)) return;

            Row row = rows[index];
            Setting setting = activeSettings[index];
            bool focused = index == focusIndex;
            bool lockedKeyboard = setting.Control == Control.Rebind && !RebindableInput.IsKeyboardRebindable(setting.RebindAction);
            bool lockedGamepad = setting.Control == Control.Rebind && !RebindableInput.IsGamepadRebindable(setting.RebindAction);

            ApplyRowAppearance(row, IsSupported(setting));
            if (lockedKeyboard) row.Value.color = LockedBindingTextColor;
            if (row.GamepadValue != null) row.GamepadValue.color = lockedGamepad ? LockedBindingTextColor : row.Label.color;

            if (row.Background != null)
            {
                row.Background.sprite = focused && dropdownSelectedSprite != null ? dropdownSelectedSprite : normalBackgroundSprite;
                if (focused) row.Background.color = Color.white;
            }

            if (!focused) return;
            row.Label.color = Color.white;
            if (!lockedKeyboard) row.Value.color = Color.white;
            if (row.GamepadValue != null && !lockedGamepad) row.GamepadValue.color = Color.white;
        }

        private void ScrollRowIntoView(int index)
        {
            if (settingsScrollRect == null || index < 0 || index >= rows.Count) return;

            RectTransform viewport = settingsScrollRect.viewport != null
                ? settingsScrollRect.viewport
                : settingsScrollRect.transform as RectTransform;
            RectTransform content = settingsScrollRect.content != null ? settingsScrollRect.content : rowsRoot;
            if (viewport == null || content == null) return;

            Bounds rowBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, rows[index].Root);
            Rect viewRect = viewport.rect;

            float delta = 0f;
            if (rowBounds.max.y + ScrollPadding > viewRect.yMax)
                delta = rowBounds.max.y + ScrollPadding - viewRect.yMax;
            else if (rowBounds.min.y - ScrollPadding < viewRect.yMin)
                delta = rowBounds.min.y - ScrollPadding - viewRect.yMin;

            if (Mathf.Approximately(delta, 0f)) return;

            settingsScrollRect.StopMovement();
            Vector2 position = content.anchoredPosition;
            position.y -= delta;
            content.anchoredPosition = position;
            settingsScrollRect.verticalNormalizedPosition = Mathf.Clamp01(settingsScrollRect.verticalNormalizedPosition);
        }

        // ── Actions on the focused element ─────────────────────────────────────────

        private void AdjustFocused(int direction)
        {
            int rowCount = RowCount;
            if (focusIndex >= rowCount)
            {
                int footerCount = GetFooterButtons().Count;
                if (footerCount == 0) return;
                int footerIndex = Mathf.Clamp(focusIndex - rowCount + direction, 0, footerCount - 1);
                SetFocus(rowCount + footerIndex);
                return;
            }

            Setting setting = activeSettings[focusIndex];
            if (!IsSupported(setting)) return;

            switch (setting.Control)
            {
                case Control.Dropdown:
                    int optionCount = setting.Options.Length;
                    if (optionCount > 1)
                        SelectDropdownOption(focusIndex, ((setting.Index + direction) % optionCount + optionCount) % optionCount);
                    break;
                case Control.Slider:
                    StepSlider(focusIndex, direction);
                    break;
            }
        }

        private void StepSlider(int index, int direction)
        {
            Setting setting = activeSettings[index];
            // Snap to the step grid: 52 -> 55 / 50, 1 -> 5.
            float target = direction > 0
                ? Mathf.Floor(setting.Value / SliderStep) * SliderStep + SliderStep
                : Mathf.Ceil(setting.Value / SliderStep) * SliderStep - SliderStep;
            target = Mathf.Clamp(target, setting.MinValue, setting.MaxValue);
            if (Mathf.Approximately(target, setting.Value)) return;
            SetSliderValue(index, target);
        }

        private void SubmitFocused(bool fromGamepad)
        {
            int rowCount = RowCount;
            if (focusIndex >= rowCount)
            {
                List<Button> footer = GetFooterButtons();
                int footerIndex = focusIndex - rowCount;
                if (footerIndex < footer.Count) footer[footerIndex].onClick.Invoke();
                return;
            }

            Setting setting = activeSettings[focusIndex];
            if (!IsSupported(setting)) return;

            switch (setting.Control)
            {
                case Control.Dropdown:
                    ToggleDropdown(focusIndex);
                    break;
                case Control.Rebind:
                    if (fromGamepad) BeginGamepadRebind(focusIndex);
                    else BeginRebind(focusIndex);
                    break;
            }
        }

        // ── Dropdown popup ─────────────────────────────────────────────────────────

        private void HandleDropdownPopupInput(int verticalStep, bool submit)
        {
            int count = dropdownOptionImages.Count;
            if (count == 0) return;

            if (verticalStep != 0)
            {
                dropdownHighlightIndex = Mathf.Clamp(dropdownHighlightIndex - verticalStep, 0, count - 1);
                RefreshDropdownOptionVisuals();
                return;
            }

            if (submit && dropdownHighlightIndex >= 0)
                SelectDropdownOption(openDropdownIndex, dropdownHighlightIndex);
        }

        private void RefreshDropdownOptionVisuals()
        {
            if (!IsValidRowIndex(openDropdownIndex)) return;
            Setting setting = activeSettings[openDropdownIndex];
            Sprite highlightSprite = dropdownSelectedSprite != null ? dropdownSelectedSprite : normalBackgroundSprite;

            for (int i = 0; i < dropdownOptionImages.Count; i++)
            {
                bool highlighted = i == dropdownHighlightIndex;
                if (dropdownOptionImages[i] != null)
                    dropdownOptionImages[i].sprite = highlighted ? highlightSprite : normalBackgroundSprite;
                if (i < dropdownOptionLabels.Count && dropdownOptionLabels[i] != null)
                    dropdownOptionLabels[i].color = highlighted || i == setting.Index ? Color.white : DropdownOptionTextColor;
            }
        }

        // ── Gamepad rebinding ──────────────────────────────────────────────────────

        private void BeginGamepadRebind(int index)
        {
            if (!IsValidRowIndex(index)) return;
            Setting setting = activeSettings[index];
            if (setting.Control != Control.Rebind || !RebindableInput.IsGamepadRebindable(setting.RebindAction)) return;

            CancelRebind();
            CancelGamepadRebind();
            CloseDropdown();

            _awaitingGamepadRebindIndex = index;
            gamepadRebindStartFrame = Time.frameCount;
            // Capture only after the A that started the rebind has been released.
            gamepadRebindWaitForSubmitRelease = Gamepad.current != null && Gamepad.current.buttonSouth.isPressed;
            BindRow(index);
        }

        private void CaptureGamepadRebindInput()
        {
            if (!IsValidRowIndex(_awaitingGamepadRebindIndex))
            {
                _awaitingGamepadRebindIndex = -1;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CancelGamepadRebind();
                return;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null || Time.frameCount <= gamepadRebindStartFrame) return;

            if (gamepadRebindWaitForSubmitRelease)
            {
                if (gamepad.buttonSouth.isPressed) return;
                gamepadRebindWaitForSubmitRelease = false;
            }

            if (gamepad.startButton.wasPressedThisFrame)
            {
                CancelGamepadRebind();
                return;
            }

            if (RebindableInput.TryGetPressedGamepadControl(out string controlPath))
            {
                int index = _awaitingGamepadRebindIndex;
                GameAction action = activeSettings[index].RebindAction;
                _awaitingGamepadRebindIndex = -1;
                // Raises BindingChanged for this action (and a swapped one), which refreshes the rows.
                RebindableInput.SetGamepadBinding(action, controlPath);
                if (IsValidRowIndex(index)) BindRow(index);
            }
        }

        private void CancelGamepadRebind()
        {
            int index = _awaitingGamepadRebindIndex;
            _awaitingGamepadRebindIndex = -1;
            if (IsValidRowIndex(index)) BindRow(index);
        }

        // ── Gamepad binding cell ───────────────────────────────────────────────────

        private void BindGamepadCell(Row row, int index, Setting setting)
        {
            bool rebind = setting.Control == Control.Rebind;
            if (rebind) EnsureGamepadCell(row);

            if (row.GamepadDivider != null) row.GamepadDivider.gameObject.SetActive(rebind);
            if (row.GamepadValue == null) return;

            row.GamepadValue.gameObject.SetActive(rebind);
            if (!rebind) return;

            row.GamepadValue.text = index == _awaitingGamepadRebindIndex
                ? GamepadRebindPrompt
                : GamepadCellPrefix + RebindableInput.GetGamepadDisplayName(setting.RebindAction);

            bool rebindable = RebindableInput.IsGamepadRebindable(setting.RebindAction);
            row.GamepadButton.interactable = rebindable;
            row.GamepadButton.onClick.RemoveAllListeners();
            if (rebindable) row.GamepadButton.onClick.AddListener(() => BeginGamepadRebind(index));
        }

        /// <summary>
        /// Creates the gamepad column for a pooled row: a text cell left of the keyboard/mouse value
        /// (which stays at its prefab position), with its own divider copied from the row's "Value Divider".
        /// </summary>
        private void EnsureGamepadCell(Row row)
        {
            if (row.GamepadValue != null || row.Value == null) return;

            GameObject cellObject = new GameObject(
                "Gamepad Value",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(Button));
            RectTransform cellRect = cellObject.GetComponent<RectTransform>();
            cellRect.SetParent(row.Root, false);

            RectTransform valueRect = row.Value.rectTransform;
            cellRect.anchorMin = valueRect.anchorMin;
            cellRect.anchorMax = valueRect.anchorMax;
            cellRect.pivot = valueRect.pivot;
            cellRect.sizeDelta = new Vector2(GamepadCellWidth, valueRect.sizeDelta.y);
            cellRect.anchoredPosition = new Vector2(GamepadCellX, valueRect.anchoredPosition.y);

            TextMeshProUGUI text = cellObject.GetComponent<TextMeshProUGUI>();
            text.font = row.Value.font;
            text.fontSize = row.Value.fontSize;
            text.fontStyle = row.Value.fontStyle;
            text.alignment = row.Value.alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            text.raycastTarget = true;

            Button button = cellObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = text;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            row.GamepadValue = text;
            row.GamepadButton = button;

            RectTransform sourceDivider = row.Root.Find("Value Divider") as RectTransform;
            if (sourceDivider != null && row.GamepadDivider == null)
            {
                RectTransform divider = Instantiate(sourceDivider, row.Root);
                divider.name = "Gamepad Divider";
                divider.anchoredPosition = new Vector2(GamepadDividerX, sourceDivider.anchoredPosition.y);
                row.GamepadDivider = divider;
            }
        }
    }
}
