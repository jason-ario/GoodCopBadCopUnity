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

        public PlayerControlSettings(
            float mouseSensitivity,
            bool invertYAxis,
            EInputActivationMode crouchMode,
            EInputActivationMode sprintMode,
            bool runningEffectsEnabled,
            bool headBobEnabled,
            bool cameraShakeEnabled)
        {
            MouseSensitivity = mouseSensitivity;
            InvertYAxis = invertYAxis;
            CrouchMode = crouchMode;
            SprintMode = sprintMode;
            RunningEffectsEnabled = runningEffectsEnabled;
            HeadBobEnabled = headBobEnabled;
            CameraShakeEnabled = cameraShakeEnabled;
        }
    }
}
