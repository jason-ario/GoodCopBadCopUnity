using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ElectricityController : NetworkBehaviour
{
    [SerializeField] private ElectricObject[] electricObjects;
    [SerializeField] private AudioClip powerOffSound;
    [SerializeField] private AudioClip powerOnSound;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private Vector2 powerOutageRandomTime = new Vector2(60, 120);

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
        PowerOffClientRpc();
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
        PowerOnClientRpc();

        if (wasFuseOutage)
            OnFuseOutageResolved?.Invoke();

        if (enablePowerOutage)
            StartCoroutine(WaitAndShutDown());
    }

    // ------------------------------------------------------------------
    // Client RPCs — run on every client including the host
    // ------------------------------------------------------------------

    [ClientRpc]
    private void PowerOffClientRpc()
    {
        if (_powerOffCoroutine != null)
            StopCoroutine(_powerOffCoroutine);

        _powerOffCoroutine = StartCoroutine(PowerOffCoroutine());
    }

    [ClientRpc]
    private void PowerOnClientRpc()
    {
        // Cancel any pending power-off coroutine so its delayed OnElectricityTurnOff
        // does not fire after the power has already been restored.
        if (_powerOffCoroutine != null)
        {
            StopCoroutine(_powerOffCoroutine);
            _powerOffCoroutine = null;
        }

        foreach (var electricObject in electricObjects)
        {
            electricObject.OnElectricityTurnOn?.Invoke();
        }

        sfxSource.PlayOneShot(powerOnSound);
    }

    private IEnumerator PowerOffCoroutine()
    {
        sfxSource.PlayOneShot(powerOffSound);

        yield return new WaitForSeconds(2f);

        foreach (var electricObject in electricObjects)
        {
            electricObject.OnElectricityTurnOff?.Invoke();
        }

        _powerOffCoroutine = null;
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
            foreach (var electricObject in electricObjects)
            {
                electricObject.OnElectricityTurnOn?.Invoke();
            }

            // Fires locally on every client (this callback runs wherever the NetworkVariable
            // is readable, i.e. everyone) — safe hook for day-specific controllers (e.g. Day_03)
            // to complete their local "fix the power outage" objective/threat.
            OnPowerRestoredAllClients?.Invoke();
        }
        else
        {
            foreach (var electricObject in electricObjects)
            {
                electricObject.OnElectricityTurnOff?.Invoke();
            }
        }
    }
}
