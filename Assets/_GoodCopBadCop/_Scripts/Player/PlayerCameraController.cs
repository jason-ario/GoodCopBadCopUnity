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

    [Header("Hit Impulse")]
    [Tooltip("Impulse source used when the player takes damage.")]
    [SerializeField] private CinemachineImpulseSource _hitImpulseSource;

    private CinemachineHeadSwayExtension _headSwayExtension;
    private Sequence _swaySequence;
    private Vector3 _appliedSwayOffsets;
    private float _appliedFieldOfViewOffset;

    private void Awake()
    {
        EnsureHitImpulseSource();
    }

    private void OnDisable()
    {
        StopSway();
    }

    /// <summary>Enables or disables the Cinemachine virtual camera.</summary>
    public void SetCameraActive(bool active)
    {
        if (!active)
        {
            StopSway();
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

    /// <summary>Fires a one-shot Cinemachine impulse to simulate a camera hit shake.</summary>
    public void TriggerHitImpulse()
    {
        PlayImpulse(CameraImpulseSettings.DefaultHit());
    }

    public void PlayImpulse(CameraImpulseSettings settings)
    {
        if (settings == null || !settings.Enabled)
        {
            return;
        }

        if (!EnsureHitImpulseSource())
        {
            Debug.LogWarning("[PlayerCameraController] Hit impulse source is not available.", this);
            return;
        }

        ApplyImpulseSettings(_hitImpulseSource, settings);

        switch (settings.Mode)
        {
            case ECameraImpulseMode.Force:
                _hitImpulseSource.GenerateImpulseWithForce(settings.Force);
                break;
            case ECameraImpulseMode.Velocity:
                _hitImpulseSource.GenerateImpulseWithVelocity(settings.Velocity);
                break;
            default:
                _hitImpulseSource.GenerateImpulse();
                break;
        }
    }

    public void PlaySway(CameraSwaySettings settings)
    {
        if (settings == null || !settings.Enabled || camera == null || !EnsureHeadSwayExtension(true))
            return;

        StopSway();

        _swaySequence = CreateSwaySequence(settings)
            .OnComplete(ResetSwayOffsets)
            .OnKill(() => _swaySequence = null);
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

    private Sequence BuildHeadSwaySequence(Sequence sequence, float duration, Vector3 amplitude)
    {
        float leanInDuration = duration * 0.3f;
        float swingDuration = duration * 0.4f;
        float settleDuration = Mathf.Max(0.01f, duration - leanInDuration - swingDuration);

        return sequence
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, amplitude, leanInDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, -amplitude * 0.65f, swingDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, Vector3.zero, settleDuration)
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
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, inhale, inhaleDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, lift, liftDuration)
                .SetEase(Ease.OutSine))
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, Vector3.zero, exhaleDuration)
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
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, push, pushDuration)
                .SetEase(Ease.InOutSine))
            .Join(DOTween.To(() => _appliedFieldOfViewOffset, SetFieldOfViewOffset, fieldOfViewOffset, pushDuration)
                .SetEase(Ease.InOutSine))
            .AppendInterval(holdDuration)
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, release, releaseDuration)
                .SetEase(Ease.InOutSine))
            .Append(DOTween.To(() => _appliedSwayOffsets, SetSwayOffsets, Vector3.zero, settleDuration)
                .SetEase(Ease.InOutSine))
            .Insert(resetStartTime, DOTween.To(() => _appliedFieldOfViewOffset, SetFieldOfViewOffset, 0f, releaseDuration + settleDuration)
                .SetEase(Ease.InOutSine));
    }

    private void SetSwayOffsets(Vector3 offsets)
    {
        _appliedSwayOffsets = offsets;

        if (EnsureHeadSwayExtension(false))
            _headSwayExtension.EulerOffset = offsets;
    }

    private void SetFieldOfViewOffset(float offset)
    {
        _appliedFieldOfViewOffset = offset;

        if (EnsureHeadSwayExtension(false))
            _headSwayExtension.FieldOfViewOffset = offset;
    }

    private void ResetSwayOffsets()
    {
        SetSwayOffsets(Vector3.zero);
        SetFieldOfViewOffset(0f);
    }

    private static void ConfigureDefaultHitImpulseSource(CinemachineImpulseSource impulseSource)
    {
        if (impulseSource == null)
            return;

        impulseSource.DefaultVelocity = Vector3.down;
        impulseSource.ImpulseDefinition = CreateImpulseDefinition(CameraImpulseSettings.DefaultHit());
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

    private bool EnsureHitImpulseSource()
    {
        if (_hitImpulseSource != null)
            return true;

        _hitImpulseSource = GetComponent<CinemachineImpulseSource>();
        if (_hitImpulseSource != null)
            return true;

        _hitImpulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
        ConfigureDefaultHitImpulseSource(_hitImpulseSource);
        return _hitImpulseSource != null;
    }

    private bool EnsureHeadSwayExtension(bool createIfMissing)
    {
        if (_headSwayExtension != null)
            return true;

        if (camera == null)
            return false;

        _headSwayExtension = camera.GetComponent<CinemachineHeadSwayExtension>();
        if (_headSwayExtension == null && createIfMissing)
            _headSwayExtension = camera.gameObject.AddComponent<CinemachineHeadSwayExtension>();

        return _headSwayExtension != null;
    }
}
