using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// Tracks whether the player is currently driving the game with a gamepad or with
    /// keyboard/mouse, based on the last device that produced real input. Used by helper-icon UI
    /// to swap between keyboard/mouse and gamepad prompt icons.
    /// </summary>
    public static class ActiveInputDeviceTracker
    {
        public static event Action<bool> DeviceChanged;
        public static bool IsGamepad { get; private set; }

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

            bool isGamepadDevice = device is Gamepad;
            bool isKeyboardOrMouse = device is Keyboard || device is Mouse;

            if (isGamepadDevice && !IsGamepad)
            {
                IsGamepad = true;
                DeviceChanged?.Invoke(true);
            }
            else if (isKeyboardOrMouse && IsGamepad)
            {
                IsGamepad = false;
                DeviceChanged?.Invoke(false);
            }
        }
    }
}
