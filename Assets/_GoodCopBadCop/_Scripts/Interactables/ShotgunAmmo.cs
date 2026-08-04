using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A box of shotgun shells that can be picked up from a <see cref="Shotgun"/> container or found
/// loose in the world.
///
/// Tracks the number of shells remaining in the box as a networked value so all clients
/// see the correct count in the reticle text. Partially used boxes stay in the holder's hand;
/// fully depleted boxes are despawned by <see cref="Shotgun.ReloadServerRpc"/>.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → Shotgun Ammo.asset
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class ShotgunAmmo : PickableObject, IAmmoProvider
{
    /// <summary>Maximum shells a single box can carry.</summary>
    public const int MaxRoundsPerClip = 15;

    private readonly NetworkVariable<int> _roundsInClip = new(
        MaxRoundsPerClip,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current number of shells remaining in this box.</summary>
    public int RoundsInClip => _roundsInClip.Value;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _roundsInClip.Value;
    public float MaxAmmo => MaxRoundsPerClip;
    public event Action OnAmmoChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        UpdateInteractText(MaxRoundsPerClip);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _roundsInClip.OnValueChanged += HandleRoundsChanged;

        // Server initialises the authoritative count so late-joining clients replicate correctly.
        if (IsServer)
            _roundsInClip.Value = _roundsInClip.Value; // no-op but triggers initial sync

        // Sync text immediately — OnValueChanged won't fire when value equals the NetworkVariable default.
        UpdateInteractText(_roundsInClip.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _roundsInClip.OnValueChanged -= HandleRoundsChanged;
    }

    private void HandleRoundsChanged(int previous, int current)
    {
        UpdateInteractText(current);
        OnAmmoChanged?.Invoke();
    }

    private void UpdateInteractText(int rounds)
        => interactText = $"Shotgun Ammo ({rounds}/{MaxRoundsPerClip})";

    // ── Ammo consumption ──────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Deducts up to <paramref name="amount"/> shells from this box and returns
    /// the actual number deducted (capped at the remaining count).
    /// </summary>
    public int ConsumeRounds(int amount)
    {
        int actual = Mathf.Min(amount, _roundsInClip.Value);
        _roundsInClip.Value -= actual;
        return actual;
    }
}
