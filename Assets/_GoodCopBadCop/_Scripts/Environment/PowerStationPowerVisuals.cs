using System.Collections;
using UnityEngine;

/// <summary>
/// Makes the power station look "on" while <see cref="ElectricityController"/> reports power
/// and "off" while it doesn't. Purely local/visual: every peer reads the networked power state
/// itself, so no RPCs are needed.
///
///   - Light fixtures: swaps a material slot between on/off materials and toggles a realtime
///     Light. On an off → on transition they turn on one by one (<see cref="_lightOnInterval"/>);
///     on load / late join they snap to the current state. Power off turns them all off at once.
///   - Turbines: looping machine hum + a slight positional shake, both ramping up/down over
///     <see cref="_turbineRampDuration"/>.
///   - Proximity: realtime lights, hum and shake only run while the local camera is within
///     <see cref="_activeRadius"/> of <see cref="_proximityCenter"/>. Materials always reflect
///     the power state (they cost nothing).
///
/// Turbines must not be Batching Static, otherwise the shake has no visible effect.
/// </summary>
public class PowerStationPowerVisuals : MonoBehaviour
{
    [System.Serializable]
    public class LightFixture
    {
        public Renderer renderer;
        [Tooltip("Material slot swapped between the on/off materials.")]
        public int materialIndex = 0;
        [Tooltip("Optional realtime light enabled while this fixture is on (and the player is near).")]
        public Light light;
    }

    [System.Serializable]
    public class TurbineEntry
    {
        public Transform transform;
        public AudioSource humSource;
    }

    [Header("Light Fixtures (turn on in list order)")]
    [SerializeField] private LightFixture[] _fixtures;
    [SerializeField] private Material _lightOnMaterial;
    [SerializeField] private Material _lightOffMaterial;
    [Tooltip("Seconds before the first fixture turns on after power is restored.")]
    [SerializeField] private float _lightOnStartDelay = 0f;
    [Tooltip("Seconds between each fixture turning on.")]
    [SerializeField] private float _lightOnInterval = 0.3f;

    [Header("Turbines")]
    [SerializeField] private TurbineEntry[] _turbines;
    [Tooltip("Looping machine hum assigned to each turbine's hum AudioSource. Leave empty for no hum.")]
    [SerializeField] private AudioClip _humClip;
    [SerializeField] private float _humVolume = 0.6f;
    [Tooltip("Seconds for the hum/shake to ramp fully up (power on) or down (power off).")]
    [SerializeField] private float _turbineRampDuration = 1.5f;
    [Tooltip("Max shake offset in meters.")]
    [SerializeField] private float _shakeAmplitude = 0.006f;
    [SerializeField] private float _shakeFrequency = 22f;

    [Header("Proximity")]
    [Tooltip("Distance is measured from here to the local camera. Defaults to this transform.")]
    [SerializeField] private Transform _proximityCenter;
    [SerializeField] private float _activeRadius = 60f;
    [Tooltip("Extra distance beyond the radius before turning effects back off (prevents flicker at the edge).")]
    [SerializeField] private float _radiusHysteresis = 5f;
    [SerializeField] private float _proximityCheckInterval = 0.25f;

    private bool _initialized;
    private bool _powered;
    private bool _inRange;
    private bool[] _fixtureOn;
    private Vector3[] _turbineRestPositions;
    private float _runLevel;
    private float _proximityTimer;
    private bool _turbinesAtRest = true;
    private Coroutine _sequence;

    private void Awake()
    {
        _fixtureOn = new bool[_fixtures != null ? _fixtures.Length : 0];

        int turbineCount = _turbines != null ? _turbines.Length : 0;
        _turbineRestPositions = new Vector3[turbineCount];
        for (int i = 0; i < turbineCount; i++)
        {
            var t = _turbines[i];
            if (t.transform != null) _turbineRestPositions[i] = t.transform.localPosition;
            if (t.humSource != null)
            {
                t.humSource.playOnAwake = false;
                t.humSource.loop = true;
                if (_humClip != null) t.humSource.clip = _humClip;
                t.humSource.Stop();
            }
        }
    }

    private void OnDisable()
    {
        if (_sequence != null)
        {
            StopCoroutine(_sequence);
            _sequence = null;
        }
        ResetTurbines();
        _initialized = false;
    }

