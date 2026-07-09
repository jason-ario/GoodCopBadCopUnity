using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages which hat is visible on the player character and syncs the state across all clients.
/// Each entry in <see cref="hats"/> maps an index to a hat child GameObject parented under the
/// Hats bone. Index -1 means no hat is worn.
///
/// Only the owning client may call <see cref="EquipHat"/> or <see cref="RemoveHat"/>.
/// The server sets the <see cref="NetworkVariable{T}"/> so every client (including late-joiners)
/// automatically reflects the correct visual state.
/// </summary>
public class PlayerHatController : NetworkBehaviour
{
    [Tooltip("Hat child GameObjects parented under the Hats bone, ordered by their hat index.\n" +
             "Index -1 means no hat. Index 0 is the first entry, index 1 the second, etc.")]
    [SerializeField] private GameObject[] hats = Array.Empty<GameObject>();

    /// <summary>
    /// Optional metadata for each hat (display name, preview sprite).
    /// When assigned, <c>hatData[i]</c> must correspond to <c>hats[i]</c>.
    /// </summary>
    [Tooltip("Optional HatData assets matching each entry in hats[] by index.")]
    [SerializeField] private HatData[] hatData = Array.Empty<HatData>();

    [Tooltip("Hat index to equip when the player first spawns. -1 means no hat.")]
    [SerializeField] private int _defaultHatIndex = -1;

    private readonly NetworkVariable<int> _equippedHatIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Index of the currently equipped hat, or -1 if none is worn.</summary>
    public int EquippedHatIndex => _equippedHatIndex.Value;

    /// <summary>The number of hat slots registered in the hats array.</summary>
    public int HatCount => hats.Length;

    /// <summary>Returns the <see cref="HatData"/> for the currently equipped hat, or null.</summary>
    public HatData CurrentHatData =>
        _equippedHatIndex.Value >= 0 && _equippedHatIndex.Value < hatData.Length
            ? hatData[_equippedHatIndex.Value]
            : null;

    /// <summary>
    /// Returns the <see cref="HatData"/> for the hat at <paramref name="index"/>,
    /// or null if no data asset was assigned for that slot.
    /// </summary>
    public HatData GetHatData(int index) =>
        (index >= 0 && index < hatData.Length) ? hatData[index] : null;

    /// <summary>
    /// Raised on every client (including the owner) whenever the equipped hat changes.
    /// Provides the new hat index (-1 = no hat).
    /// </summary>
    public event System.Action<int> OnHatChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _equippedHatIndex.OnValueChanged += OnHatIndexChanged;

        if (IsServer && _defaultHatIndex != -1)
            _equippedHatIndex.Value = _defaultHatIndex;
        else
            ApplyHatVisibility(_equippedHatIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _equippedHatIndex.OnValueChanged -= OnHatIndexChanged;
    }

    private void OnHatIndexChanged(int previous, int current)
    {
        ApplyHatVisibility(current);
        OnHatChanged?.Invoke(current);
    }

    private void ApplyHatVisibility(int index)
    {
        for (int i = 0; i < hats.Length; i++)
        {
            if (hats[i] != null)
                hats[i].SetActive(i == index);
        }
    }

    /// <summary>
    /// Equips the hat at <paramref name="hatIndex"/>.
    /// Pass <c>-1</c> to remove any equipped hat.
    /// Only the owning client may call this.
    /// </summary>
    public void EquipHat(int hatIndex)
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[PlayerHatController] EquipHat called on a non-owner client. Ignoring.");
            return;
        }

        EquipHatServerRpc(hatIndex);
    }

    /// <summary>Removes the currently equipped hat. Only the owner may call this.</summary>
    public void RemoveHat() => EquipHat(-1);

    [ServerRpc]
    private void EquipHatServerRpc(int hatIndex)
    {
        if (hatIndex < -1 || hatIndex >= hats.Length)
        {
            Debug.LogWarning($"[PlayerHatController] Invalid hat index {hatIndex}. " +
                             $"Valid range: -1 to {hats.Length - 1}.");
            return;
        }

        _equippedHatIndex.Value = hatIndex;
    }
}
