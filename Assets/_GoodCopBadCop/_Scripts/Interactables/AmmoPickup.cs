using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Base class for ammunition pickups (<see cref="PistolAmmo"/>, <see cref="ShotgunAmmo"/>,
/// <see cref="FlamethrowerCannister"/>).
///
/// The pickup is a normal <see cref="PickableObject"/>: it can be carried, stowed, dropped, and
/// handed to other players. Left-clicking while holding it pours its contents into the holder's
/// <see cref="PlayerAmmoReserve"/>. If the reserve fills before the pickup is empty, the
/// remainder stays in the pickup (networked and saved), so it can be used later or shared.
/// A fully drained pickup is despawned.
///
/// Prefab requirements: NetworkObject, NetworkTransform, HighlightEffect, ParentConstraint,
/// a collider on the Interactable layer, and the matching "Item Data" asset.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public abstract class AmmoPickup : PickableObject, IAmmoProvider
{
    /// <summary>Which reserve this pickup feeds.</summary>
    public abstract AmmoType AmmoType { get; }

    /// <summary>Maximum (and initial) amount this pickup holds.</summary>
    public abstract int Capacity { get; }

    /// <summary>Name shown in the reticle interact text.</summary>
    protected abstract string DisplayName { get; }

    [Header("Ammo Pickup")]
    [Tooltip("Optional sound played locally when ammo is loaded into the reserve.")]
    [SerializeField] private AudioClip _loadSound;

    /// <summary>-1 means "untouched" and resolves to <see cref="Capacity"/>.</summary>
    private readonly NetworkVariable<int> _amount = new(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Amount remaining in this pickup.</summary>
    public int Amount => _amount.Value < 0 ? Capacity : _amount.Value;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => Amount;
    public float MaxAmmo => Capacity;
    public event Action OnAmmoChanged;

    // ── Save ──────────────────────────────────────────────────────────────────

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = Amount;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _amount.Value = Mathf.Clamp(Mathf.RoundToInt(data.ResourceAmount), 0, Capacity);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        UpdateInteractText(Capacity);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _amount.OnValueChanged += HandleAmountChanged;

        if (IsServer && _amount.Value < 0)
            _amount.Value = Capacity;

        UpdateInteractText(Amount);
    }

    public override void OnNetworkDespawn()
    {
        _amount.OnValueChanged -= HandleAmountChanged;
        base.OnNetworkDespawn();
    }

    private void HandleAmountChanged(int previous, int current)
    {
        UpdateInteractText(Amount);
        OnAmmoChanged?.Invoke();
    }

    private void UpdateInteractText(int amount)
        => interactText = $"{DisplayName} ({amount}/{Capacity})";

    // ── Use (LMB while held) ──────────────────────────────────────────────────

    /// <summary>
    /// Owner-side LMB. Asks the server to move this pickup's contents into the holder's reserve.
    /// A full reserve is reported immediately (no round-trip) and the request is skipped.
    /// </summary>
    /// <summary>LMB loads the ammo into the player's reserve; nothing when empty.</summary>
    public override string GetHeldUseVerb() => Amount <= 0 ? null : "Load ammo";

    public override void OnStartUse()
    {
        base.OnStartUse();

        if (Amount <= 0) return;

        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        if (reserve != null && reserve.GetFreeSpace(AmmoType) <= 0)
        {
            reserve.RaiseFullLocal(AmmoType);
            return;
        }

        if (_loadSound != null)
            SFXController.Instance.Play(_loadSound);

        LoadIntoReserveServerRpc();
    }

    /// <summary>
    /// Server: validates the sender is holding this pickup, transfers as much as fits into their
    /// reserve, keeps any remainder in the pickup, and despawns it (via the holder) once empty.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void LoadIntoReserveServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!PlayerAmmoReserve.TryGetForHolder(clientId, NetworkObject, out PlayerAmmoReserve reserve))
        {
            Debug.LogWarning($"[{GetType().Name}] LoadIntoReserveServerRpc: client {clientId} is not holding this pickup.");
            return;
        }

        int current = Amount;
        if (current <= 0) return;

        int accepted = reserve.Add(AmmoType, current);
        int remaining = current - accepted;
        _amount.Value = remaining;

        if (remaining > 0)
        {
            reserve.NotifyFull(AmmoType);
            return;
        }

        DestroyHeldPickupClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    /// <summary>
    /// Received only by the holder once the pickup is empty. Unequips it through
    /// <see cref="PlayerPickupController.DestroyEquippedItem"/> (which also despawns it) so the
    /// hand, arm containers, and inventory slot are cleared on the holder's machine.
    /// </summary>
    [ClientRpc]
    private void DestroyHeldPickupClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = PlayerInstance.Instance != null
            ? PlayerInstance.Instance.PlayerPickupController
            : null;

        if (ppc != null && ppc.HeldObject == this)
            ppc.DestroyEquippedItem();
        else
            DespawnServerRpc();
    }
}
