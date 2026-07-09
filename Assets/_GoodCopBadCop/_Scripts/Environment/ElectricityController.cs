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

    /// <summary>Returns the current power state, readable by all clients.</summary>
    public bool IsPowerOn => _isPowerOn.Value;

    public override void OnNetworkSpawn()
    {
        _isPowerOn.OnValueChanged += OnPowerStateChanged;
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += StartCountdown;
        UIController.Instance.OnReportShown += PauseCountdown;
        UIController.Instance.OnReportHidden += ResumeCountdown;
    }

    private void OnDestroy()
    {
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

    [ContextMenu("Power On")]
    public void PowerOn()
    {
        if (!IsServer) return;

        _isPowerOn.Value = true;
        PowerOnClientRpc();

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
    // NetworkVariable change callback (handles late-joining clients)
    // ------------------------------------------------------------------

    private void OnPowerStateChanged(bool previous, bool current)
    {
        // Snap late-joining clients to the correct visual state without SFX.
        if (current)
        {
            foreach (var electricObject in electricObjects)
            {
                electricObject.OnElectricityTurnOn?.Invoke();
            }
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
