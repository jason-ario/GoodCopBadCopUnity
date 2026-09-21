using GoodCopBadCop.CameraSystem;
using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [SerializeField] private CinemachineCamera camera;
    [Tooltip("The actual render Camera whose near clip plane is temporarily tightened for close-up hand-held items (cigarette, pills, etc).")]
    [SerializeField] private Camera renderCamera;
    [Tooltip("Near clip plane distance to use while the player is holding a close-up item near the camera (smoking, drinking pills, etc).")]
    [SerializeField] private float smokingNearClipPlane = 0.05f;
    [Tooltip("Seconds to ease the near clip plane between its default and tightened values.")]
    [SerializeField] private float nearClipPlaneLerpDuration = 0.2f;
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
    [Tooltip("Seconds to ease the running camera shake (head bob) in/out. Part of \"Running Effects\" — only engages while actually moving forward while sprinting.")]
    [SerializeField] private float runningShakeLerpDuration = 0.3f;

    [Header("Running FOV/Speed Lines")]
    [Tooltip("Master toggle for the running FOV widen and speed-line overlay. Driven by the Gameplay settings menu (\"Running Effects\").")]
    [SerializeField] private bool runningEffectsEnabled = true;
    [Tooltip("Degrees added to the camera's field of view while sprinting.")]
    [SerializeField] private float runningFieldOfViewOffset = 5f;
    [Tooltip("Seconds to ease the FOV offset in/out when the run state changes.")]
    [SerializeField] private float runningFovLerpDuration = 0.3f;
    [Tooltip("Speed-line particle system (Vefects VFX_Vignette_Speed), parented under the render camera so it stays screen-aligned. Its material's _OpacityMultiply is tweened to fade it in/out.")]
    [SerializeField] private ParticleSystem speedLinesParticleSystem;
    [Tooltip("Seconds of sustained sprinting required before the speed lines fade in, so brief taps of sprint don't flicker the overlay.")]
    [SerializeField] private float speedLinesSustainedRunDelay = 0.4f;
    [Range(0f, 1f)]
    [Tooltip("Maximum opacity of the speed-line overlay once fully faded in.")]
    [SerializeField] private float speedLinesMaxAlpha = 1f;
    [Tooltip("Seconds to fade the speed-line overlay in/out.")]
    [SerializeField] private float speedLinesFadeDuration = 0.3f;

    private static readonly int OpacityMultiplyId = Shader.PropertyToID("_OpacityMultiply");

    private CinemachineCameraFeedbackExtension _cameraFeedbackExtension;
    private CinemachineBasicMultiChannelPerlin _perlin;
    private CinemachineImpulseListener _impulseListener;
    private Sequence _swaySequence;
    private Sequence _cameraKickSequence;
    private Vector3 _swayEulerOffset;
    private Vector3 _cameraKickEulerOffset;
    private float _swayFieldOfViewOffset;
    private float _cameraKickFieldOfViewOffset;
    private float _runningFieldOfViewOffsetCurrent;
    private Tween _runningFovTween;
    private Tween _speedLinesTween;
    private float _runningHeldTime;
    private Material _speedLinesMaterialInstance;
    private float _speedLinesOpacityCurrent;
    private bool _runningEffectsActive;
    private Tween _runningShakeTween;
    private float _shakeBlend;
    private bool _rumbleActive;
    private bool _headBobEnabled = true;
    private bool _cameraShakeEnabled = true;
    private float _defaultNearClipPlane;
    private bool _nearClipPlaneCached;
    private Tween _nearClipPlaneTween;

    private void OnDisable()
    {
        StopSway();
        StopCameraKick();
        _runningFovTween?.Kill();
        _speedLinesTween?.Kill();
        _runningShakeTween?.Kill();
        _nearClipPlaneTween?.Kill();
        SetRunningFieldOfViewOffset(0f);
        SetSpeedLinesAlpha(0f);
        _runningHeldTime = 0f;
    }

    private void Update()
    {
        if (!_runningEffectsActive || !runningEffectsEnabled)
        {
            if (_runningHeldTime != 0f)
                _runningHeldTime = 0f;

            return;
        }

        if (_runningHeldTime < speedLinesSustainedRunDelay)
        {
            _runningHeldTime += Time.deltaTime;

            if (_runningHeldTime >= speedLinesSustainedRunDelay)
                FadeSpeedLines(true);
        }
    }

    /// <summary>
    /// Called every frame by the movement controller to keep the camera shake, FOV widen, and
    /// speed-line overlay in sync with the run state. All three are part of "Running Effects" and
    /// only engage while the player is both sprinting AND actually moving forward — holding sprint
    /// while stationary or only strafing/backpedaling should not trigger them.
    /// </summary>
    public void UpdateMovementShake(bool isRunning, bool isMovingForward)
    {
        bool runningEffectsActive = isRunning && isMovingForward;
        if (_runningEffectsActive == runningEffectsActive)
            return;

        _runningEffectsActive = runningEffectsActive;
        ApplyRunningShake(runningEffectsActive, animate: true);
        UpdateRunningFieldOfView(runningEffectsActive);

        if (!runningEffectsActive)
        {
            _runningHeldTime = 0f;
            FadeSpeedLines(false);
        }
    }

    /// <summary>Enables or disables the running FOV widen, speed-line overlay, and running camera shake, e.g. from the Gameplay settings menu.</summary>
    public void SetRunningEffectsEnabled(bool isEnabled)
    {
        runningEffectsEnabled = isEnabled;

        if (!isEnabled)
        {
            _runningHeldTime = 0f;
            ApplyRunningShake(false, animate: true);
            UpdateRunningFieldOfView(false);
            FadeSpeedLines(false);
        }
        else if (_runningEffectsActive)
        {
            ApplyRunningShake(true, animate: true);
            UpdateRunningFieldOfView(true);
        }
    }

    private void UpdateRunningFieldOfView(bool isRunning)
    {
        if (!EnsureCameraFeedbackExtension(true))
            return;

        float target = isRunning && runningEffectsEnabled ? runningFieldOfViewOffset : 0f;

        _runningFovTween?.Kill();
        _runningFovTween = DOTween.To(
                () => _runningFieldOfViewOffsetCurrent,
                SetRunningFieldOfViewOffset,
                target,
                Mathf.Max(0.01f, runningFovLerpDuration))
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetTarget(this);
    }

    /// <summary>
    /// Enables or disables the idle/running camera noise ("head bob"), e.g. from the Gameplay
    /// settings menu. Does not affect the dedicated rumble effect (see TurnOnRumble).
    /// </summary>
    public void SetHeadBobEnabled(bool isEnabled)
    {
        if (_headBobEnabled == isEnabled)
            return;

        _headBobEnabled = isEnabled;
        SetShakeBlend(_shakeBlend);
    }

    private void SetRunningFieldOfViewOffset(float offset)
    {
        _runningFieldOfViewOffsetCurrent = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void FadeSpeedLines(bool show)
    {
        if (!EnsureSpeedLinesReady())
            return;

        float target = show && runningEffectsEnabled ? speedLinesMaxAlpha : 0f;

        _speedLinesTween?.Kill();
        _speedLinesTween = DOTween.To(
                () => _speedLinesOpacityCurrent,
                SetSpeedLinesAlpha,
                target,
                Mathf.Max(0.01f, speedLinesFadeDuration))
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetTarget(this);
    }

    private void SetSpeedLinesAlpha(float alpha)
    {
        _speedLinesOpacityCurrent = alpha;

        if (_speedLinesMaterialInstance != null)
            _speedLinesMaterialInstance.SetFloat(OpacityMultiplyId, alpha);
    }

    private bool EnsureSpeedLinesReady()
    {
        if (_speedLinesMaterialInstance != null)
            return true;

        if (speedLinesParticleSystem == null)
            return false;

        ParticleSystemRenderer renderer = speedLinesParticleSystem.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return false;

        _speedLinesMaterialInstance = renderer.material;
        _speedLinesMaterialInstance.SetFloat(OpacityMultiplyId, 0f);

        if (!speedLinesParticleSystem.isPlaying)
            speedLinesParticleSystem.Play();

        return true;
    }

    /// <summary>
    /// Blends the Cinemachine noise between the normal and running shake settings.
    /// When animate is true, eases the blend over runningShakeLerpDuration (used when the run
    /// state changes); when false, snaps immediately (used e.g. after rumble ends).
    /// </summary>
    private void ApplyRunningShake(bool active, bool animate)
    {
        if (_rumbleActive)
            return;

        CinemachineBasicMultiChannelPerlin perlin = GetPerlin();
        if (perlin == null)
            return;

        bool useRunningProfile = active && runningEffectsEnabled;
        float target = useRunningProfile ? 1f : 0f;

        _runningShakeTween?.Kill();

        if (useRunningProfile)
            perlin.NoiseProfile = runningNoiseSettings != null ? runningNoiseSettings : normalNoiseSettings;

        if (!animate)
        {
            if (!useRunningProfile)
                perlin.NoiseProfile = normalNoiseSettings;

            SetShakeBlend(target);
            return;
        }

        _runningShakeTween = DOTween.To(
                () => _shakeBlend,
                SetShakeBlend,
                target,
                Mathf.Max(0.01f, runningShakeLerpDuration))
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetTarget(this);

        if (!useRunningProfile)
        {
            _runningShakeTween.OnComplete(() =>
            {
                if (!_rumbleActive && perlin != null)
                    perlin.NoiseProfile = normalNoiseSettings;
            });
        }
    }

    private void SetShakeBlend(float blend)
    {
        _shakeBlend = blend;

        if (_rumbleActive)
            return;

        CinemachineBasicMultiChannelPerlin perlin = GetPerlin();
        if (perlin == null)
            return;

        if (!_headBobEnabled)
        {
            perlin.AmplitudeGain = 0f;
            perlin.FrequencyGain = 0f;
            return;
        }

        perlin.AmplitudeGain = Mathf.Lerp(amplitudeGainNormal, amplitudeGainRunning, blend);
        perlin.FrequencyGain = Mathf.Lerp(frequencyGainNormal, frequencyGainRunning, blend);
    }

    private CinemachineBasicMultiChannelPerlin GetPerlin()
    {
        if (_perlin == null && camera != null)
            _perlin = camera.GetComponent<CinemachineBasicMultiChannelPerlin>();

        return _perlin;
    }

    private CinemachineImpulseListener GetImpulseListener()
    {
        if (_impulseListener == null && camera != null)
            _impulseListener = camera.GetComponent<CinemachineImpulseListener>();

        return _impulseListener;
    }

    /// <summary>
    /// Enables or disables camera reaction to Cinemachine impulse signals (weapon fire, impacts,
    /// interactable events, etc.), e.g. from the Gameplay settings menu. Does not affect weapon
    /// aim recoil, which is a core gunplay mechanic rather than decorative shake.
    /// </summary>
    public void SetCameraShakeEnabled(bool isEnabled)
    {
        _cameraShakeEnabled = isEnabled;

        CinemachineImpulseListener impulseListener = GetImpulseListener();
        if (impulseListener != null)
            impulseListener.Gain = isEnabled ? 1f : 0f;
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
        _runningShakeTween?.Kill();

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
        ApplyRunningShake(_runningEffectsActive, animate: false);
    }

    /// <summary>
    /// Eases the render camera's near clip plane between its default and a tightened value used
    /// for close-up hand-held items (e.g. smoking a cigarette, drinking radiation pills), restoring
    /// the cached default when finished. Lerps over <see cref="nearClipPlaneLerpDuration"/> seconds
    /// instead of snapping, so the transition isn't jarring.
    /// </summary>
    public void SetNearClipPlaneTightened(bool tightened)
    {
        if (renderCamera == null)
            return;

        if (!_nearClipPlaneCached)
        {
            _defaultNearClipPlane = renderCamera.nearClipPlane;
            _nearClipPlaneCached = true;
        }

        float target = tightened ? smokingNearClipPlane : _defaultNearClipPlane;

        _nearClipPlaneTween?.Kill();
        _nearClipPlaneTween = DOTween.To(
                () => renderCamera.nearClipPlane,
                value => renderCamera.nearClipPlane = value,
                target,
                Mathf.Max(0.01f, nearClipPlaneLerpDuration))
            .OnKill(() => _nearClipPlaneTween = null);
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
        _cameraFeedbackExtension.FieldOfViewOffset =
            _swayFieldOfViewOffset + _cameraKickFieldOfViewOffset + _runningFieldOfViewOffsetCurrent;
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
