using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks whether the local player is currently drunk.
/// The server is the sole writer; all clients replicate via NetworkVariable.
/// Call <see cref="SetDrunk"/> from any context — it routes through a ServerRpc when not on the server.
/// Drinks (e.g. vodka sips) are counted with <see cref="RegisterDrinkServer"/>: after
/// <see cref="drinksToGetDrunk"/> drinks the player becomes drunk, and sobers up
/// <see cref="soberUpSeconds"/> after their last drink.
/// </summary>
public class PlayerDrunkState : NetworkBehaviour
{
    [Header("Drinking")]
    [Tooltip("Drinks needed before the player becomes drunk.")]
    [SerializeField, Min(1)] private int drinksToGetDrunk = 3;

    [Tooltip("Seconds after the last drink before the player sobers up and the drink count resets.")]
    [SerializeField, Min(1f)] private float soberUpSeconds = 30f;

    private readonly NetworkVariable<bool> _isDrunk = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server-only drink bookkeeping.
    private int _drinkCount;
    private float _soberUpTime = -1f;

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

    private void Update()
    {
        if (!IsServer || _soberUpTime < 0f || Time.time < _soberUpTime) return;

        _soberUpTime = -1f;
        _drinkCount = 0;
        _isDrunk.Value = false;
    }

    /// <summary>Sets drunk state. Routes through a ServerRpc when not on the server.</summary>
    public void SetDrunk(bool drunk)
    {
        if (IsServer)
            ApplyDrunkServer(drunk);
        else
            SetDrunkServerRpc(drunk);
    }

    /// <summary>
    /// Server-only. Counts one drink, refreshes the sober-up timer and makes the player drunk
    /// once <see cref="drinksToGetDrunk"/> is reached.
    /// </summary>
    public void RegisterDrinkServer()
    {
        if (!IsServer) return;

        _drinkCount++;
        _soberUpTime = Time.time + soberUpSeconds;
        if (_drinkCount >= drinksToGetDrunk)
            _isDrunk.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetDrunkServerRpc(bool drunk) => ApplyDrunkServer(drunk);

    private void ApplyDrunkServer(bool drunk)
    {
        _isDrunk.Value = drunk;
        if (drunk)
        {
            _soberUpTime = Time.time + soberUpSeconds;
        }
        else
        {
            _drinkCount = 0;
            _soberUpTime = -1f;
        }
    }

    private void HandleDrunkChanged(bool previous, bool current) =>
        OnDrunkChanged?.Invoke(current);
}
