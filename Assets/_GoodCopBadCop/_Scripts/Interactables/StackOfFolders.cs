using Unity.Netcode;
using UnityEngine;

public class StackOfFolders : Interactable
{
    [SerializeField] private PickableItemData folder;

    private readonly NetworkVariable<bool> _folderGrabbedAlready = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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

    private void Start()
    {
        SuspectController.Instance.OnTakeFolder += OnShiftEnd;
    }

    private void OnDestroy()
    {
        if (SuspectController.Instance != null)
            SuspectController.Instance.OnTakeFolder -= OnShiftEnd;
    }

    private void OnShiftEnd()
    {
        if (IsServer)
            _folderGrabbedAlready.Value = false;
    }

    /// <summary>
    /// Locks or unlocks the stack. When locked, players cannot grab a folder from it.
    /// Safe to call from any client — forwards to the server if needed.
    /// </summary>
    public void SetInteractable(bool value)
    {
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

        if (_folderGrabbedAlready.Value)
        {
            return;
        }

        SetFolderGrabbedServerRpc();
        player.pickupController.SpawnAndPickUp(folder, transform);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetFolderGrabbedServerRpc()
    {
        _folderGrabbedAlready.Value = true;
    }
}
