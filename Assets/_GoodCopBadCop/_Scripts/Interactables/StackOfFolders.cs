using Unity.Netcode;
using UnityEngine;

public class StackOfFolders : Interactable
{
    [SerializeField] private PickableItemData folder;

    /// <summary>
    /// When false the stack is locked and cannot be interacted with.
    /// Defaults to true so the stack works normally outside of Day 1.
    /// On Day 1, <see cref="Day_01"/> locks it at start and unlocks it when the folder
    /// tutorial step begins.
    /// </summary>
    private readonly NetworkVariable<bool> _isInteractable = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Locks or unlocks the stack. When locked, players cannot grab a folder from it.
    /// Safe to call from any client — forwards to the server if needed.
    /// </summary>
    public void SetInteractable(bool value)
    {
        // Guard: DayActivated can be called before NGO spawns scene NetworkObjects
        // (e.g. during a debug skip). Writing to the NetworkVariable before this object has
        // spawned is silently dropped by Netcode without erroring, which left this stack
        // stuck in whatever state it last held — mirrors the same guard on InkStamp.SetSlotInteractable.
        if (!IsSpawned) return;

        if (IsServer)
            _isInteractable.Value = value;
        else
            SetInteractableServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetInteractableServerRpc(bool value) => _isInteractable.Value = value;

    public override void Interact(PlayerInteractionController player)
    {
        if (!_isInteractable.Value) return;

        base.Interact(player);

        player.pickupController.SpawnAndPickUp(folder, transform);
    }
}
