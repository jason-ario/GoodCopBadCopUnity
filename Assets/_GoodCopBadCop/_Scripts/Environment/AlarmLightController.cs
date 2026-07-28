using System.Collections;
using UnityEngine;

/// <summary>
/// Client-visual controller that pulses a set of Light components red while a mutant breach
/// alarm is active, then restores their original color and intensity. Driven entirely by
/// <see cref="MutantBreachManager"/> via ClientRpc — this component has no networking of its
/// own and should never be triggered directly except for local testing.
/// </summary>
public class AlarmLightController : MonoBehaviour
{
    [Header("Alarm Lights")]
    [Tooltip("All Light components that should flash red while the alarm is active.")]
    [SerializeField] private Light[] alarmLights;

    [Header("Pulse")]
    [Tooltip("Color the lights switch to while the alarm is active.")]
    [SerializeField] private Color alarmColor = Color.red;

    [Tooltip("Pulses per second.")]
    [SerializeField] private float pulseSpeed = 4f;

    [Tooltip("Minimum intensity of the pulse.")]
    [SerializeField] private float minIntensity = 0.2f;

    [Tooltip("Maximum intensity of the pulse.")]
    [SerializeField] private float maxIntensity = 3f;

    private Color[] _originalColors;
    private float[] _originalIntensities;
    private Coroutine _pulseCoroutine;

    private void Awake()
    {
        // Lights are off unless a breach is actively running.
        SetLightsEnabled(false);
    }

    /// <summary>Starts the red pulsing loop. Safe to call if already running.</summary>
    public void StartAlarm()
    {
        if (_pulseCoroutine != null || alarmLights == null || alarmLights.Length == 0)
            return;

        CacheOriginalState();
        SetLightsEnabled(true);
        _pulseCoroutine = StartCoroutine(PulseLoop());
    }

    /// <summary>Stops the pulsing loop, restores every light's original color and intensity, then deactivates them.</summary>
    public void StopAlarm()
    {
        if (_pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
        }

        RestoreOriginalState();
        SetLightsEnabled(false);
    }

    private void SetLightsEnabled(bool isEnabled)
    {
        if (alarmLights == null) return;

        for (int i = 0; i < alarmLights.Length; i++)
        {
            if (alarmLights[i] != null)
                alarmLights[i].gameObject.SetActive(isEnabled);
        }
    }

    private void CacheOriginalState()
    {
        _originalColors = new Color[alarmLights.Length];
        _originalIntensities = new float[alarmLights.Length];

        for (int i = 0; i < alarmLights.Length; i++)
        {
            if (alarmLights[i] == null) continue;
            _originalColors[i] = alarmLights[i].color;
            _originalIntensities[i] = alarmLights[i].intensity;
        }
    }

    private void RestoreOriginalState()
    {
        if (_originalColors == null) return;

        for (int i = 0; i < alarmLights.Length; i++)
        {
            if (alarmLights[i] == null) continue;
            alarmLights[i].color = _originalColors[i];
            alarmLights[i].intensity = _originalIntensities[i];
        }
    }

    private IEnumerator PulseLoop()
    {
        for (int i = 0; i < alarmLights.Length; i++)
        {
            if (alarmLights[i] != null)
                alarmLights[i].color = alarmColor;
        }

        while (true)
        {
            float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

            for (int i = 0; i < alarmLights.Length; i++)
            {
                if (alarmLights[i] != null)
                    alarmLights[i].intensity = intensity;
            }

            yield return null;
        }
    }
}
