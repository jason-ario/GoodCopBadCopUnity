#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GoodCopBadCop.Autopilot
{
    /// <summary>
    /// Drives the game through a virtual Input System gamepad. Every gameplay input in this project
    /// has a gamepad equivalent read through <c>Gamepad.current</c> (movement/look in
    /// PlayerMovementController, interact/place/use/throw in PlayerInteractionController), so a
    /// virtual device exercises exactly the same code paths a player would.
    ///
    /// Mapping (see RebindableInput.GamepadButtonName and PlayerInteractionController):
    /// left stick = move, right stick = look, West = Interact (E), RightTrigger = LMB (primary
    /// interact / use held item), LeftTrigger = RMB (hold to place), RightShoulder = throw, North (Y) = toggle slot,
    /// South = jump / UI submit.
    /// </summary>
    public class AutopilotInputDriver : IDisposable
    {
        private Gamepad _pad;
        private GamepadState _state;
        private bool _dirty = true;
        private readonly Dictionary<GamepadButton, int> _pressFrames = new Dictionary<GamepadButton, int>();
        private int _rightTriggerFrames;
        private bool _leftTriggerHeld;

        public bool IsEnabled => _pad != null && _pad.added;

        public void Enable()
        {
            if (IsEnabled) return;
            _pad = InputSystem.AddDevice<Gamepad>("AutopilotGamepad");
            _state = new GamepadState();
            _dirty = true;
        }

        public void Dispose()
        {
            if (_pad != null && _pad.added)
                InputSystem.RemoveDevice(_pad);
            _pad = null;
        }

        public void SetMove(Vector2 move)
        {
            move = Vector2.ClampMagnitude(move, 1f);
            if (_state.leftStick == move) return;
            _state.leftStick = move;
            _dirty = true;
        }

        public void SetLook(Vector2 look)
        {
            look = Vector2.ClampMagnitude(look, 1f);
            if (_state.rightStick == look) return;
            _state.rightStick = look;
            _dirty = true;
        }

        /// <summary>Presses a face/shoulder/dpad button for <paramref name="frames"/> frames, then releases it.</summary>
        public void Press(GamepadButton button, int frames = 2)
        {
            _state = _state.WithButton(button, true);
            _pressFrames[button] = Mathf.Max(1, frames);
            _dirty = true;
        }

        /// <summary>Pulls the right trigger (LMB equivalent) briefly.</summary>
        public void PressRightTrigger(int frames = 2)
        {
            _state.rightTrigger = 1f;
            _rightTriggerFrames = Mathf.Max(1, frames);
            _dirty = true;
        }

        /// <summary>Holds or releases the left trigger (RMB equivalent, placement ghost).</summary>
        public void HoldLeftTrigger(bool held)
        {
            if (_leftTriggerHeld == held) return;
            _leftTriggerHeld = held;
            _state.leftTrigger = held ? 1f : 0f;
            _dirty = true;
        }

        public void ReleaseAll()
        {
            _state = new GamepadState();
            _pressFrames.Clear();
            _rightTriggerFrames = 0;
            _leftTriggerHeld = false;
            _dirty = true;
        }

        /// <summary>
        /// Called once per frame by the runner. Queues the current state (processed by the Input
        /// System at the start of the next frame, so wasPressedThisFrame fires exactly once) and
        /// counts down timed presses.
        /// </summary>
        public void Tick()
        {
            if (!IsEnabled) return;

            // Keep the virtual pad as Gamepad.current even if a physical pad is connected.
            if (Gamepad.current != _pad)
                _pad.MakeCurrent();

            if (_dirty)
            {
                InputSystem.QueueStateEvent(_pad, _state);
                _dirty = false;
            }

            // Count down timed presses after queueing so each press is held for at least one processed frame.
            if (_pressFrames.Count > 0)
            {
                var released = new List<GamepadButton>();
                var keys = new List<GamepadButton>(_pressFrames.Keys);
                foreach (GamepadButton b in keys)
                {
                    int left = _pressFrames[b] - 1;
                    if (left <= 0) released.Add(b);
                    else _pressFrames[b] = left;
                }

                foreach (GamepadButton b in released)
                {
                    _pressFrames.Remove(b);
                    _state = _state.WithButton(b, false);
                    _dirty = true;
                }
            }

            if (_rightTriggerFrames > 0 && --_rightTriggerFrames == 0)
            {
                _state.rightTrigger = 0f;
                _dirty = true;
            }
        }
    }
}
#endif
