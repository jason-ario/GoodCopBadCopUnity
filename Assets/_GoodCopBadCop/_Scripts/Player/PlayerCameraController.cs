using GoodCopBadCop.CameraSystem;
using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

public class PlayerCameraController : MonoBehaviour
{
    [SerializeField] private CinemachineCamera camera;
    [SerializeField] NoiseSettings normalNoiseSettings;
    [SerializeField] NoiseSettings rumbleNoiseSettings;
    [SerializeField] private float amplitudeGainNormal;
    [SerializeField] private float frequencyGainNormal;
    [SerializeField] private float amplitudeGainRumble;
    [SerializeField] private float frequencyGainRumble;

    [Header("Camera Impulse")]
    [Tooltip("Impulse source used by effect presets that still need Cinemachine impulse feedback.")]
    [SerializeField, FormerlySerializedAs("_hitImpulseSource")] private CinemachineImpulseSource _impulseSource;

    private CinemachineCameraFeedbackExtension _cameraFeedbackExtension;
    private Sequence _swaySequence;
    private Sequence _damageSequence;
    private Vector3 _swayEulerOffset;
    private Vector3 _damageEulerOffset;
    private float _swayFieldOfViewOffset;
    private float _damageFieldOfViewOffset;

    private void OnDisable()
    {
        StopSway();
        StopDamageFeedback();
    }

    /// <summary>Enables or disables the Cinemachine virtual camera.</summary>
    public void SetCameraActive(bool active)
    {
        if (!active)
        {
            StopSway();
            StopDamageFeedback();
        }

        if (camera != null)
            camera.gameObject.SetActive(active);
    }

    public void TurnOnRumble()
    {
        CinemachineBasicMultiChannelPerlin cinemachineBasicMultiChannelPerlin = camera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        cinemachineBasicMultiChannelPerlin.NoiseProfile = rumbleNoiseSettings;
        cinemachineBasicMultiChannelPerlin.AmplitudeGain = amplitudeGainRumble;
        cinemachineBasicMultiChannelPerlin.FrequencyGain = frequencyGainRumble;

    }

    public void TurnOffRumble()
    {
        CinemachineBasicMultiChannelPerlin cinemachineBasicMultiChannelPerlin = camera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        cinemachineBasicMultiChannelPerlin.NoiseProfile = normalNoiseSettings;
        cinemachineBasicMultiChannelPerlin.AmplitudeGain = amplitudeGainNormal;
        cinemachineBasicMultiChannelPerlin.FrequencyGain = frequencyGainNormal;
    }

