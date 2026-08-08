using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates the bunker door wheel's spin (local Z rotation) across all clients.
/// The client actively dragging the wheel (see <see cref="DoorWheelDiegeticController"/>)
/// drives the rotation locally for a responsive feel, then publishes the new Z angle here.
/// The value is routed through the server as a <see cref="NetworkVariable{T}"/> (matching the
/// pattern used by <see cref="BunkerDoorController"/>) so every other client's wheel visually
/// spins in lockstep, and late joiners snap to the correct angle on spawn.
/// Requires a <see cref="NetworkObject"/> on this GameObject.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class DoorWheelNetworkSync : NetworkBehaviour
{
    [Tooltip("The Transform that visually spins — should be the same Transform assigned to " +
             "DoorWheelDiegeticController's wheel transform field.")]
    [SerializeField] private Transform _wheelTransform;

    private readonly NetworkVariable<float> _wheelZRotation = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Set true by the local client while it is the one actively dragging the wheel, so this
    /// component doesn't reapply the network-replicated echo of its own just-set rotation
    /// (which would otherwise cause a one-frame stutter/rubber-band on the dragging client).
    /// </summary>
    public bool IsLocalAuthority { get; set; }

    public override void OnNetworkSpawn()
    {
        _wheelZRotation.OnValueChanged += OnWheelZRotationChanged;

        // Snap late-joining/newly-spawned clients to the authoritative angle immediately.
        ApplyToTransform(_wheelZRotation.Value);
    }

    public override void OnNetworkDespawn()
    {
        _wheelZRotation.OnValueChanged -= OnWheelZRotationChanged;
    }

    /// <summary>
    /// Publishes a new wheel Z rotation. Can be called from any client — routes through the
    /// server, which is the sole writer of the underlying <see cref="NetworkVariable{T}"/>.
    /// </summary>
    public void PublishWheelZRotation(float z)
    {
        if (IsServer)
            _wheelZRotation.Value = z;
        else
            PublishWheelZRotationServerRpc(z);
    }

    [Rpc(SendTo.Server)]
    private void PublishWheelZRotationServerRpc(float z) => _wheelZRotation.Value = z;

    private void OnWheelZRotationChanged(float previous, float current)
    {
        if (IsLocalAuthority) return;
        ApplyToTransform(current);
    }

    private void ApplyToTransform(float z)
    {
        if (_wheelTransform == null) return;

        Vector3 euler = _wheelTransform.localEulerAngles;
        euler.z = z;
        _wheelTransform.localEulerAngles = euler;
    }
}
