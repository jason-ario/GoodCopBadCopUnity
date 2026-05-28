using System.Collections;
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

    /// <summary>
    /// Enables or disables the Cinemachine virtual camera.
    /// When enabling, forces an instant cut on the CinemachineBrain so there is
    /// no lerp from the previously active camera.
    /// </summary>
    public void SetCameraActive(bool active)
    {
        if (camera == null) return;

        if (active)
        {
            var brain = FindFirstObjectByType<CinemachineBrain>();
            if (brain != null)
            {
                var prevBlend = brain.DefaultBlend;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                camera.gameObject.SetActive(true);
                StartCoroutine(RestoreBlend(brain, prevBlend));
                return;
            }
        }

        camera.gameObject.SetActive(active);
    }

    private IEnumerator RestoreBlend(CinemachineBrain brain, CinemachineBlendDefinition prevBlend)
    {
        yield return null;
        if (brain != null)
            brain.DefaultBlend = prevBlend;
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
        if (_hitImpulseSource == null)
        {
            Debug.LogWarning("[PlayerCameraController] Hit impulse source is not assigned.", this);
            return;
        }

        _hitImpulseSource.GenerateImpulse();
    }
}
