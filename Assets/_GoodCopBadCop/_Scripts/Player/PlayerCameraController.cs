using GoodCopBadCop.CameraSystem;
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

    private CameraSwaySettings _activeSway;
    private Vector3 _appliedSwayEuler;
    private float _swayElapsed;

    private void Awake()
    {
        EnsureHitImpulseSource();
    }

    private void LateUpdate()
    {
        UpdateSway();
    }

    private void OnDisable()
    {
        ClearAppliedSway();
        _activeSway = null;
    }

    /// <summary>Enables or disables the Cinemachine virtual camera.</summary>
    public void SetCameraActive(bool active)
    {
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
        if (settings == null || !settings.Enabled || camera == null)
            return;

        ClearAppliedSway();
        _activeSway = settings;
        _swayElapsed = 0f;
    }

    private void UpdateSway()
    {
        if (_activeSway == null || camera == null)
            return;

        ClearAppliedSway();

        float duration = Mathf.Max(0.01f, _activeSway.Duration);
        _swayElapsed += Time.unscaledDeltaTime;
        float normalizedTime = Mathf.Clamp01(_swayElapsed / duration);
        float wave = Mathf.Sin(normalizedTime * _activeSway.Cycles * Mathf.PI * 2f);
        float envelope = EvaluateSwayEnvelope(_activeSway, normalizedTime);

        _appliedSwayEuler = _activeSway.EulerAmplitude * (wave * envelope);
        camera.transform.localRotation *= Quaternion.Euler(_appliedSwayEuler);

        if (_swayElapsed >= duration)
        {
            ClearAppliedSway();
            _activeSway = null;
        }
    }

    private void ClearAppliedSway()
    {
        if (camera == null || _appliedSwayEuler == Vector3.zero)
            return;

        camera.transform.localRotation *= Quaternion.Inverse(Quaternion.Euler(_appliedSwayEuler));
        _appliedSwayEuler = Vector3.zero;
    }

    private static float EvaluateSwayEnvelope(CameraSwaySettings settings, float normalizedTime)
    {
        AnimationCurve envelope = settings.Envelope;
        if (envelope == null || envelope.length == 0)
            return Mathf.Sin(normalizedTime * Mathf.PI);

        return Mathf.Clamp01(envelope.Evaluate(normalizedTime));
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
}