    public void PlayImpulse(CameraImpulseSettings settings)
    {
        if (settings == null || !settings.Enabled)
        {
            return;
        }

        if (!EnsureImpulseSource())
        {
            Debug.LogWarning("[PlayerCameraController] Camera impulse source is not available.", this);
            return;
        }

        ApplyImpulseSettings(_impulseSource, settings);

        switch (settings.Mode)
        {
            case ECameraImpulseMode.Force:
                _impulseSource.GenerateImpulseWithForce(settings.Force);
                break;
            case ECameraImpulseMode.Velocity:
                _impulseSource.GenerateImpulseWithVelocity(settings.Velocity);
                break;
            default:
                _impulseSource.GenerateImpulse();
                break;
        }
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

    public void PlayDamageFeedback(CameraDamageFeedbackSettings settings)
    {
        if (settings == null || !settings.Enabled || camera == null || !EnsureCameraFeedbackExtension(true))
            return;

        StopDamageFeedback();

        _damageSequence = CreateDamageFeedbackSequence(settings)
            .OnComplete(ResetDamageFeedbackOffsets)
            .OnKill(() => _damageSequence = null);
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

    private void StopDamageFeedback()
    {
        if (_damageSequence != null)
        {
            _damageSequence.Kill(false);
            _damageSequence = null;
        }

        ResetDamageFeedbackOffsets();
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

    private Sequence CreateDamageFeedbackSequence(CameraDamageFeedbackSettings settings)
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
            .Append(DOTween.To(() => _damageEulerOffset, SetDamageEulerOffset, kick, impactDuration)
                .SetEase(Ease.OutCubic))
            .Join(DOTween.To(() => _damageFieldOfViewOffset, SetDamageFieldOfViewOffset, fieldOfViewKick, impactDuration)
                .SetEase(Ease.OutCubic))
            .Append(DOTween.To(() => _damageEulerOffset, SetDamageEulerOffset, counterKick, counterDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _damageEulerOffset, SetDamageEulerOffset, Vector3.zero, settleDuration)
                .SetEase(Ease.OutSine))
            .Insert(impactDuration, DOTween.To(() => _damageFieldOfViewOffset, SetDamageFieldOfViewOffset, 0f, counterDuration + settleDuration)
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

    private void SetDamageEulerOffset(Vector3 offset)
    {
        _damageEulerOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void SetSwayFieldOfViewOffset(float offset)
    {
        _swayFieldOfViewOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void SetDamageFieldOfViewOffset(float offset)
    {
        _damageFieldOfViewOffset = offset;
        ApplyCameraFeedbackOffsets();
    }

    private void ResetSwayOffsets()
    {
        SetSwayEulerOffset(Vector3.zero);
        SetSwayFieldOfViewOffset(0f);
    }

    private void ResetDamageFeedbackOffsets()
    {
        SetDamageEulerOffset(Vector3.zero);
        SetDamageFieldOfViewOffset(0f);
    }

    private void ApplyCameraFeedbackOffsets()
    {
        if (!EnsureCameraFeedbackExtension(false))
            return;

        _cameraFeedbackExtension.EulerOffset = _swayEulerOffset + _damageEulerOffset;
        _cameraFeedbackExtension.FieldOfViewOffset = _swayFieldOfViewOffset + _damageFieldOfViewOffset;
    }

    private static void ConfigureDefaultImpulseSource(CinemachineImpulseSource impulseSource)
    {
        if (impulseSource == null)
            return;

        impulseSource.DefaultVelocity = Vector3.down;
        impulseSource.ImpulseDefinition = CreateImpulseDefinition(CameraImpulseSettings.DefaultImpulse());
    }

    private static void ApplyImpulseSettings(CinemachineImpulseSource impulseSource, CameraImpulseSettings settings)
    {
        if (impulseSource == null || settings == null)
            return;

        Vector3 defaultVelocity = settings.Velocity.sqrMagnitude > Mathf.Epsilon
            ? settings.Velocity
            : Vector3.down;

        impulseSource.DefaultVelocity = defaultVelocity;
        impulseSource.ImpulseDefinition = CreateImpulseDefinition(settings);
    }

    private static CinemachineImpulseDefinition CreateImpulseDefinition(CameraImpulseSettings settings)
    {
        var definition = new CinemachineImpulseDefinition
        {
            ImpulseChannel = 1,
            ImpulseShape = settings.CinemachineShape,
            CustomImpulseShape = new AnimationCurve(),
            ImpulseDuration = Mathf.Max(0.01f, settings.ImpulseDuration),
            ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
            DissipationRate = 0.25f,
            AmplitudeGain = settings.AmplitudeGain,
            FrequencyGain = settings.FrequencyGain,
            RepeatMode = CinemachineImpulseDefinition.RepeatModes.Stretch,
            Randomize = false,
            TimeEnvelope = new CinemachineImpulseManager.EnvelopeDefinition
            {
                AttackShape = new AnimationCurve(),
                DecayShape = new AnimationCurve(),
                AttackTime = Mathf.Max(0f, settings.AttackTime),
                SustainTime = Mathf.Max(0f, settings.SustainTime),
                DecayTime = Mathf.Max(0f, settings.DecayTime),
                ScaleWithImpact = settings.ScaleEnvelopeWithImpact,
                HoldForever = false
            },
            ImpactRadius = 100f,
            DirectionMode = CinemachineImpulseManager.ImpulseEvent.DirectionModes.Fixed,
            DissipationMode = CinemachineImpulseManager.ImpulseEvent.DissipationModes.ExponentialDecay,
            DissipationDistance = 100f,
            PropagationSpeed = 343f
        };

        definition.OnValidate();
        return definition;
    }

    private bool EnsureImpulseSource()
    {
        if (_impulseSource != null)
            return true;

        _impulseSource = GetComponent<CinemachineImpulseSource>();
        if (_impulseSource != null)
            return true;

        _impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
        ConfigureDefaultImpulseSource(_impulseSource);
        return _impulseSource != null;
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
