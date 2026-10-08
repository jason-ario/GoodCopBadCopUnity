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
///
/// Grid outage (fuse-box restore, e.g. Day 3):
///   While <see cref="IsGridOutage"/> is true the panel is dead: interacting does not open the
///   puzzle, the reticle verb changes, and the player gets an error notification pointing at the
///   power station (see <see cref="ShowDeadPanelFeedbackLocal"/>).
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

    [Header("Tutorial Arrow")]
    [Tooltip("Anchor point for tutorial arrows (e.g. Ocho's power-outage tutorial). Position this at " +
             "the TOP of the panel box — NOT this component's own root/pivot, which sits low and would " +
             "make the arrow render too close to the ground. Falls back to this transform if unassigned.")]
    [SerializeField] private Transform _arrowTarget;

    [Header("Grid Outage (Fuse-Box Restore)")]
    [Tooltip("Optional one-shot played when a player tries the panel during a grid outage " +
             "(ElectricityController.RequiresFuseBoxRestore). The error notification already " +
             "plays its own negative cue, so this is just an extra dead-panel 'thunk'.")]
    [SerializeField] private AudioClip _deadPanelSound;

    [Tooltip("Error notification shown when a player tries the panel during a grid outage. The panel " +
             "does not open, so players never solve a puzzle that can't restore power.")]
    [SerializeField] private string _deadPanelMessage = "No current reaching the panel. The fault is at the power station.";

    [Tooltip("Reticle verb shown on the panel during a grid outage (instead of 'Open').")]
    [SerializeField] private string _deadPanelInteractVerb = "Inspect";

    /// <summary>
    /// Fired on the server whenever any player tries the panel during a grid outage. Day
    /// controllers (e.g. <see cref="Day_03"/>) use it for a one-shot "wrong panel" reminder.
    /// </summary>
    public static event System.Action OnDeadPanelUsedServer;

    // ─── Network state ────────────────────────────────────────────────────────

    private NetworkVariable<bool> _isDoorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Bitmask of circuit switch states (bit i = <see cref="_switches"/>[i] is On). Server-owned;
    /// clients request changes via ServerRpc. Defaults to all On, matching the default power state.
    /// </summary>
    private NetworkVariable<int> _switchMask = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ─── Runtime state ────────────────────────────────────────────────────────

    private Coroutine _doorCoroutine;

    private int AllSwitchesOnMask =>
        _switches == null || _switches.Length >= 32 ? -1 : (1 << _switches.Length) - 1;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isDoorOpen.OnValueChanged  += OnDoorStateChanged;
        _switchMask.OnValueChanged  += OnSwitchMaskChanged;

        // Snap the door to its correct visual state for late-joining clients.
        SnapDoor(_isDoorOpen.Value);

        // Server seeds the switch mask from the current power state; everyone then snaps to it
        // (late-joining clients receive the mask with the spawn payload).
        if (IsServer)
            _switchMask.Value = IsPowerOn ? AllSwitchesOnMask : 0;

        SnapSwitchesToMask(_switchMask.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isDoorOpen.OnValueChanged -= OnDoorStateChanged;
        _switchMask.OnValueChanged -= OnSwitchMaskChanged;
    }

    // ─── Interactable override ────────────────────────────────────────────────

    protected override string DefaultInteractVerb => "Open";

    public override string GetInteractVerb(PlayerInteractionController player)
        => IsGridOutage && !string.IsNullOrEmpty(_deadPanelInteractVerb)
            ? _deadPanelInteractVerb
            : base.GetInteractVerb(player);

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (DiegeticViewController.IsAnyViewActive) return;

        // Grid outage: the panel is dead. Don't open the puzzle (the server would silently
        // reject the restore anyway); tell the player where the real fault is instead.
        if (IsGridOutage)
        {
            ShowDeadPanelFeedbackLocal();
            return;
        }

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
    /// True while power is out AND the outage can only be fixed at the power station's fuse box
    /// (e.g. Day 3's post-call outage). The panel is dead during this time.
    /// </summary>
    public bool IsGridOutage =>
        _electricityController != null
        && !_electricityController.IsPowerOn
        && _electricityController.RequiresFuseBoxRestore;

    /// <summary>
    /// Local feedback for trying the panel during a grid outage: dead-panel sound + error
    /// notification pointing at the power station. Also tells the server (see
    /// <see cref="OnDeadPanelUsedServer"/>). Called by <see cref="Interact"/> and by
    /// <see cref="ElectricPanelDiegeticController"/> when a grid outage hits while the view is open.
    /// </summary>
    public void ShowDeadPanelFeedbackLocal()
    {
        if (_audioSource != null && _deadPanelSound != null)
            _audioSource.PlayOneShot(_deadPanelSound);

        if (!string.IsNullOrEmpty(_deadPanelMessage))
            UIController.Instance?.ShowErrorNotification(_deadPanelMessage);

        NotifyDeadPanelUsedServerRpc();
    }

    /// <summary>World-space anchor tutorial arrows should point at — the top of the box.</summary>
    public Transform ArrowTarget => _arrowTarget != null ? _arrowTarget : transform;

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

        if (IsServer) _switchMask.Value = 0;

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
    public void OnPowerOn()
    {
        SyncSwitchesToPowerState();
        if (IsServer && IsPowerOn) _switchMask.Value = AllSwitchesOnMask;
    }

    /// <summary>
    /// Called by <see cref="ElectricPanelDiegeticController"/> right after the local player flipped
    /// <paramref name="sw"/> (already applied locally). Replicates the new state to everyone.
    /// </summary>
    public void NotifySwitchFlipped(CircuitSwitch sw)
    {
        int index = _switches != null ? System.Array.IndexOf(_switches, sw) : -1;
        if (index < 0 || index >= 32) return;
        SetSwitchStateServerRpc(index, sw.IsOn);
    }

    /// <summary>
    /// Called by <see cref="ElectricPanelDiegeticController"/> after it locally reset every switch
    /// to Off (failed attempt). Replicates the reset and the reset cue to other players.
    /// </summary>
    public void NotifySwitchesReset()
    {
        ResetSwitchesServerRpc();
        PlayAllSwitchesResetSoundRpc();
    }

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
    private void SetSwitchStateServerRpc(int index, bool isOn)
    {
        if (index < 0 || index >= 32) return;
        int bit = 1 << index;
        _switchMask.Value = isOn ? (_switchMask.Value | bit) : (_switchMask.Value & ~bit);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResetSwitchesServerRpc() => _switchMask.Value = 0;

    [Rpc(SendTo.NotMe)]
    private void PlayAllSwitchesResetSoundRpc() => PlayAllSwitchesResetSound();

    [ServerRpc(RequireOwnership = false)]
    private void RestorePowerServerRpc()
    {
        if (_electricityController == null)
        {
            Debug.LogWarning("[ElectricPanelController] RestorePower rejected: no ElectricityController assigned.", this);
            return;
        }

        // Blocked while a fuse-box-required outage (e.g. Day 3/4) is active — the player
        // must travel to the power station, find the fuses, and use the PowerSwitch there.
        if (_electricityController.RequiresFuseBoxRestore)
        {
            Debug.LogWarning("[ElectricPanelController] RestorePower rejected: current outage requires the fuse box.", this);
            return;
        }

        _electricityController.PowerOn();
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyDeadPanelUsedServerRpc()
    {
        if (!IsGridOutage) return;
        OnDeadPanelUsedServer?.Invoke();
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

    /// <summary>
    /// Applies only the bits that changed. The flipping player already applied its change locally,
    /// so its switches already match and nothing re-triggers; remote players see and hear the flip.
    /// </summary>
    private void OnSwitchMaskChanged(int oldMask, int newMask)
    {
        if (_switches == null) return;

        int changed = oldMask ^ newMask;
        for (int i = 0; i < _switches.Length && i < 32; i++)
        {
            if ((changed & (1 << i)) == 0 || _switches[i] == null) continue;
            _switches[i].ApplyNetworkState((newMask & (1 << i)) != 0, playSound: true);
        }
    }

    private void SnapSwitchesToMask(int mask)
    {
        if (_switches == null) return;

        for (int i = 0; i < _switches.Length && i < 32; i++)
        {
            CircuitSwitch sw = _switches[i];
            if (sw == null) continue;
            if ((mask & (1 << i)) != 0) sw.SetSwitchOn();
            else sw.ApplyNetworkState(false, playSound: false);
        }
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
