using System.Collections;
using UnityEngine;

/// <summary>
/// Scene-level controller that manages anomaly-triggered flickering bursts for a set of booth lights.
/// Referenced by <see cref="FlickeringLightsAnomaly"/> to start and stop the sequence.
/// Each light flickers independently with a randomised per-step hold time and a per-light
/// rate multiplier to keep them out of sync.
/// </summary>
public class BoothFlickeringLightsController : MonoBehaviour
{
    [Header("Booth Lights")]
    [Tooltip("All Light components inside the booth that should flicker during the anomaly.")]
    [SerializeField] private Light[] boothLights;

    [Header("Burst Interval")]
    [Tooltip("Minimum seconds to wait between flicker bursts.")]
    [SerializeField] private float minInterval = 6f;

    [Tooltip("Maximum seconds to wait between flicker bursts.")]
    [SerializeField] private float maxInterval = 15f;

    [Header("Burst Settings")]
    [Tooltip("Minimum total duration of a single flicker burst.")]
    [SerializeField] private float minBurstDuration = 1.5f;

    [Tooltip("Maximum total duration of a single flicker burst.")]
    [SerializeField] private float maxBurstDuration = 4.5f;

    [Header("Flicker Timing")]
    [Tooltip("Shortest time a light holds any state before switching.")]
    [SerializeField] private float minFlickerStep = 0.03f;

    [Tooltip("Longest time a light holds any state before switching.")]
    [SerializeField] private float maxFlickerStep = 0.25f;

    [Tooltip("Per-light rate multiplier sampled at burst start. Gives each light a structurally different cadence.")]
    [SerializeField] private float minRateMultiplier = 1.0f;

    [Tooltip("Per-light rate multiplier sampled at burst start. Gives each light a structurally different cadence.")]
    [SerializeField] private float maxRateMultiplier = 3.0f;

    [Header("Intensity")]
    [Tooltip("Intensity multiplier for the dim state (0 = fully off).")]
    [SerializeField] [Range(0f, 1f)] private float dimmedIntensityRatio = 0.05f;

    [Tooltip("Probability (0–1) that a dark step is fully off rather than just dimmed.")]
    [SerializeField] [Range(0f, 1f)] private float fullyOffChance = 0.35f;

    [Tooltip("Probability (0–1) that any given step lands on full brightness vs dark.")]
    [SerializeField] [Range(0f, 1f)] private float brightChance = 0.65f;

    [Header("Audio")]
    [Tooltip("Sound played once at the start of each flicker burst.")]
    [SerializeField] private AudioClip flickerSound;

    [SerializeField] private float flickerSoundVolume = 1f;

    [Header("Ambient Flicker")]
    [Tooltip("Minimum seconds between ambient flicker events (always active, unrelated to anomaly).")]
    [SerializeField] private float minAmbientInterval = 15f;

    [Tooltip("Maximum seconds between ambient flicker events.")]
    [SerializeField] private float maxAmbientInterval = 45f;

    [Tooltip("Intensity ratio the light dips to during an ambient flicker, relative to its normal intensity.")]
    [SerializeField] [Range(0f, 1f)] private float ambientDipIntensityRatio = 0.15f;

    [Tooltip("Minimum duration of the ambient dip in seconds.")]
    [SerializeField] private float minAmbientDipDuration = 0.04f;

    [Tooltip("Maximum duration of the ambient dip in seconds.")]
    [SerializeField] private float maxAmbientDipDuration = 0.12f;

    [Header("Debug")]
    [Tooltip("Press this key in Play Mode to immediately trigger a single flicker burst.")]
    [SerializeField] private KeyCode testKey = KeyCode.F;

    private float[] _originalIntensities;
    private Coroutine _flickerLoopCoroutine;
    private Coroutine _testBurstCoroutine;
    private Coroutine _ambientFlickerCoroutine;

    private void Start()
    {
        CacheOriginalIntensities();
        _ambientFlickerCoroutine = StartCoroutine(AmbientFlickerLoop());
    }

