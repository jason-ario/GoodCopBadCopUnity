namespace GoodCopBadCop.Settings
{
    public readonly struct PlayerControlSettings
    {
        public readonly float MouseSensitivity;
        public readonly bool InvertYAxis;
        public readonly EInputActivationMode CrouchMode;
        public readonly EInputActivationMode SprintMode;
        public readonly bool RunningEffectsEnabled;
        public readonly bool HeadBobEnabled;
        public readonly bool CameraShakeEnabled;

        /// <summary>Gamepad right-stick look sensitivity on the settings 1-100 scale (50 = the prefab's base speed).</summary>
        public readonly float ControllerLookSensitivity;

        /// <summary>Inverts the gamepad right-stick vertical look independently of the mouse.</summary>
        public readonly bool ControllerInvertYAxis;

        public PlayerControlSettings(
            float mouseSensitivity,
            bool invertYAxis,
            EInputActivationMode crouchMode,
            EInputActivationMode sprintMode,
            bool runningEffectsEnabled,
            bool headBobEnabled,
            bool cameraShakeEnabled,
            float controllerLookSensitivity,
            bool controllerInvertYAxis)
        {
            MouseSensitivity = mouseSensitivity;
            InvertYAxis = invertYAxis;
            CrouchMode = crouchMode;
            SprintMode = sprintMode;
            RunningEffectsEnabled = runningEffectsEnabled;
            HeadBobEnabled = headBobEnabled;
            CameraShakeEnabled = cameraShakeEnabled;
            ControllerLookSensitivity = controllerLookSensitivity;
            ControllerInvertYAxis = controllerInvertYAxis;
        }
    }
}
