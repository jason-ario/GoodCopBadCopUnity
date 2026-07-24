using GoodCopBadCop.CameraSystem;
using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [SerializeField] private CinemachineCamera camera;
    [SerializeField] NoiseSettings normalNoiseSettings;
    [SerializeField] NoiseSettings rumbleNoiseSettings;
    [SerializeField] private float amplitudeGainNormal;
    [SerializeField] private float frequencyGainNormal;
    [SerializeField] private float amplitudeGainRumble;
    [SerializeField] private float frequencyGainRumble;

    [Header("Running Shake")]
    [Tooltip("Optional noise profile used while the player is running. Falls back to normalNoiseSettings when unset.")]
    [SerializeField] private NoiseSettings runningNoiseSettings;
    [SerializeField] private float amplitudeGainRunning = 1.6f;
    [SerializeField] private float frequencyGainRunning = 2f;

    private CinemachineCameraFeedbackExtension _cameraFeedbackExtension;
    private CinemachineBasicMultiChannelPerlin _perlin;
    private Sequence _swaySequence;
    private Sequence _cameraKickSequence;
    private Vector3 _swayEulerOffset;
    private Vector3 _cameraKickEulerOffset;
    private float _swayFieldOfViewOffset;
    private float _cameraKickFieldOfViewOffset;
    private bool _isRunning;
    private bool _rumbleActive;

    private void OnDisable()
    {
        StopSway();
        StopCameraKick();
    }

    /// <summary>Called every frame by the movement controller to keep the camera shake in sync with the run state.</summary>
    public void UpdateMovementShake(bool isRunning)
    {
        if (_isRunning == isRunning)
            return;

        _isRunning = isRunning;
        RefreshMovementNoise();
    }

    private void RefreshMovementNoise()
    {
        if (_rumbleActive)
            return;

        CinemachineBasicMultiChannelPerlin perlin = GetPerlin();
        if (perlin == null)
            return;

        perlin.NoiseProfile = _isRunning && runningNoiseSettings != null ? runningNoiseSettings : normalNoiseSettings;
        perlin.AmplitudeGain = _isRunning ? amplitudeGainRunning : amplitudeGainNormal;
        perlin.FrequencyGain = _isRunning ? frequencyGainRunning : frequencyGainNormal;
    }

    private CinemachineBasicMultiChannelPerlin GetPerlin()
    {
        if (_perlin == null && camera != null)
            _perlin = camera.GetComponent<CinemachineBasicMultiChannelPerlin>();

        return _perlin;
    }

    /// <summary>Enables or disables the Cinemachine virtual camera.</summary>
    public void SetCameraActive(bool active)
    {
        if (!active)
        {
            StopSway();
            StopCameraKick();
        }

        if (camera != null)
            camera.gameObject.SetActive(active);
    }

    public void TurnOnRumble()
    {
        _rumbleActive = true;

        CinemachineBasicMultiChannelPerlin perlin = GetPerlin();
        if (perlin == null)
            return;

        perlin.NoiseProfile = rumbleNoiseSettings;
        perlin.AmplitudeGain = amplitudeGainRumble;
        perlin.FrequencyGain = frequencyGainRumble;
    }

    public void TurnOffRumble()
    {
        _rumbleActive = false;
        RefreshMovementNoise();
    }

    public void PlaySway(CameraSwaySettings settings)
    {
        if (settings == null || !settings.Enabled || camera == null || !EnsureCameraFeedbackExtension(true))
            return;

        StopSway();

        _swaySequence = CreateSwaySequence(settings)
            .OnComplete(ResetSwayOffsets)
            .OnKill(() => _swaySequence = null);
    }

    public void PlayCameraKick(CameraKickSettings settings)
    {
        if (settings == null || !settings.Enabled || camera == null || !EnsureCameraFeedbackExtension(true))
            return;

        StopCameraKick();

        _cameraKickSequence = CreateCameraKickSequence(settings)
            .OnComplete(ResetCameraKickOffsets)
            .OnKill(() => _cameraKickSequence = null);
    }

    private void StopSway()
    {
        if (_swaySequence != null)
        {
            _swaySequence.Kill(false);
            _swaySequence = null;
        }

        ResetSwayOffsets();
    }

    private void StopCameraKick()
    {
        if (_cameraKickSequence != null)
        {
            _cameraKickSequence.Kill(false);
            _cameraKickSequence = null;
        }

        ResetCameraKickOffsets();
    }

    private Sequence CreateSwaySequence(CameraSwaySettings settings)
    {
        float duration = Mathf.Max(0.01f, settings.Duration);
        Vector3 amplitude = settings.EulerAmplitude;
        float fieldOfViewOffset = settings.FieldOfViewOffset;

        Sequence sequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetTarget(this);

        switch (settings.Motion)
        {
            case ECameraSwayMotion.CigaretteDrag:
                return BuildCigaretteDragSequence(sequence, duration, amplitude);
            case ECameraSwayMotion.HealRush:
                return BuildHealRushSequence(sequence, duration, amplitude, fieldOfViewOffset);
            default:
                return BuildHeadSwaySequence(sequence, duration, amplitude);
        }
    }

    private Sequence CreateCameraKickSequence(CameraKickSettings settings)
    {
        float duration = Mathf.Max(0.01f, settings.Duration);
        float impactDuration = duration * 0.22f;
        float counterDuration = duration * 0.24f;
        float settleDuration = Mathf.Max(0.01f, duration - impactDuration - counterDuration);
        Vector3 kick = settings.EulerKick;
        Vector3 counterKick = new Vector3(-kick.x * 0.18f, -kick.y * 0.2f, -kick.z * 0.24f);
        float fieldOfViewKick = settings.FieldOfViewKick;

        return DOTween.Sequence()
            .SetUpdate(true)
            .SetTarget(this)
            .Append(DOTween.To(() => _cameraKickEulerOffset, SetCameraKickEulerOffset, kick, impactDuration)
                .SetEase(Ease.OutCubic))
            .Join(DOTween.To(() => _cameraKickFieldOfViewOffset, SetCameraKickFieldOfViewOffset, fieldOfViewKick, impactDuration)
                .SetEase(Ease.OutCubic))
            .Append(DOTween.To(() => _cameraKickEulerOffset, SetCameraKickEulerOffset, counterKick, counterDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _cameraKickEulerOffset, SetCameraKickEulerOffset, Vector3.zero, settleDuration)
                .SetEase(Ease.OutSine))
            .Insert(impactDuration, DOTween.To(() => _cameraKickFieldOfViewOffset, SetCameraKickFieldOfViewOffset, 0f, counterDuration + settleDuration)
                .SetEase(Ease.OutSine));
    }

    private Sequence BuildHeadSwaySequence(Sequence sequence, float duration, Vector3 amplitude)
    {
        float leanInDuration = duration * 0.3f;
        float swingDuration = duration * 0.4f;
        float settleDuration = Mathf.Max(0.01f, duration - leanInDuration - swingDuration);

        return sequence
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, amplitude, leanInDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, -amplitude * 0.65f, swingDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, Vector3.zero, settleDuration)
                .SetEase(Ease.OutSine));
    }

    private Sequence BuildCigaretteDragSequence(Sequence sequence, float duration, Vector3 amplitude)
    {
        float inhaleDuration = duration * 0.38f;
        float liftDuration = duration * 0.24f;
        float exhaleDuration = Mathf.Max(0.01f, duration - inhaleDuration - liftDuration);
        Vector3 inhale = new Vector3(-Mathf.Abs(amplitude.x), amplitude.y * 0.35f, amplitude.z * 0.25f);
        Vector3 lift = new Vector3(Mathf.Abs(amplitude.x) * 0.55f, -amplitude.y * 0.2f, -amplitude.z * 0.15f);

        return sequence
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, inhale, inhaleDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, lift, liftDuration)
                .SetEase(Ease.OutSine))
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, Vector3.zero, exhaleDuration)
                .SetEase(Ease.InOutSine));
    }

    private Sequence BuildHealRushSequence(Sequence sequence, float duration, Vector3 amplitude, float fieldOfViewOffset)
    {
        float pushDuration = duration * 0.32f;
        float holdDuration = duration * 0.08f;
        float releaseDuration = duration * 0.24f;
        float settleDuration = Mathf.Max(0.01f, duration - pushDuration - holdDuration - releaseDuration);
        float resetStartTime = pushDuration + holdDuration;
        Vector3 push = new Vector3(-Mathf.Abs(amplitude.x), amplitude.y, amplitude.z);
        Vector3 release = new Vector3(Mathf.Abs(amplitude.x) * 0.18f, -amplitude.y * 0.18f, -amplitude.z * 0.15f);

        return sequence
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, push, pushDuration)
                .SetEase(Ease.InOutSine))
            .Join(DOTween.To(() => _swayFieldOfViewOffset, SetSwayFieldOfViewOffset, fieldOfViewOffset, pushDuration)
                .SetEase(Ease.InOutSine))
            .AppendInterval(holdDuration)
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, release, releaseDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _swayEulerOffset, SetSwayEulerOffset, Vector3.zero, settleDuration)
                .SetEase(Ease.InOutSine))
            .Insert(resetStartTime, DOTween.To(() => _swayFieldOfViewOffset, SetSwayFieldOfViewOffset, 0f, releaseDuration + settleDuration)
                .SetEase(Ease.InOutSine));
    }

    private void SetSwayEulerOffset(Vector3 offset)
    {
        _swayEulerOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void SetCameraKickEulerOffset(Vector3 offset)
    {
        _cameraKickEulerOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void SetSwayFieldOfViewOffset(float offset)
    {
        _swayFieldOfViewOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void SetCameraKickFieldOfViewOffset(float offset)
    {
        _cameraKickFieldOfViewOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void ResetSwayOffsets()
    {
        SetSwayEulerOffset(Vector3.zero);
        SetSwayFieldOfViewOffset(0f);
    }

    private void ResetCameraKickOffsets()
    {
        SetCameraKickEulerOffset(Vector3.zero);
        SetCameraKickFieldOfViewOffset(0f);
    }

    private void ApplyCameraFeedbackOffsets()
    {
        if (!EnsureCameraFeedbackExtension(false))
            return;

        _cameraFeedbackExtension.EulerOffset = _swayEulerOffset + _cameraKickEulerOffset;
        _cameraFeedbackExtension.FieldOfViewOffset = _swayFieldOfViewOffset + _cameraKickFieldOfViewOffset;
    }

    private bool EnsureCameraFeedbackExtension(bool createIfMissing)
    {
        if (_cameraFeedbackExtension != null)
            return true;

        if (camera == null)
            return false;

        _cameraFeedbackExtension = camera.GetComponent<CinemachineCameraFeedbackExtension>();
        if (_cameraFeedbackExtension == null && createIfMissing)
            _cameraFeedbackExtension = camera.gameObject.AddComponent<CinemachineCameraFeedbackExtension>();

        return _cameraFeedbackExtension != null;
    }
}