    private void OnDestroy()
    {
        if (_ambientFlickerCoroutine != null)
            StopCoroutine(_ambientFlickerCoroutine);
    }

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
            TriggerTestBurst();
    }

    /// <summary>
    /// Immediately fires a single flicker burst, independent of the anomaly loop.
    /// Intended for Play Mode testing only.
    /// </summary>
    [ContextMenu("Trigger Test Burst")]
    public void TriggerTestBurst()
    {
        if (_testBurstCoroutine != null) return;

        CacheOriginalIntensities();
        _testBurstCoroutine = StartCoroutine(RunTestBurst());
    }

    private IEnumerator RunTestBurst()
    {
        yield return StartCoroutine(FlickerBurst());
        RestoreOriginalIntensities();
        _testBurstCoroutine = null;
    }

    /// <summary>
    /// Starts the periodic flickering loop. Caches current light intensities for later restoration.
    /// Safe to call even if already running.
    /// </summary>
    public void StartFlickering()
    {
        if (_flickerLoopCoroutine != null) return;

        CacheOriginalIntensities();
        _flickerLoopCoroutine = StartCoroutine(FlickerLoop());
    }

    /// <summary>
    /// Stops the flickering loop and restores all booth lights to their original intensities.
    /// </summary>
    public void StopFlickering()
    {
        if (_flickerLoopCoroutine != null)
        {
            StopCoroutine(_flickerLoopCoroutine);
            _flickerLoopCoroutine = null;
        }

        RestoreOriginalIntensities();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Ambient Flicker
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs continuously throughout the scene, triggering an occasional, subtle
    /// single-light dip to add atmospheric life. Suppressed while the anomaly
    /// flicker loop is active to avoid visual interference.
    /// </summary>
    private IEnumerator AmbientFlickerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minAmbientInterval, maxAmbientInterval));

            // Defer to the anomaly if it is currently running.
            if (_flickerLoopCoroutine != null) continue;

            yield return StartCoroutine(AmbientFlickerDip());
        }
    }

    /// <summary>
    /// Briefly dips a single randomly chosen booth light to <see cref="ambientDipIntensityRatio"/>
    /// of its normal intensity, then restores it.
    /// </summary>
    private IEnumerator AmbientFlickerDip()
    {
        if (boothLights == null || boothLights.Length == 0) yield break;

        int index = Random.Range(0, boothLights.Length);
        if (boothLights[index] == null) yield break;

        float original = _originalIntensities[index];
        boothLights[index].intensity = original * ambientDipIntensityRatio;
        yield return new WaitForSeconds(Random.Range(minAmbientDipDuration, maxAmbientDipDuration));
        boothLights[index].intensity = original;
    }

    // ──────────────────────────────────────────────────────────────────────────

    private void CacheOriginalIntensities()
    {
        _originalIntensities = new float[boothLights.Length];
        for (int i = 0; i < boothLights.Length; i++)
        {
            if (boothLights[i] != null)
                _originalIntensities[i] = boothLights[i].intensity;
        }
    }

    private void RestoreOriginalIntensities()
    {
        if (_originalIntensities == null) return;

        for (int i = 0; i < boothLights.Length; i++)
        {
            if (boothLights[i] != null)
                boothLights[i].intensity = _originalIntensities[i];
        }
    }

    /// <summary>
    /// Waits a random interval then runs a flicker burst. Repeats indefinitely until stopped.
    /// </summary>
    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
            yield return StartCoroutine(FlickerBurst());
            RestoreOriginalIntensities();
        }
    }

    /// <summary>
    /// Launches a parallel per-light coroutine for each booth light. Each light receives a
    /// randomly sampled rate multiplier so their cadences are structurally different.
    /// Waits until every per-light coroutine has completed before returning.
    /// </summary>
    private IEnumerator FlickerBurst()
    {
        SFXController.Instance.PlayAtPosition(flickerSound, transform.position, flickerSoundVolume);

        float burstEndTime = Time.time + Random.Range(minBurstDuration, maxBurstDuration);
        int completedCount = 0;
        int activeCount = 0;

        for (int i = 0; i < boothLights.Length; i++)
        {
            if (boothLights[i] == null) continue;

            float rateMultiplier = Random.Range(minRateMultiplier, maxRateMultiplier);
            activeCount++;
            StartCoroutine(FlickerSingleLight(i, burstEndTime, rateMultiplier, () => completedCount++));
        }

        yield return new WaitUntil(() => completedCount >= activeCount);
    }

    /// <summary>
    /// Independently flickers a single light until <paramref name="burstEndTime"/> is reached.
    /// Each step picks a random intensity and waits a random hold time scaled by
    /// <paramref name="rateMultiplier"/>, keeping this light's rhythm distinct from others.
    /// A random startup offset further breaks initial phase alignment.
    /// Invokes <paramref name="onComplete"/> when finished.
    /// </summary>
    private IEnumerator FlickerSingleLight(int index, float burstEndTime, float rateMultiplier, System.Action onComplete)
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.5f));

        while (Time.time < burstEndTime)
        {
            boothLights[index].intensity = SampleIntensity(index);
            yield return new WaitForSeconds(Random.Range(minFlickerStep, maxFlickerStep) / rateMultiplier);
        }

        onComplete?.Invoke();
    }

    /// <summary>
    /// Returns a random intensity for the light at <paramref name="index"/>.
    /// </summary>
    private float SampleIntensity(int index)
    {
        if (Random.value < brightChance)
            return _originalIntensities[index];

        return _originalIntensities[index] * (Random.value < fullyOffChance ? 0f : dimmedIntensityRatio);
    }
}
