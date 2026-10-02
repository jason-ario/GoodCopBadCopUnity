using System;
using Steamworks;
using Unity.Collections;
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
///
/// Persistence: on spawn the owner reports a stable identity (SteamID, or a host/client fallback
/// outside Steam) and the server registers the reserve with <see cref="AmmoReserveSaveStore"/>,
/// which saves it in the day-start checkpoint and parks it when a client disconnects.
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
        {
            Local = this;
            RegisterSaveKeyServerRpc(BuildLocalSaveKey());
        }
    }

    public override void OnNetworkDespawn()
    {
        _pistol.OnValueChanged  -= HandlePistolChanged;
        _shotgun.OnValueChanged -= HandleShotgunChanged;
        _fuel.OnValueChanged    -= HandleFuelChanged;

        if (Local == this)
            Local = null;

        if (IsServer && !string.IsNullOrEmpty(_saveKey))
        {
            // A client leaving a running session keeps its ammo for the next checkpoint / rejoin.
            // A full shutdown (host leaving, scene teardown) has nothing to hand it to.
            bool sessionContinues = NetworkManager != null && !NetworkManager.ShutdownInProgress;
            AmmoReserveSaveStore.UnregisterLive(this, _saveKey, sessionContinues);
            _saveKey = null;
        }

        base.OnNetworkDespawn();
    }

    // ── Save identity ─────────────────────────────────────────────────────────

    /// <summary>Server-only. Identity this reserve is saved under, or null before the owner registers.</summary>
    private string _saveKey;

    /// <summary>
    /// Owner-side. SteamID when Steam is running; otherwise a host/client fallback so editor and
    /// non-Steam sessions still round-trip through the save.
    /// </summary>
    private string BuildLocalSaveKey()
    {
        try
        {
            if (SteamClient.IsValid)
                return $"steam:{SteamClient.SteamId.Value}";
        }
        catch (Exception)
        {
            // Steam not initialised in this session — fall through to the local key.
        }

        return IsHost ? "local:host" : "local:client";
    }

    [ServerRpc]
    private void RegisterSaveKeyServerRpc(FixedString64Bytes key)
    {
        if (!string.IsNullOrEmpty(_saveKey)) return;
        _saveKey = AmmoReserveSaveStore.RegisterLive(this, key.ToString());
    }

    /// <summary>Server-only. Current counts as save data under <paramref name="key"/>.</summary>
    public AmmoReserveSaveData CaptureSaveData(string key) => new()
    {
        Key = key,
        Pistol = _pistol.Value,
        Shotgun = _shotgun.Value,
        Fuel = _fuel.Value,
    };

    /// <summary>Server-only. Sets every count to the saved value (zero when <paramref name="data"/> is null), clamped to the carry limits.</summary>
    public void ServerSetFromSave(AmmoReserveSaveData data)
    {
        if (!IsServer) return;
        _pistol.Value  = Mathf.Clamp(data?.Pistol  ?? 0, 0, _maxPistolRounds);
        _shotgun.Value = Mathf.Clamp(data?.Shotgun ?? 0, 0, _maxShotgunShells);
        _fuel.Value    = Mathf.Clamp(data?.Fuel    ?? 0, 0, _maxFuel);
    }

    /// <summary>Server-only. Adds saved counts on top of the current ones, capped at the carry limits.</summary>
    public void ServerAddFromSave(AmmoReserveSaveData data)
    {
        if (!IsServer || data == null) return;
        Add(AmmoType.Pistol, data.Pistol);
        Add(AmmoType.Shotgun, data.Shotgun);
        Add(AmmoType.Fuel, data.Fuel);
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
