using Unity.Netcode;
using UnityEngine.Events;

/// <summary>
/// Tracks whether the local player is currently drunk.
/// The server is the sole writer; all clients replicate via NetworkVariable.
/// Call <see cref="SetDrunk"/> from any context — it routes through a ServerRpc when not on the server.
/// </summary>
public class PlayerDrunkState : NetworkBehaviour
{
    private readonly NetworkVariable<bool> _isDrunk = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Whether the player is currently drunk.</summary>
    public bool IsDrunk => _isDrunk.Value;

    /// <summary>Fired on all clients when the drunk state changes. Argument is the new value.</summary>
    public UnityAction<bool> OnDrunkChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isDrunk.OnValueChanged += HandleDrunkChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isDrunk.OnValueChanged -= HandleDrunkChanged;
    }

    /// <summary>Sets drunk state. Routes through a ServerRpc when not on the server.</summary>
    public void SetDrunk(bool drunk)
    {
        if (IsServer)
            _isDrunk.Value = drunk;
        else
            SetDrunkServerRpc(drunk);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetDrunkServerRpc(bool drunk) => _isDrunk.Value = drunk;

    private void HandleDrunkChanged(bool previous, bool current) =>
        OnDrunkChanged?.Invoke(current);
}
