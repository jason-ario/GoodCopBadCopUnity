using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// Tracks whether the player is currently driving the game with a gamepad or with
    /// keyboard/mouse. Used by helper-icon UI to swap between keyboard/mouse and gamepad prompt icons.
    ///
    /// Switching is intentionally sticky to avoid flicker: gamepads (stick drift, periodic reports)
    /// and mice emit state events constantly, so only deliberate input counts:
    ///  - Gamepad: a newly pressed button (face buttons, d-pad, shoulders, triggers, sticks, start/select).
    ///  - Keyboard: a newly pressed key.
    ///  - Mouse: a newly pressed button, scroll, or movement above a small pixel threshold.
    /// </summary>
    public static class ActiveInputDeviceTracker
    {
        public static event Action<bool> DeviceChanged;
        public static bool IsGamepad { get; private set; }

        private const float MouseMoveThresholdPixels = 4f;

        private static bool _subscribed;

        public static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            InputSystem.onEvent += OnEvent;
        }

        private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;

            if (device is Gamepad)
            {
                if (!IsGamepad && eventPtr.HasButtonPress())
                    SetIsGamepad(true);
            }
            else if (device is Keyboard)
            {
                if (IsGamepad && eventPtr.HasButtonPress())
                    SetIsGamepad(false);
            }
            else if (device is Mouse mouse)
            {
                if (IsGamepad && IsDeliberateMouseInput(eventPtr, mouse))
                    SetIsGamepad(false);
            }
        }

        private static bool IsDeliberateMouseInput(InputEventPtr eventPtr, Mouse mouse)
        {
            if (eventPtr.HasButtonPress()) return true;

            if (mouse.scroll.ReadValueFromEvent(eventPtr, out Vector2 scroll) && scroll.sqrMagnitude > 0.01f)
                return true;

            return mouse.delta.ReadValueFromEvent(eventPtr, out Vector2 delta)
                && delta.sqrMagnitude >= MouseMoveThresholdPixels * MouseMoveThresholdPixels;
        }

        private static void SetIsGamepad(bool value)
        {
            if (IsGamepad == value) return;
            IsGamepad = value;
            DeviceChanged?.Invoke(value);
        }
    }
}
