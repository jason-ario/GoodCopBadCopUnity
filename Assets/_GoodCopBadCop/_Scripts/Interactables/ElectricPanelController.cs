using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable entry point for the electrical panel puzzle.
///
/// Interaction flow:
///   1. Player interacts → door smoothly rotates to its open position + door sound plays on all
///      clients + the diegetic view opens for the interacting player.
///   2. Player solves the puzzle (all switches On + knob turned) → <see cref="RestorePower"/>
///      fires a ServerRpc that calls <see cref="ElectricityController.PowerOn"/>.
///   3. When the diegetic view closes, the door rotates back to its closed position.
///
/// Power-outage reset:
///   Add an <see cref="ElectricObject"/> component on this same GameObject and wire its
///   <c>OnElectricityTurnOff</c> UnityEvent to <see cref="OnPowerOff"/> and its
///   <c>OnElectricityTurnOn</c> UnityEvent to <see cref="OnPowerOn"/> in the Inspector.
///   This also registers the panel as an electric object so the <see cref="ElectricityController"/>
///   calls it automatically on every outage and restore.
///
/// Tripping the breaker:
///   While the power is already on, touching any circuit switch trips the breaker and cuts
///   power entirely (see <see cref="TripPower"/>), same as a real panel.
/// </summary>
public class ElectricPanelController : Interactable
{
    [Header("Door")]
    [Tooltip("The Door transform to rotate.")]
    [SerializeField] private Transform _door;

    [Tooltip("Marker whose local rotation represents the door fully closed.")]
    [SerializeField] private Transform _doorClosedRef;

    [Tooltip("Marker whose local rotation represents the door fully open.")]
    [SerializeField] private Transform _doorOpenRef;

    [Tooltip("Door rotation speed in degrees per second.")]
    [SerializeField] private float _doorRotateSpeed = 90f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _doorOpenSound;

    [Tooltip("One-shot cue played whenever every circuit switch resets to Off in one go " +
             "(power outage, or a failed puzzle attempt when the knob reaches On early).")]
    [SerializeField] private AudioClip   _allSwitchesResetSound;

    [Header("Puzzle")]
    [SerializeField] private ElectricPanelDiegeticController _diegeticController;
    [SerializeField] private ElectricityController           _electricityController;
    [SerializeField] private CircuitSwitch[]                 _switches;
    [SerializeField] private TurningNobController            _nob;

    [Tooltip("Tracks whether another player is currently using this panel's diegetic view.")]
    [SerializeField] private DiegeticOccupancy _occupancy;

    // ─── Network state ────────────────────────────────────────────────────────

    private NetworkVariable<bool> _isDoorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ─── Runtime state ────────────────────────────────────────────────────────

    private Coroutine _doorCoroutine;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isDoorOpen.OnValueChanged += OnDoorStateChanged;

        // Snap the door to its correct visual state for late-joining clients.
        SnapDoor(_isDoorOpen.Value);

