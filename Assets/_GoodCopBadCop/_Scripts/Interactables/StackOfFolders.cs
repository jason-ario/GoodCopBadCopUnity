using Unity.Netcode;
using UnityEngine;

public class StackOfFolders : Interactable
{
    [SerializeField] private PickableItemData folder;
    [SerializeField] private string[] alreadyHaveFolderTutorialBarks;

    private readonly NetworkVariable<bool> _folderGrabbedAlready = new NetworkVariable<bool>(
        false,
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

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_folderGrabbedAlready.Value)
        {
            TutorialManager.Instance.ShowTutorialText(alreadyHaveFolderTutorialBarks[UnityEngine.Random.Range(0, alreadyHaveFolderTutorialBarks.Length)]);
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
