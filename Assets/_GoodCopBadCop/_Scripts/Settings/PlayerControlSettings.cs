namespace GoodCopBadCop.Settings
{
    public readonly struct PlayerControlSettings
    {
        public readonly float MouseSensitivity;
        public readonly bool InvertYAxis;
        public readonly EInputActivationMode CrouchMode;
        public readonly EInputActivationMode SprintMode;

        public PlayerControlSettings(
            float mouseSensitivity,
            bool invertYAxis,
            EInputActivationMode crouchMode,
            EInputActivationMode sprintMode)
        {
            MouseSensitivity = mouseSensitivity;
            InvertYAxis = invertYAxis;
            CrouchMode = crouchMode;
            SprintMode = sprintMode;
        }
    }
}
