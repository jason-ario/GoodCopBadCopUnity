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
        if (_hitImpulseSource == null)
        {
            Debug.LogWarning("[PlayerCameraController] Hit impulse source is not assigned.", this);
            return;
        }

        if (settings == null || !settings.Enabled)
        {
            return;
        }

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
}