    private void Update()
    {
        bool powered = ReadPowerState();

        if (!_initialized)
        {
            _initialized = true;
            _powered = powered;
            _runLevel = powered ? 1f : 0f;
            _inRange = ComputeInRange(false);
            SetAllFixtures(powered);
        }
        else if (powered != _powered)
        {
            _powered = powered;
            if (_sequence != null)
            {
                StopCoroutine(_sequence);
                _sequence = null;
            }

            if (powered) _sequence = StartCoroutine(TurnOnSequence());
            else SetAllFixtures(false);
        }

        _proximityTimer -= Time.deltaTime;
        if (_proximityTimer <= 0f)
        {
            _proximityTimer = _proximityCheckInterval;
            bool inRange = ComputeInRange(_inRange);
            if (inRange != _inRange)
            {
                _inRange = inRange;
                RefreshLights();
            }
        }

        float target = _powered ? 1f : 0f;
        float rampSpeed = _turbineRampDuration > 0f ? 1f / _turbineRampDuration : float.MaxValue;
        _runLevel = Mathf.MoveTowards(_runLevel, target, rampSpeed * Time.deltaTime);

        UpdateTurbines();
    }

    private static bool ReadPowerState()
    {
        var ec = ElectricityController.Instance;
        return ec != null && ec.IsSpawned && ec.IsPowerOn;
    }

    private bool ComputeInRange(bool currentlyInRange)
    {
        var cam = Camera.main;
        if (cam == null) return false;

        Vector3 center = _proximityCenter != null ? _proximityCenter.position : transform.position;
        float radius = currentlyInRange ? _activeRadius + _radiusHysteresis : _activeRadius;
        return (cam.transform.position - center).sqrMagnitude <= radius * radius;
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private IEnumerator TurnOnSequence()
    {
        if (_lightOnStartDelay > 0f)
            yield return new WaitForSeconds(_lightOnStartDelay);

        for (int i = 0; i < _fixtureOn.Length; i++)
        {
            if (!_powered) yield break;
            SetFixture(i, true);
            if (i < _fixtureOn.Length - 1)
                yield return new WaitForSeconds(_lightOnInterval);
        }
        _sequence = null;
    }

    private void SetAllFixtures(bool on)
    {
        for (int i = 0; i < _fixtureOn.Length; i++)
            SetFixture(i, on);
    }

    private void SetFixture(int index, bool on)
    {
        _fixtureOn[index] = on;
        var fixture = _fixtures[index];

        if (fixture.renderer != null)
        {
            Material mat = on ? _lightOnMaterial : _lightOffMaterial;
            Material[] mats = fixture.renderer.sharedMaterials;
            if (mat != null && fixture.materialIndex >= 0 && fixture.materialIndex < mats.Length
                && mats[fixture.materialIndex] != mat)
            {
                mats[fixture.materialIndex] = mat;
                fixture.renderer.sharedMaterials = mats;
            }
        }

        if (fixture.light != null)
            fixture.light.enabled = on && _inRange;
    }

    private void RefreshLights()
    {
        for (int i = 0; i < _fixtureOn.Length; i++)
        {
            var light = _fixtures[i].light;
            if (light != null) light.enabled = _fixtureOn[i] && _inRange;
        }
    }

    // ── Turbines ──────────────────────────────────────────────────────────────

    private void UpdateTurbines()
    {
        if (_turbines == null) return;

        if (!_inRange || _runLevel <= 0f)
        {
            if (!_turbinesAtRest) ResetTurbines();
            return;
        }

        _turbinesAtRest = false;
        float time = Time.time * _shakeFrequency;
        float amplitude = _shakeAmplitude * _runLevel;

        for (int i = 0; i < _turbines.Length; i++)
        {
            var t = _turbines[i];

            if (t.transform != null)
            {
                float seed = i * 17.31f;
                Vector3 offset = new Vector3(
                    Mathf.PerlinNoise(time, seed) - 0.5f,
                    Mathf.PerlinNoise(seed, time) - 0.5f,
                    Mathf.PerlinNoise(time + 3.7f, seed + 9.1f) - 0.5f) * (2f * amplitude);
                t.transform.localPosition = _turbineRestPositions[i] + offset;
            }

            if (t.humSource != null && t.humSource.clip != null)
            {
                t.humSource.volume = _humVolume * _runLevel;
                if (!t.humSource.isPlaying) t.humSource.Play();
            }
        }
    }

    private void ResetTurbines()
    {
        _turbinesAtRest = true;
        if (_turbines == null || _turbineRestPositions == null) return;

        for (int i = 0; i < _turbines.Length; i++)
        {
            var t = _turbines[i];
            if (t.transform != null && i < _turbineRestPositions.Length)
                t.transform.localPosition = _turbineRestPositions[i];
            if (t.humSource != null && t.humSource.isPlaying)
                t.humSource.Stop();
        }
    }
}
