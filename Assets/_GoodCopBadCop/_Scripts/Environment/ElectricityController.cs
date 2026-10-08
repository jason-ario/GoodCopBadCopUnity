using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ElectricityController : NetworkBehaviour
{
    [SerializeField] private ElectricObject[] electricObjects;
    [SerializeField] private AudioClip powerOffSound;
    [SerializeField] private AudioClip powerOnSound;
    [Tooltip("Played instead of powerOnSound when power is restored at the power station (fuse-box outage). Falls back to powerOnSound if empty.")]
    [SerializeField] private AudioClip fuseBoxPowerOnSound;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private Vector2 powerOutageRandomTime = new Vector2(60, 120);

    [Header("Grid Outage (Fuse-Box Restore)")]
    [Tooltip("Played instead of powerOffSound when the outage needs the power station's fuse box " +
             "(PowerOffFuseRequired). Should sound bigger and further away than a local trip, " +
             "e.g. 'Power-Outage Sound - NrMaxi Sky.wav'. Falls back to powerOffSound if empty.")]
    [SerializeField] private AudioClip gridPowerOffSound;

    [Tooltip("Start point of the grid-outage blackout wave. Electric objects switch off in order of " +
             "distance from here, so the dark rolls in from the power station toward the booth. " +
             "Falls back to the PowerStationPowerVisuals in the scene, then to this transform.")]
    [SerializeField] private Transform gridOutageWaveOrigin;

    [Tooltip("Seconds between the nearest and the furthest electric object switching off during a " +
             "grid outage. 0 = everything off at once, like a local trip.")]
    [SerializeField, Min(0f)] private float gridOutageWaveDuration = 2.5f;

    /// <summary>When false, the automatic power outage countdown never starts.</summary>
    [SerializeField] private bool enablePowerOutage = false;

    private NetworkVariable<bool> _isPowerOn = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// When true the standard circuit breaker cannot restore power — only the
    /// fuse-box puzzle + power switch can. Reset to false by <see cref="PowerOn"/>.
    /// </summary>
    private NetworkVariable<bool> _requiresFuseBoxRestore = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Scene singleton — set in <see cref="Awake"/>, cleared in <see cref="OnDestroy"/>.</summary>
    public static ElectricityController Instance { get; private set; }

    /// <summary>Returns the current power state, readable by all clients.</summary>
    public bool IsPowerOn => _isPowerOn.Value;

    /// <summary>True when the automatic random outage countdown is enabled for this session.</summary>
    public bool EnablePowerOutage => enablePowerOutage;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// True when the current outage can only be cleared via the fuse-box puzzle.
    /// The normal <see cref="CircuitBox"/> interaction will be blocked while this is set.
    /// </summary>
    public bool RequiresFuseBoxRestore => _requiresFuseBoxRestore.Value;

    // ── Server-side events ────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the server when a fuse-required outage begins via
    /// <see cref="PowerOffFuseRequired"/>. Subscribe to spawn fuses, trigger
    /// environment changes, etc.
    /// </summary>
    public event System.Action OnFuseRequiredOutageStarted;

    /// <summary>
    /// Fired on the server when <see cref="PowerOn"/> is called after a fuse-required
    /// outage (i.e. <see cref="RequiresFuseBoxRestore"/> was true). Use this to clean
    /// up any spawned objects that should be removed once power is restored.
    /// </summary>
    public event System.Action OnFuseOutageResolved;

    /// <summary>
    /// Fired on ALL clients (via the <c>_isPowerOn</c> NetworkVariable's OnValueChanged
    /// callback, which runs locally on every client) whenever power transitions from off to
    /// on, regardless of which interactable restored it. Day-specific controllers can
    /// subscribe to complete their local objective/threat state without needing a ClientRpc.
    /// </summary>
    public event System.Action OnPowerRestoredAllClients;

    public override void OnNetworkSpawn()
    {
        _isPowerOn.OnValueChanged += OnPowerStateChanged;
        _requiresFuseBoxRestore.OnValueChanged += OnFuseRequirementChanged;
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += StartCountdown;
        UIController.Instance.OnReportShown += PauseCountdown;
        UIController.Instance.OnReportHidden += ResumeCountdown;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftStart -= StartCountdown;

        if (UIController.Instance != null)
        {
            UIController.Instance.OnReportShown -= PauseCountdown;
            UIController.Instance.OnReportHidden -= ResumeCountdown;
        }
    }

    public override void OnNetworkDespawn()
    {
        _isPowerOn.OnValueChanged -= OnPowerStateChanged;
        _requiresFuseBoxRestore.OnValueChanged -= OnFuseRequirementChanged;
    }

    // ------------------------------------------------------------------
    // Server-only logic
    // ------------------------------------------------------------------

    [SerializeField, ReadOnly] private float _countdownRemaining = 0f;
    private bool _isCountdownPaused = false;
    private Coroutine _powerOffCoroutine;

    private void PauseCountdown() => _isCountdownPaused = true;
    private void ResumeCountdown() => _isCountdownPaused = false;

    private void StartCountdown()
    {
        if (!IsServer) return;
        if (!enablePowerOutage) return;
        Debug.Log("[ElectricityController] StartCountdown called.");
        StartCoroutine(WaitAndShutDown());
    }

    private IEnumerator WaitAndShutDown()
    {
        _countdownRemaining = Random.Range(powerOutageRandomTime.x, powerOutageRandomTime.y);
        Debug.Log($"[ElectricityController] Power outage in {_countdownRemaining:F1}s.");

        while (_countdownRemaining > 0f)
        {
            yield return null;
            if (!_isCountdownPaused)
                _countdownRemaining -= Time.deltaTime;
        }

        _countdownRemaining = 0f;
        PowerOff();
    }

    [ContextMenu("Power Off")]
    public void PowerOff()
    {
        if (!IsServer) return;

        _isPowerOn.Value = false;
        PowerOffClientRpc(_requiresFuseBoxRestore.Value);
    }

    /// <summary>
    /// Cuts power as a regular outage that the booth <see cref="ElectricPanelController"/> (or
    /// <see cref="CircuitBox"/>) can restore, explicitly clearing any stray fuse-box requirement
    /// first. Used by scripted booth outages such as Day 2's Ocho encounter.
    /// </summary>
    public void PowerOffPanelRestorable()
    {
        if (!IsServer) return;

        _requiresFuseBoxRestore.Value = false;
        PowerOff();
    }

    /// <summary>
    /// Cuts power and marks the outage as requiring the fuse-box puzzle to resolve.
    /// The standard <see cref="CircuitBox"/> will silently reject restore attempts.
    /// </summary>
    [ContextMenu("Power Off (Fuse Required)")]
    public void PowerOffFuseRequired()
    {
        if (!IsServer) return;

        _requiresFuseBoxRestore.Value = true;
        OnFuseRequiredOutageStarted?.Invoke();
        PowerOff();
    }

    [ContextMenu("Power On")]
    public void PowerOn()
    {
        if (!IsServer) return;

        bool wasFuseOutage = _requiresFuseBoxRestore.Value;
        _requiresFuseBoxRestore.Value = false;
        _isPowerOn.Value = true;
        PowerOnClientRpc(wasFuseOutage);

        if (wasFuseOutage)
            OnFuseOutageResolved?.Invoke();

        if (enablePowerOutage)
            StartCoroutine(WaitAndShutDown());
    }

    // ------------------------------------------------------------------
    // Client RPCs — run on every client including the host
    // ------------------------------------------------------------------

    [ClientRpc]
    private void PowerOffClientRpc(bool gridOutage)
    {
        if (_powerOffCoroutine != null)
            StopCoroutine(_powerOffCoroutine);

        _powerOffCoroutine = StartCoroutine(PowerOffCoroutine(gridOutage));
    }

    [ClientRpc]
    private void PowerOnClientRpc(bool wasFuseOutage)
    {
        // Cancel any pending power-off coroutine so its delayed OnElectricityTurnOff
        // does not fire after the power has already been restored.
        if (_powerOffCoroutine != null)
        {
            StopCoroutine(_powerOffCoroutine);
            _powerOffCoroutine = null;
        }

        StopPendingPowerOff();
        _electricOffApplied = false;

        foreach (var electricObject in electricObjects)
        {
            if (electricObject != null)
                electricObject.OnElectricityTurnOn?.Invoke();
        }

        AudioClip onClip = wasFuseOutage && fuseBoxPowerOnSound != null ? fuseBoxPowerOnSound : powerOnSound;
        if (onClip != null) sfxSource.PlayOneShot(onClip);

        // Fire "power restored" here unconditionally, rather than relying solely on
        // OnPowerStateChanged below (driven by the _isPowerOn NetworkVariable's OnValueChanged).
        // NetworkVariable.Value setters skip OnValueChanged entirely when the new value equals
        // the current one — so if PowerOn() is ever called while _isPowerOn.Value is already
        // true (a stray leftover state, a duplicate call, or any other edge case), the value
        // never "changes" and OnPowerRestoredAllClients would otherwise silently never fire,
        // leaving listeners (e.g. OchoBoothEncounter's power-outage sequence, Day_03's fuse-box
        // task) stuck thinking power is still out. This ClientRpc always runs whenever PowerOn()
        // executes, so it's the reliable place to guarantee the notification. Safe to fire
        // alongside OnPowerStateChanged's own invoke on the normal (value actually changes)
        // path — every OnPowerRestoredAllClients subscriber in this codebase unsubscribes itself
        // as the first thing it does, so a double notification is a harmless no-op.
        OnPowerRestoredAllClients?.Invoke();
    }

    private IEnumerator PowerOffCoroutine(bool gridOutage)
    {
        AudioClip offClip = gridOutage && gridPowerOffSound != null ? gridPowerOffSound : powerOffSound;
        if (offClip != null) sfxSource.PlayOneShot(offClip);

        yield return new WaitForSeconds(2f);

        // The _isPowerOn OnValueChanged callback usually already switched everything off (or
        // started the grid blackout wave) during this delay. ApplyPowerOffLocal is guarded so it
        // never re-resets the electrical panel under a player who already started repairing it.
        ApplyPowerOffLocal(gridOutage);

        _powerOffCoroutine = null;
    }

    /// <summary>Local, per peer: true once OnElectricityTurnOff has run (or its wave has started) for the current outage.</summary>
    private bool _electricOffApplied;

    /// <summary>Local, per peer: the running grid blackout wave, if any.</summary>
    private Coroutine _powerOffWaveCoroutine;

    /// <summary>Local, per peer: the one-frame deferral started by <see cref="OnPowerStateChanged"/>.</summary>
    private Coroutine _deferredPowerOffCoroutine;

    /// <summary>
    /// Local, per peer. Switches every electric object off once per outage, either all at once
    /// (local trip) or as a blackout wave rolling out from the power station (grid outage).
    /// </summary>
    private void ApplyPowerOffLocal(bool gridOutage)
    {
        if (_electricOffApplied || _isPowerOn.Value) return;
        _electricOffApplied = true;

        if (gridOutage && gridOutageWaveDuration > 0f && isActiveAndEnabled)
        {
            _powerOffWaveCoroutine = StartCoroutine(PowerOffWave());
            return;
        }

        foreach (var electricObject in electricObjects)
        {
            if (electricObject != null)
                electricObject.OnElectricityTurnOff?.Invoke();
        }
    }

    /// <summary>Stops any pending deferral or blackout wave (power came back on).</summary>
    private void StopPendingPowerOff()
    {
        if (_deferredPowerOffCoroutine != null)
        {
            StopCoroutine(_deferredPowerOffCoroutine);
            _deferredPowerOffCoroutine = null;
        }

        if (_powerOffWaveCoroutine != null)
        {
            StopCoroutine(_powerOffWaveCoroutine);
            _powerOffWaveCoroutine = null;
        }
    }

    /// <summary>
    /// Grid outage only: switches electric objects off in order of distance from the wave origin
    /// (the power station by default), spread over <see cref="gridOutageWaveDuration"/>, so players
    /// see the dark arrive from the station instead of only their booth tripping.
    /// </summary>
    private IEnumerator PowerOffWave()
    {
        Vector3 origin = ResolveGridOutageWaveOrigin();

        var ordered = new System.Collections.Generic.List<(ElectricObject obj, float dist)>();
        float minDist = float.MaxValue;
        float maxDist = 0f;
        foreach (var electricObject in electricObjects)
        {
            if (electricObject == null) continue;
            float d = Vector3.Distance(origin, electricObject.transform.position);
            ordered.Add((electricObject, d));
            minDist = Mathf.Min(minDist, d);
            maxDist = Mathf.Max(maxDist, d);
        }
        ordered.Sort((a, b) => a.dist.CompareTo(b.dist));

        float range = Mathf.Max(maxDist - minDist, 0.001f);
        float elapsed = 0f;
        int next = 0;

        while (next < ordered.Count)
        {
            while (next < ordered.Count
                   && (ordered[next].dist - minDist) / range * gridOutageWaveDuration <= elapsed)
            {
                if (ordered[next].obj != null)
                    ordered[next].obj.OnElectricityTurnOff?.Invoke();
                next++;
            }

            if (next >= ordered.Count) break;
            yield return null;
            elapsed += Time.deltaTime;
        }

        _powerOffWaveCoroutine = null;
    }

    private Vector3 ResolveGridOutageWaveOrigin()
    {
        if (gridOutageWaveOrigin == null)
        {
            PowerStationPowerVisuals station = FindFirstObjectByType<PowerStationPowerVisuals>();
            if (station != null) gridOutageWaveOrigin = station.transform;
        }

        return gridOutageWaveOrigin != null ? gridOutageWaveOrigin.position : transform.position;
    }

    /// <summary>
    /// Waits one frame before applying a power-off seen through the NetworkVariable, so the
    /// <c>_requiresFuseBoxRestore</c> delta from the same server tick has arrived and the right
    /// style (wave vs. instant) is used. NGO applies <c>_isPowerOn</c> first (field order).
    /// </summary>
    private IEnumerator DeferredPowerOff()
    {
        yield return null;
        _deferredPowerOffCoroutine = null;
        ApplyPowerOffLocal(_requiresFuseBoxRestore.Value);
    }

    // ------------------------------------------------------------------
    // NetworkVariable change callbacks (handle late-joining clients)
    // ------------------------------------------------------------------

    private void OnFuseRequirementChanged(bool previous, bool current)
    {
        // No visual response needed — consumers poll RequiresFuseBoxRestore directly.
    }

    private void OnPowerStateChanged(bool previous, bool current)
    {
        // Snap late-joining clients to the correct visual state without SFX.
        if (current)
        {
            StopPendingPowerOff();
            _electricOffApplied = false;

            foreach (var electricObject in electricObjects)
            {
                if (electricObject != null)
                    electricObject.OnElectricityTurnOn?.Invoke();
            }

            // Fires locally on every client (this callback runs wherever the NetworkVariable
            // is readable, i.e. everyone) — safe hook for day-specific controllers (e.g. Day_03)
            // to complete their local "fix the power outage" objective/threat.
            OnPowerRestoredAllClients?.Invoke();
        }
        else
        {
            if (_electricOffApplied || _deferredPowerOffCoroutine != null) return;

            if (isActiveAndEnabled)
                _deferredPowerOffCoroutine = StartCoroutine(DeferredPowerOff());
            else
                ApplyPowerOffLocal(_requiresFuseBoxRestore.Value);
        }
    }
}
