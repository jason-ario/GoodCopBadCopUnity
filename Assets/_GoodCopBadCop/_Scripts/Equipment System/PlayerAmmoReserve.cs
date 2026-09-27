using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player ammunition reserve (pistol rounds, shotgun shells, flamethrower fuel).
///
/// Ammo enters the reserve when the holder left-clicks a held <see cref="AmmoPickup"/>, and leaves
/// it when the player presses R to reload the equipped <see cref="IInventoryReloadable"/> weapon.
/// Counts are server-write <see cref="NetworkVariable{T}"/>s so every peer (and late joiners)
/// agrees; only the server may call <see cref="Add"/> / <see cref="Take"/>.
///
/// Lives on the root of Player.prefab. The owning client's instance is exposed as <see cref="Local"/>
/// for HUD code (<see cref="AmmoReserveHUD"/>).
/// </summary>
[DisallowMultipleComponent]
public class PlayerAmmoReserve : NetworkBehaviour
{
    [Header("Carry Limits")]
    [Tooltip("Maximum pistol rounds a player can carry in reserve.")]
    [SerializeField, Min(1)] private int _maxPistolRounds = 90;

    [Tooltip("Maximum shotgun shells a player can carry in reserve.")]
    [SerializeField, Min(1)] private int _maxShotgunShells = 30;

    [Tooltip("Maximum flamethrower fuel units a player can carry in reserve.")]
    [SerializeField, Min(1)] private int _maxFuel = 200;

    private readonly NetworkVariable<int> _pistol = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _shotgun = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _fuel = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>The reserve belonging to this machine's local player, or null before it spawns.</summary>
    public static PlayerAmmoReserve Local { get; private set; }

    /// <summary>Fired on every peer when a count changes: (type, previous, current).</summary>
    public event Action<AmmoType, int, int> OnAmountChanged;

    /// <summary>Fired on the owning client when an add was refused (fully or partially) because the reserve is full.</summary>
    public event Action<AmmoType> OnReserveFull;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _pistol.OnValueChanged  += HandlePistolChanged;
        _shotgun.OnValueChanged += HandleShotgunChanged;
        _fuel.OnValueChanged    += HandleFuelChanged;

        if (IsOwner)
            Local = this;
    }

    public override void OnNetworkDespawn()
    {
        _pistol.OnValueChanged  -= HandlePistolChanged;
        _shotgun.OnValueChanged -= HandleShotgunChanged;
        _fuel.OnValueChanged    -= HandleFuelChanged;

        if (Local == this)
            Local = null;

        base.OnNetworkDespawn();
    }

    private void HandlePistolChanged(int previous, int current)  => OnAmountChanged?.Invoke(AmmoType.Pistol, previous, current);
    private void HandleShotgunChanged(int previous, int current) => OnAmountChanged?.Invoke(AmmoType.Shotgun, previous, current);
    private void HandleFuelChanged(int previous, int current)    => OnAmountChanged?.Invoke(AmmoType.Fuel, previous, current);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Current amount of <paramref name="type"/> in reserve.</summary>
    public int Get(AmmoType type) => Variable(type).Value;

    /// <summary>Maximum amount of <paramref name="type"/> this player can carry.</summary>
    public int GetMax(AmmoType type) => type switch
    {
        AmmoType.Pistol  => _maxPistolRounds,
        AmmoType.Shotgun => _maxShotgunShells,
        AmmoType.Fuel    => _maxFuel,
        _                => 0,
    };

    /// <summary>How much more <paramref name="type"/> fits before the reserve is full.</summary>
    public int GetFreeSpace(AmmoType type) => Mathf.Max(0, GetMax(type) - Get(type));

    // ── Server mutations ──────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Adds up to <paramref name="amount"/> of <paramref name="type"/>, capped at the
    /// carry limit, and returns how much was actually accepted.
    /// </summary>
    public int Add(AmmoType type, int amount)
    {
        if (!IsServer || amount <= 0) return 0;

        NetworkVariable<int> variable = Variable(type);
        int accepted = Mathf.Min(amount, GetFreeSpace(type));
        if (accepted > 0)
            variable.Value += accepted;
        return accepted;
    }

    /// <summary>
    /// Server-only. Removes up to <paramref name="amount"/> of <paramref name="type"/> and returns
    /// how much was actually removed.
    /// </summary>
    public int Take(AmmoType type, int amount)
    {
        if (!IsServer || amount <= 0) return 0;

        NetworkVariable<int> variable = Variable(type);
        int taken = Mathf.Min(amount, variable.Value);
        if (taken > 0)
            variable.Value -= taken;
        return taken;
    }

    /// <summary>Server-only. Tells the owning client its reserve of <paramref name="type"/> is full.</summary>
    public void NotifyFull(AmmoType type)
    {
        if (!IsServer) return;
        NotifyFullClientRpc(type, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        });
    }

    [ClientRpc]
    private void NotifyFullClientRpc(AmmoType type, ClientRpcParams clientRpcParams = default)
        => RaiseFullLocal(type);

    /// <summary>Raises <see cref="OnReserveFull"/> locally (used for instant client-side feedback).</summary>
    public void RaiseFullLocal(AmmoType type) => OnReserveFull?.Invoke(type);

    // ── Server helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. True when <paramref name="clientId"/> is connected, is currently holding
    /// <paramref name="heldItem"/> (checked via the replicated <see cref="PlayerPickupController.HeldObjectRef"/>,
    /// since <c>HeldObject</c> is local-only), and has a <see cref="PlayerAmmoReserve"/>.
    /// </summary>
    public static bool TryGetForHolder(ulong clientId, NetworkObject heldItem, out PlayerAmmoReserve reserve)
    {
        reserve = null;
        if (heldItem == null || NetworkManager.Singleton == null) return false;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null) return false;

        PlayerPickupController ppc = playerObject.GetComponent<PlayerPickupController>();
        if (ppc == null || !ppc.HeldObjectRef.TryGet(out NetworkObject held) || held != heldItem) return false;

        reserve = playerObject.GetComponent<PlayerAmmoReserve>();
        return reserve != null;
    }

    private NetworkVariable<int> Variable(AmmoType type) => type switch
    {
        AmmoType.Shotgun => _shotgun,
        AmmoType.Fuel    => _fuel,
        _                => _pistol,
    };
}