        // Snap the switches to match the current power state for late-joining clients.
        SyncSwitchesToPowerState();
    }

    public override void OnNetworkDespawn()
    {
        _isDoorOpen.OnValueChanged -= OnDoorStateChanged;
    }

    // ─── Interactable override ────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (DiegeticViewController.IsAnyViewActive) return;

        if (_occupancy != null && !_occupancy.TryClaim(player)) return;

        // Request door open on the server — all clients will animate via NetworkVariable callback.
        OpenDoorServerRpc();

        // Open the diegetic view immediately for the interacting player (local only).
        _diegeticController?.Open(player);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>True when the panel's electricity controller currently has power on.</summary>
    public bool IsPowerOn => _electricityController != null && _electricityController.IsPowerOn;

    /// <summary>
    /// Resets all circuit switches to Off and snaps the knob to its Off position.
    /// Wire this to <see cref="ElectricObject.OnElectricityTurnOff"/> in the Inspector.
    /// </summary>
    public void OnPowerOff()
    {
        if (_switches != null)
            foreach (CircuitSwitch sw in _switches)
                sw?.SetSwitchOff();

        _nob?.SnapToOff();
        PlayAllSwitchesResetSound();

        // If the local player is currently inside this view, close it.
        if (DiegeticViewController.Current == _diegeticController)
            _diegeticController?.Close();
    }

    /// <summary>
    /// Snaps all circuit switches to On to match the current power state. Wire this to
    /// <see cref="ElectricObject.OnElectricityTurnOn"/> in the Inspector so the switches always
    /// visually reflect that power is flowing (e.g. right after the puzzle is solved, or for
    /// late-joining clients when power is already on).
    /// </summary>
    public void OnPowerOn() => SyncSwitchesToPowerState();

    /// <summary>
    /// Called by <see cref="ElectricPanelDiegeticController"/> when the player messes with a
    /// circuit switch while the power is already on. Trips the breaker and cuts power entirely.
    /// </summary>
    public void TripPower() => TripPowerServerRpc();

    /// <summary>
    /// Called by <see cref="ElectricPanelDiegeticController"/> when the puzzle is solved.
    /// Sends a ServerRpc to restore power.
    /// </summary>
    public void RestorePower() => RestorePowerServerRpc();

    /// <summary>
    /// Called by <see cref="ElectricPanelDiegeticController.OnClosed"/> when the player
    /// exits the view. Requests the door to close on all clients and releases occupancy.
    /// </summary>
    public void OnViewClosed()
    {
        CloseDoorServerRpc();
        _occupancy?.Release();
    }

    /// <summary>
    /// Plays <see cref="_allSwitchesResetSound"/> once. Called whenever every circuit switch
    /// resets to Off in one go — on a power outage (<see cref="OnPowerOff"/>) or when
    /// <see cref="ElectricPanelDiegeticController"/> resets the panel after a failed attempt.
    /// </summary>
    public void PlayAllSwitchesResetSound()
    {
        if (_audioSource != null && _allSwitchesResetSound != null)
            _audioSource.PlayOneShot(_allSwitchesResetSound);
    }

    // ─── ServerRpcs ──────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void OpenDoorServerRpc() => _isDoorOpen.Value = true;

    [ServerRpc(RequireOwnership = false)]
    private void CloseDoorServerRpc() => _isDoorOpen.Value = false;

    [ServerRpc(RequireOwnership = false)]
    private void RestorePowerServerRpc()
    {
        if (_electricityController == null) return;

        // Blocked while a fuse-box-required outage (e.g. Day 3/4) is active — the player
        // must travel to the power station, find the fuses, and use the PowerSwitch there.
        if (_electricityController.RequiresFuseBoxRestore) return;

        _electricityController.PowerOn();
    }

    [ServerRpc(RequireOwnership = false)]
    private void TripPowerServerRpc()
    {
        if (_electricityController == null) return;
        if (!_electricityController.IsPowerOn) return;

        _electricityController.PowerOff();
    }

    // ─── NetworkVariable callback ─────────────────────────────────────────────

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        if (_doorCoroutine != null)
            StopCoroutine(_doorCoroutine);

        Transform target = newValue ? _doorOpenRef : _doorClosedRef;
        _doorCoroutine = StartCoroutine(RotateDoorTo(target));

        if (newValue && _audioSource != null && _doorOpenSound != null)
            _audioSource.PlayOneShot(_doorOpenSound);
    }

    // ─── Door animation ───────────────────────────────────────────────────────

    private IEnumerator RotateDoorTo(Transform target)
    {
        if (_door == null || target == null) yield break;

        Quaternion startRot = _door.localRotation;
        Quaternion endRot   = target.localRotation;
        float angle         = Quaternion.Angle(startRot, endRot);

        if (angle < 0.5f)
        {
            _door.localRotation = endRot;
            _doorCoroutine = null;
            yield break;
        }

        float duration = angle / _doorRotateSpeed;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _door.localRotation = Quaternion.Slerp(startRot, endRot, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        _door.localRotation = endRot;
        _doorCoroutine = null;
    }

    private void SnapDoor(bool open)
    {
        if (_door == null) return;
        Transform target = open ? _doorOpenRef : _doorClosedRef;
        if (target != null)
            _door.localRotation = target.localRotation;
    }

    private void SyncSwitchesToPowerState()
    {
        if (_switches == null) return;

        bool isOn = IsPowerOn;
        foreach (CircuitSwitch sw in _switches)
        {
            if (sw == null) continue;
            if (isOn) sw.SetSwitchOn();
            else sw.SetSwitchOff();
        }
    }
}
