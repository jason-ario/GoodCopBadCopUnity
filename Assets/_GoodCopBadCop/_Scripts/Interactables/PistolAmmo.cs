using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A pistol ammo clip that can be picked up from a <see cref="Pistol"/> container or found
/// loose in the world.
///
/// Tracks the number of rounds remaining in the clip as a networked value so all clients
/// see the correct count in the reticle text. Partially used clips stay in the holder's hand;
/// fully depleted clips are despawned by <see cref="Pistol.ReloadServerRpc"/>.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - "Item Data" field → PistolAmmo.asset
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PistolAmmo : PickableObject
{
    /// <summary>Maximum rounds a single clip can carry.</summary>
    public const int MaxRoundsPerClip = 30;

    private readonly NetworkVariable<int> _roundsInClip = new(
        MaxRoundsPerClip,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current number of rounds remaining in this clip.</summary>
    public int RoundsInClip => _roundsInClip.Value;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        UpdateInteractText(MaxRoundsPerClip);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _roundsInClip.OnValueChanged += OnRoundsChanged;

        // Server initialises the authoritative count so late-joining clients replicate correctly.
        if (IsServer)
            _roundsInClip.Value = _roundsInClip.Value; // no-op but triggers initial sync

        // Sync text immediately — OnValueChanged won't fire when value equals the NetworkVariable default.
        UpdateInteractText(_roundsInClip.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _roundsInClip.OnValueChanged -= OnRoundsChanged;
    }

    private void OnRoundsChanged(int previous, int current)
        => UpdateInteractText(current);

    private void UpdateInteractText(int rounds)
        => interactText = $"Pistol Ammo ({rounds}/{MaxRoundsPerClip})";

    // ── Ammo consumption ──────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Deducts up to <paramref name="amount"/> rounds from this clip and returns
    /// the actual number deducted (capped at the remaining count).
    /// </summary>
    public int ConsumeRounds(int amount)
    {
        int actual = Mathf.Min(amount, _roundsInClip.Value);
        _roundsInClip.Value -= actual;
        return actual;
    }
}
